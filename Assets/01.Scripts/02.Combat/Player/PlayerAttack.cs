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
    /// <summary> 플레이어의 기본 공격. 판정은 전부 서버에서 한다 </summary>
    public class PlayerAttack : NetworkBehaviour, IPlayerAction
    {
        /****************************************
        *                Types
        ****************************************/

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

        private RequestIdTracker _requestIds = new RequestIdTracker();

        // 판정 시각과 다음 공격이 가능해지는 시각. 서버 시간 기준이다
        private double      _hitServerTime      = 0d;
        private double      _recoveryServerTime = 0d;
        private bool        _isSwinging         = false;
        private AttackReach _swingReach         = AttackReach.None;

        // 근접 콤보 단계(1~3)
        private int    _comboStage           = 0;
        private double _comboExpireServerTime = 0d;

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

        /// <summary> 이번 공격의 게이트 입력을 만든다 </summary>
        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return false;
            }

            if (!_weapon.HasWeapon)
            {
                return false;
            }

            _pendingProfile = ResolveProfile(_weapon.Weapon, GameDB.Combat);

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

            _comboStage = (profile.Reach == AttackReach.Melee)
                ? (((_comboStage > 0) && (now <= _comboExpireServerTime)) ? ((_comboStage % Constants.Combat.MELEE_COMBO_STAGE_COUNT) + 1) : 1)
                : 0;
            _comboExpireServerTime = _recoveryServerTime + GameDB.Combat.MeleeComboWindowDuration;

            RpcAttackStarted(_comboStage);
        }

        /****************************************
        *              Client Rpc
        ****************************************/

        /// <summary> 공격이 수락됐음을 모든 화면에 알린다 </summary>
        [ClientRpc]
        private void RpcAttackStarted(int comboStage)
        {
            if (_animation != null)
            {
                _animation.PlayAttack(comboStage);
            }
        }

        /// <summary> 요청이 거부된 사유를 요청자에게만 돌려준다 </summary>
        [TargetRpc]
        private void TargetActionRejected(ActionRejection rejection)
        {
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /// <summary> 근접 판정 결과를 모든 화면에 알린다 </summary>
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

        /// <summary> 선딜이 끝나는 시점에 한 번만 판정한다 </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            if (!_isSwinging)
            {
                return;
            }

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

        /// <summary> 이번 공격이 향하는 평면 방향. PlayerPresentation의 AimDirection을 읽는다 </summary>
        [Server]
        private Vector3 ResolveAimForward()
        {
            if (_presentation == null)
            {
                Debug.LogError("[PlayerAttack] PlayerPresentation이 없어 조준 방향을 정할 수 없다", this);
                return transform.forward;
            }

            Vector2 facing = _presentation.AimDirection;

            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                Debug.LogWarning("[PlayerAttack] 조준 방향이 비어 있어 이번 공격은 루트 정면으로 나간다", this);
                return transform.forward;
            }

            return new Vector3(facing.x, 0f, facing.y).normalized;
        }

        [Server]
        private void ApplyMeleeHit(CombatConfigSO combat)
        {
            Vector3 origin  = transform.position;
            Vector3 forward = ResolveAimForward();
            float   cosHalf = Mathf.Cos(combat.MeleeAngle * 0.5f * Mathf.Deg2Rad);

            bool  isCritical = IsCriticalReady(combat);
            float multiplier = (isCritical) ? Random.Range(combat.CriticalMultiplierMin, combat.CriticalMultiplierMax) : 1f;
            int   hitCount   = 0;

            _hitTargets.Clear();

            int count = Physics.OverlapSphereNonAlloc(origin, combat.MeleeRange, _hitBuffer, _targetMask);

            for (int i = 0; i < count; ++i)
            {
                Collider hit = _hitBuffer[i];

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

                if (!DamageTargetResolver.TryResolve(hit, out IDamageable damageable))
                {
                    continue;
                }

                if (damageable.IsDead)
                {
                    continue;
                }

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

        /// <summary> 화살을 띄운다. 판정은 화살이 서버 틱을 돌며 직접 한다 </summary>
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

        /// <summary> 락온 대상 쪽으로 발사각을 당길 수 있는지 발사 순간에 다시 확인한다 </summary>
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
