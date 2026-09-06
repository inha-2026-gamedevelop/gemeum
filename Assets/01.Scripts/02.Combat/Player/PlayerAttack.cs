// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// 플레이어의 기본 공격. 판정은 전부 서버에서 하고 클라이언트는 요청만 한다.
    /// 입력을 그대로 믿으면 사람마다 다른 적이 죽는다.
    /// 근접과 원거리는 선딜, 소모, 판정 방식이 달라 무기에서 경로를 갈라 쓴다
    /// </summary>
    public class PlayerAttack : NetworkBehaviour, IPlayerAction
    {
        /****************************************
        *                Types
        ****************************************/

        // 무기에서 뽑아낸 이번 공격의 조건. 게이트와 타이밍이 같은 값을 보게 묶어 둔다
        private readonly struct AttackProfile
        {
            public readonly AttackReach Reach;
            public readonly float       StartupDuration;
            public readonly float       RecoveryDuration;
            public readonly float       StaminaCost;

            public AttackProfile(AttackReach reach, float startupDuration, float recoveryDuration, float staminaCost)
            {
                Reach            = reach;
                StartupDuration  = startupDuration;
                RecoveryDuration = recoveryDuration;
                StaminaCost      = staminaCost;
            }

            public bool HasAttack => (Reach != AttackReach.None);
        }

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("공격 입력 키. 조작 변경은 씬이 아니라 여기서 한다")]
        [SerializeField] private KeyCode _attackKey = KeyCode.Mouse0;

        [Tooltip("판정 대상 레이어. 비워 두면 아무것도 맞지 않는다")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("원거리 무기가 쏘는 화살 프리팹. NetworkManager의 스폰 목록에도 등록되어 있어야 한다")]
        [SerializeField] private ArrowProjectile _arrowPrefab;

        private PlayerStatus              _status;
        private PlayerWeapon              _weapon;
        private PlayerRollController      _roll;
        private ParryController           _parry;
        private ParryGauge                _gauge;
        private PlayerAnimationController _animation;
        private PlayerPresentation        _presentation;
        private LockOnController          _lockOn;

        // readonly로 두면 메서드 호출마다 방어 복사본이 만들어져 수락 기록이 남지 않는다
        private RequestIdTracker _requestIds = new RequestIdTracker();

        // 판정 시각과 다음 공격이 가능해지는 시각. 서버 시간 기준이다
        private double      _hitServerTime      = 0d;
        private double      _recoveryServerTime = 0d;
        private bool        _isSwinging         = false;
        private AttackReach _swingReach         = AttackReach.None;

        // 근접 콤보 단계(1~3). 후딜이 끝난 뒤 콤보 창 안에 재입력하면 다음 단계로,
        // 창을 놓치면 1단으로 되돌아간다. 근접이 아니면 항상 0(무의미)이다
        private int    _comboStage           = 0;
        private double _comboExpireServerTime = 0d;

        // TryBuildContext가 뽑아 CommitAction이 쓰는 이번 요청의 무기 값.
        // 커밋에서 다시 계산하면 그 사이에 무기가 바뀌었을 때 선딜과 소모가 어긋난다
        private AttackProfile _pendingProfile;

        private uint _nextRequestId = 1u;

        // 매번 새로 만들면 공격마다 GC 할당이 생긴다
        private readonly Collider[]        _hitBuffer  = new Collider[Constants.Combat.HIT_QUERY_BUFFER_SIZE];
        private readonly List<IDamageable> _hitTargets = new List<IDamageable>(Constants.Combat.HIT_QUERY_BUFFER_SIZE);

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 공격 동작 중인가. 구르기와 패링이 이 값을 보고 겹침을 막는다 </summary>
        public bool IsActionInProgress(double serverTime)
        {
            return (_isSwinging) || (serverTime < _recoveryServerTime);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _parry  = GetComponent<ParryController>();
            _gauge  = GetComponent<ParryGauge>();

            _animation    = GetComponent<PlayerAnimationController>();
            _presentation = GetComponent<PlayerPresentation>();
            _lockOn       = GetComponent<LockOnController>();
        }

        private void Update()
        {
            // 입력은 자기 캐릭터에서만 읽는다
            if ((!isLocalPlayer) || (!Input.GetKeyDown(_attackKey)))
            {
                return;
            }

            CmdRequestAttack(_nextRequestId);
            ++_nextRequestId;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestAttack(uint requestId)
        {
            ActionRejection rejection = PlayerActionPipeline.TryExecute(
                ref _requestIds, requestId, this, NetworkTime.time);

            if (rejection == ActionRejection.None)
            {
                return;
            }

            TargetActionRejected(rejection);
        }

        /****************************************
        *              Player Action
        ****************************************/

        /// <summary>
        /// 이번 공격의 게이트 입력을 만든다. 무기에서 뽑은 선딜, 후딜, 소모를 여기서 확정하고
        /// 커밋까지 들고 간다 - 같은 요청 안에서 두 번 계산하면 그 사이에 무기가 바뀔 수 있다
        /// </summary>
        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return false;
            }

            // 배분 전에는 무기 값이 의미가 없다. 게이트도 막지만 여기서 읽지 않는 것이 먼저다
            if (!_weapon.HasWeapon)
            {
                return false;
            }

            _pendingProfile = ResolveProfile(_weapon.Weapon, GameDB.Combat);

            // 방울처럼 기본 공격이 없는 무기는 요청 자체를 받지 않는다
            if (!_pendingProfile.HasAttack)
            {
                return false;
            }

            context = BuildContext(serverTime, _pendingProfile);
            return true;
        }

        public bool TrySpendResource(in PlayerActionContext context)
        {
            return _status.TrySpendStamina(context.ResourceCost);
        }

        public void CommitAction(in PlayerActionContext context)
        {
            double        now     = context.ServerTime;
            AttackProfile profile = _pendingProfile;

            _isSwinging         = true;
            _swingReach         = profile.Reach;
            _hitServerTime      = now + profile.StartupDuration;
            _recoveryServerTime = _hitServerTime + profile.RecoveryDuration;

            // 콤보는 근접에서만 돈다. 창 안에 재입력했으면 다음 단계로, 아니면 1단으로 되돌아간다.
            // 판정은 무기, 거리와 무관하게 항상 MeleeBasePower 하나를 쓴다 - 표현만 세 클립으로 갈린다
            _comboStage = (profile.Reach == AttackReach.Melee)
                ? (((_comboStage > 0) && (now <= _comboExpireServerTime)) ? ((_comboStage % Constants.Combat.MELEE_COMBO_STAGE_COUNT) + 1) : 1)
                : 0;
            _comboExpireServerTime = _recoveryServerTime + GameDB.Combat.MeleeComboWindowDuration;

            // 표현은 수락 시점에 알린다. 판정 시점까지 기다리면 선딜 동안 아무 동작도 나오지 않는다.
            // 무기 종류는 이미 SyncVar로 가 있으므로 값을 실어 보내지 않는다
            RpcAttackStarted(_comboStage);
        }

        /****************************************
        *              Client Rpc
        ****************************************/

        /// <summary>
        /// 공격이 수락됐음을 모든 화면에 알린다. 이걸 거치지 않으면 남의 공격이 내 화면에서 보이지 않는다.
        /// 판정과 무관한 표현 전용 통지다. comboStage는 근접이 아니면 0이고, 그때는 1단 클립으로 재생한다
        /// </summary>
        [ClientRpc]
        private void RpcAttackStarted(int comboStage)
        {
            if (_animation != null)
            {
                _animation.PlayAttack(comboStage);
            }
        }

        /// <summary>
        /// 요청이 거부된 사유를 요청자에게만 돌려준다.
        /// 조용히 버리면 사람은 "입력이 씹혔다"고 느끼고, 우리는 3인 테스트에서
        /// 그것이 지연인지 거부인지 구분할 수 없다
        /// </summary>
        [TargetRpc]
        private void TargetActionRejected(ActionRejection rejection)
        {
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /// <summary>
        /// 근접 판정 결과를 모든 화면에 알린다. 맞았는지 헛쳤는지는 서버만 아는데,
        /// 헛스윙 구분은 게이지 보존 규칙과 한 쌍이라 소리로 돌려줘야 한다.
        /// 표현 전용이고 판정은 이미 서버에서 끝나 있다
        /// </summary>
        [ClientRpc]
        private void RpcMeleeResult(bool didHit, bool wasCritical)
        {
            SoundKey key = (!didHit) ? SoundKey.MeleeWhiff
                         : (wasCritical) ? SoundKey.MeleeCritical
                         : SoundKey.MeleeHit;

            SfxPlayer.Play(key, transform.position, isLocalPlayer, netId);
        }

        /****************************************
        *              Server Side
        ****************************************/

        /// <summary>
        /// 선딜이 끝나는 시점에 한 번만 판정한다. 코루틴을 쓰지 않는 이유는
        /// 사망이나 런 중단에서 취소 지점을 따로 관리해야 하기 때문이다
        /// </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            if (!_isSwinging)
            {
                return;
            }

            // 휘두르는 도중 죽거나 멈추면 판정 없이 끝낸다
            if ((_status.IsDead) || (IsRunStopped()))
            {
                Cancel();
                return;
            }

            if (now < _hitServerTime)
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;

            if (_swingReach == AttackReach.Ranged)
            {
                FireArrow(combat);
            }
            else
            {
                ApplyMeleeHit(combat);
            }

            _isSwinging = false;
            _swingReach = AttackReach.None;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 무기가 판정 방식과 수치를 정한다. 캐릭터가 무기를 정하므로 곧 직업별 수치다 </summary>
        private static AttackProfile ResolveProfile(WeaponKind weapon, CombatConfigSO combat)
        {
            switch (weapon.ToReach())
            {
                case AttackReach.Melee:
                    return new AttackProfile(
                        AttackReach.Melee,
                        combat.MeleeStartupDuration,
                        combat.MeleeRecoveryDuration,
                        combat.MeleeStaminaCost);

                case AttackReach.Ranged:
                    return new AttackProfile(
                        AttackReach.Ranged,
                        combat.RangedStartupDuration,
                        combat.RangedRecoveryDuration,
                        combat.RangedStaminaCost);

                default:
                    // 방울, 치유와 버프 설계가 끝난 뒤 별도 작업으로 붙인다
                    return default;
            }
        }

        private PlayerActionContext BuildContext(double now, AttackProfile profile)
        {
            bool isOtherActionInProgress = ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(now)));

            RunPhase phase    = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool     isPaused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            return new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                _weapon.HasWeapon,
                ((_isSwinging) || (isOtherActionInProgress)),
                now,
                _recoveryServerTime,
                _status.Stamina,
                profile.StaminaCost);
        }

        /// <summary>
        /// 이번 공격이 향하는 평면 방향. transform.forward를 쓰면 안 된다 -
        /// 플레이어 루트는 Rigidbody가 회전을 막고 있고(Player.prefab FreezeRotation X,Y,Z)
        /// 코드에서도 루트를 돌리는 곳이 없어 항상 월드 +Z로 고정된다.
        /// 방향은 자식 VisualPivot에만 적용되므로, 서버가 매 틱 확정하는
        /// PlayerPresentation의 값이 판정이 쓸 수 있는 유일한 방향 소스다.
        /// 이동 방향이 아니라 AimDirection을 읽는다 - 락온이 붙으면 둘이 갈라지고,
        /// 그때 공격은 걸어가는 쪽이 아니라 잠근 대상 쪽으로 나가야 한다
        /// </summary>
        [Server]
        private Vector3 ResolveAimForward()
        {
            if (_presentation == null)
            {
                Debug.LogError("[PlayerAttack] PlayerPresentation이 없어 조준 방향을 정할 수 없다", this);
                return transform.forward;
            }

            Vector2 facing = _presentation.AimDirection;

            // ResolveFacing이 마지막 유효 방향을 유지하므로 정상 경로에서는 0이 나오지 않는다.
            // 그래도 0이면 정규화가 NaN이 되어 판정이 전부 조용히 빗나가므로 여기서 막는다
            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                Debug.LogWarning("[PlayerAttack] 조준 방향이 비어 있어 이번 공격은 루트 정면으로 나간다", this);
                return transform.forward;
            }

            return new Vector3(facing.x, 0f, facing.y).normalized;
        }

        // 정면 부채꼴 안에 있는 대상만 맞춘다. 구를 통째로 쓰면 등 뒤의 적도 맞는다
        [Server]
        private void ApplyMeleeHit(CombatConfigSO combat)
        {
            Vector3 origin  = transform.position;
            Vector3 forward = ResolveAimForward();
            float   cosHalf = Mathf.Cos(combat.MeleeAngle * 0.5f * Mathf.Deg2Rad);

            // 배수를 휘두르기 한 번당 하나로 뽑아 부채꼴에 걸린 대상이 모두 같은 값을 받게 한다.
            // 게이지는 검 전용이고 근접 판정에 오는 무기는 검뿐이라 여기서만 본다
            bool  isCritical = IsCriticalReady(combat);
            float multiplier = (isCritical) ? Random.Range(combat.CriticalMultiplierMin, combat.CriticalMultiplierMax) : 1f;
            int   hitCount   = 0;

            _hitTargets.Clear();

            int count = Physics.OverlapSphereNonAlloc(origin, combat.MeleeRange, _hitBuffer, _targetMask);

            for (int i = 0; i < count; ++i)
            {
                Collider hit = _hitBuffer[i];

                // 소유자 판별은 루트 비교가 맞다. 내 무기 콜라이더가 자식에 붙어 있어도 걸러야 한다.
                // 아래의 대상 해석과는 목적이 다른 판단이므로 둘을 섞지 않는다
                if ((hit == null) || (hit.transform.root == transform.root))
                {
                    continue;
                }

                Vector3 toTarget = (hit.transform.position - origin);
                toTarget.y       = 0f;

                if (toTarget.sqrMagnitude <= Mathf.Epsilon)
                {
                    continue;
                }

                if (Vector3.Dot(forward, toTarget.normalized) < cosHalf)
                {
                    continue;
                }

                // 맞은 콜라이더에서 위로 올라가며 처음 만나는 IDamageable이 이번 타격의 대상이다.
                // 루트에서 찾으면 보스 꼬리 같은 부위 콜라이더를 쳐도 본체가 맞는다
                if (!DamageTargetResolver.TryResolve(hit, out IDamageable damageable))
                {
                    continue;
                }

                if (damageable.IsDead)
                {
                    continue;
                }

                // 몸 하나에 콜라이더가 여럿이면 한 번 휘두르기에 여러 번 맞는다.
                // netId를 키로 쓰면 부위와 본체가 같은 NetworkIdentity라 둘 중 하나만 맞고,
                // 어느 쪽이 맞을지는 물리 질의의 순회 순서가 정해 결과가 비결정적이 된다.
                // 해석된 참조 자체를 키로 쓰면 부위가 몇 개든 각각 한 번씩만 맞는다
                if (_hitTargets.Contains(damageable))
                {
                    continue;
                }

                _hitTargets.Add(damageable);

                damageable.TakeDamage(new DamageInfo(
                    combat.MeleeBasePower * multiplier,
                    netId,
                    (isCritical) ? HitStrength.Critical : HitStrength.Light));

                ++hitCount;
            }

            // 허공을 갈랐으면 게이지를 돌려준다. 패링을 세 번 성공해 얻은 보상이 빗나감 한 번에
            // 사라지면 게이지를 쓰지 않고 아끼게 된다
            if ((isCritical) && (hitCount > 0))
            {
                _gauge.ServerTryConsume();
            }

            RpcMeleeResult(hitCount > 0, (isCritical) && (hitCount > 0));
        }

        /// <summary> 다음 근접 공격이 치명의 일격인가. 소비하지 않고 보기만 한다 </summary>
        [Server]
        private bool IsCriticalReady(CombatConfigSO combat)
        {
            return ((_gauge != null) && (_gauge.Gauge >= combat.ParryGaugeMax));
        }

        /// <summary>
        /// 화살을 띄운다. 판정은 화살이 서버 틱을 돌며 직접 한다.
        /// 락온 중이면 발사각을 조준점 쪽으로 최대 12도만 당기고, 아니면
        /// 비락온 규칙(발사각 보정 없이 구형 명중 보조만)이 그대로 적용된다
        /// </summary>
        [Server]
        private void FireArrow(CombatConfigSO combat)
        {
            if (_arrowPrefab == null)
            {
                Debug.LogError("[PlayerAttack] 화살 프리팹이 비어 있어 원거리 공격이 나가지 않는다", this);
                return;
            }

            ArrowConfigSO arrowConfig = GameDB.Arrow;

            Vector3 forward = ResolveAimForward();

            // 발밑이나 제 몸 안에서 나가면 첫 틱부터 자기 자신 근처를 훑는다.
            // 총구를 보정 전 방향으로 잡는 이유는 조준점까지의 각도를 재려면 기준점이 먼저 있어야 해서다
            Vector3 origin = transform.position
                + (Vector3.up * combat.RangedMuzzleHeight)
                + (forward * combat.RangedMuzzleForward);

            uint guidanceTargetNetId = 0u;
            byte guidanceTargetPart  = 0;

            if (TryResolveLockedAim(arrowConfig, origin, forward, out Vector3 snapped))
            {
                forward             = snapped;
                guidanceTargetNetId = _lockOn.TargetNetId;
                guidanceTargetPart  = _lockOn.TargetPartIndex;
            }

            ArrowProjectile arrow = Instantiate(_arrowPrefab, origin, Quaternion.LookRotation(forward));
            arrow.ServerInitialize(
                forward, combat.RangedBasePower, netId, _targetMask.value, arrowConfig,
                guidanceTargetNetId, guidanceTargetPart);

            NetworkServer.Spawn(arrow.gameObject);
        }

        /// <summary>
        /// 락온 대상 쪽으로 발사각을 당길 수 있는지 발사 순간에 다시 확인한다.
        ///
        /// 12도는 탐색 반각 35도와 다른 값이다 - 탐색됐다고 발사 보정까지 허용되는 것은 아니다.
        /// 선딜 0.25초 동안 대상이 죽거나 숨거나 크게 움직였을 수 있으므로 생존, 거리, 시야, 각도를
        /// 전부 여기서 다시 본다. 실패하면 다른 적을 찾지 않고 겨눈 그대로 쏜다
        /// </summary>
        // 방향을 XZ로 눌러 버리지 않는다. 높낮이가 있는 지형과 공중 적을 위해 3D 벡터를 유지한다 -
        // ResolveAimForward가 돌려주는 평면 방향과 달라지는 유일한 자리다
        [Server]
        private bool TryResolveLockedAim(ArrowConfigSO arrow, Vector3 muzzle, Vector3 requested, out Vector3 desired)
        {
            desired = requested;

            if ((_lockOn == null) || (!_lockOn.IsLockedOn) || (arrow == null))
            {
                return false;
            }

            if (!_lockOn.ServerIsTargetValid(GameDB.Combat.LockOnHoldRange))
            {
                return false;
            }

            Vector3 toTarget = _lockOn.TargetAimPoint - muzzle;
            if (toTarget.sqrMagnitude <= 0f)
            {
                return false;
            }

            Vector3 direction = toTarget.normalized;

            if (Vector3.Angle(requested, direction) > arrow.AimAssistAngle)
            {
                return false;
            }

            // 총구 바로 앞에 벽이 있으면 카메라에서는 보이더라도 화살은 벽에 맞는다.
            // 그때까지 보정해 주면 벽을 뚫고 맞히는 것처럼 보인다
            if ((arrow.WorldMask != 0)
                && (Physics.Raycast(muzzle, direction, toTarget.magnitude, arrow.WorldMask, QueryTriggerInteraction.Ignore)))
            {
                return false;
            }

            desired = direction;
            return true;
        }

        [Server]
        private void Cancel()
        {
            _isSwinging         = false;
            _swingReach         = AttackReach.None;
            _recoveryServerTime = 0d;
            _comboStage         = 0;
        }

        private static bool IsRunStopped()
        {
            RunManager run = RunManager.ServerInstance;
            return ((run == null) || (run.IsPaused));
        }
    }
}
