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
        *              Constants
        ****************************************/

        // 개발용 기즈모. 스윙이 지나간 뒤에도 잠깐 남아야 눈으로 확인할 수 있다
        private const float GIZMO_SECONDS      = 1.5f;
        private const float GIZMO_LABEL_HEIGHT = 2f; // 발밑에서 이만큼 위에 글자를 띄운다(m)

        // 이번 요청의 조준. 클라이언트가 보내고 서버가 검증해 담는다
        private Vector2 _requestedAim    = Vector2.zero;
        private bool    _hasRequestedAim = false;

        private static readonly Color GIZMO_SWEEP_COLOR = new Color(1f, 0.45f, 0.2f, 0.9f);

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
        private PrecisionShotController   _precisionShot;
        private ICombatLoadoutProvider    _loadoutProvider;

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
        private CombatStatsSnapshot _pendingLoadout;
        private CombatStatsSnapshot _committedLoadout;
        private bool                _pendingCritical;
        private bool                _committedCritical;
        private bool                _pendingConsumeGauge;
        private bool                _committedConsumeGauge;
        private float               _pendingCriticalMultiplier = 1f;
        private float               _committedCriticalMultiplier = 1f;

        private ArrowFireSnapshot _burstSnapshot;
        private int               _remainingBurstShots;
        private double            _nextBurstShotServerTime;
        private bool              _hasRunResumedSubscription;

        private uint _nextRequestId = 1u;
        private uint _nextServerActionId = 1u;
        private uint _activePresentationActionId;

        // 매번 새로 만들면 공격마다 GC 할당이 생긴다
        private readonly Collider[]        _hitBuffer  = new Collider[Constants.Combat.HIT_QUERY_BUFFER_SIZE];
        private readonly List<IDamageable> _hitTargets = new List<IDamageable>(Constants.Combat.HIT_QUERY_BUFFER_SIZE);
        private readonly RaycastHit[]       _aimHitBuffer = new RaycastHit[Constants.Combat.HIT_QUERY_BUFFER_SIZE];
        private readonly Collider[]         _muzzleOverlapBuffer = new Collider[Constants.Combat.HIT_QUERY_BUFFER_SIZE];

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 공격 동작 중인가. 구르기와 패링이 이 값을 보고 겹침을 막는다 </summary>
        public bool IsActionInProgress(double serverTime)
        {
            return IsBaseActionInProgress(serverTime)
                || ((_precisionShot != null) && (_precisionShot.IsActionInProgress));
        }

        /// <summary> 정밀 사격을 제외한 기본 공격과 후딜이 진행 중인가 </summary>
        public bool IsBaseActionInProgress(double serverTime)
        {
            return (_isSwinging) || (_remainingBurstShots > 0) ||
                (serverTime < _recoveryServerTime);
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
            _precisionShot = GetComponent<PrecisionShotController>();
            _loadoutProvider = GetComponent<ICombatLoadoutProvider>();

            if (_weapon != null)
            {
                _weapon.WeaponChanged += HandleWeaponChanged;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeRunResumed();

            if (_weapon != null)
            {
                _weapon.WeaponChanged -= HandleWeaponChanged;
            }
        }

        public override void OnStartServer()
        {
            EnsureRunResumedSubscription();
        }

        public override void OnStopServer()
        {
            UnsubscribeRunResumed();
        }

        private void Update()
        {
            if ((!isLocalPlayer) || (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!Input.GetKeyDown(_attackKey)))
            {
                return;
            }

            // 마우스가 카메라를 돌리므로 카메라 방위각이 곧 조준 방향이다.
            // 모델이 바라보는 방향은 이동에 따라 늦게 따라오므로 그걸로 판정하면 눈과 어긋난다
            Vector2 aim = CameraFrame.AimDirection;

            // 내 화면은 로컬 예측값으로 그린다. 서버가 돌려 놓은 방향은 SyncVar로 오지만
            // 소유 클라이언트는 그걸 안 보므로, 여기서 같이 돌려야 클라에서도 조준 방향을 본다
            if (_presentation != null)
            {
                _presentation.ClientPredictFacing(aim);
            }

            CmdRequestAttack(_nextRequestId, aim);
            ++_nextRequestId;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestAttack(uint requestId, Vector2 aim)
        {
            EnsureRunResumedSubscription();

            // 클라이언트가 보낸 값이므로 그대로 믿지 않는다.
            // 방향만 받고 사거리·각도·피해는 계속 서버 수치를 쓴다
            ServerApplyAim(aim);

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
            _pendingCritical = false;
            _pendingConsumeGauge = false;
            _pendingCriticalMultiplier = 1f;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return false;
            }

            if (_loadoutProvider == null)
            {
                Debug.LogError("[PlayerAttack] ICombatLoadoutProvider가 없어 공격을 수락할 수 없다", this);
                return false;
            }

            if (!_weapon.HasWeapon)
            {
                return false;
            }

            ParticipantId participant = ResolveParticipantId();
            if ((!participant.IsValid) || (!_loadoutProvider.TryBuildLoadout(participant, out _pendingLoadout))
                || (!_pendingLoadout.IsValid) || (!IsFinite(_pendingLoadout.MeleeAttackPower))
                || (!IsFinite(_pendingLoadout.RangedAttackPower))
                || (!IsFinite(_pendingLoadout.MaxHealth)))
            {
                Debug.LogError("[PlayerAttack] 유효한 전투 수치를 만들지 못해 공격을 수락할 수 없다", this);
                return false;
            }

            _pendingProfile = ResolveProfile(_weapon.Weapon, GameDB.Combat,
                _pendingLoadout);

            if (!_pendingProfile.HasAttack)
            {
                return false;
            }

            bool hasValidPower = (_pendingProfile.Reach == AttackReach.Ranged)
                ? ((_pendingLoadout.RangedAttackPower > 0f) && IsFinite(_pendingLoadout.RangedAttackPower))
                : ((_pendingLoadout.MeleeAttackPower > 0f) && IsFinite(_pendingLoadout.MeleeAttackPower));
            if (!hasValidPower)
            {
                Debug.LogError("[PlayerAttack] 공격 방식에 맞는 전투 수치가 없어 공격을 수락할 수 없다", this);
                return false;
            }

            _pendingConsumeGauge = (_pendingProfile.Reach == AttackReach.Melee)
                && IsCriticalReady(GameDB.Combat);
            _pendingCritical = _pendingConsumeGauge ||
                (Random.value < _pendingLoadout.CriticalChance);
            if (_pendingCritical)
            {
                _pendingCriticalMultiplier = Random.Range(
                    _pendingLoadout.CriticalMultiplierMin,
                    _pendingLoadout.CriticalMultiplierMax);
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

            // 장비나 특성이 바뀌어도 이미 수락한 공격은 이 snapshot으로 끝낸다.
            _committedLoadout = _pendingLoadout;
            _committedCritical = _pendingCritical;
            _committedConsumeGauge = _pendingConsumeGauge;
            _committedCriticalMultiplier = _pendingCriticalMultiplier;
            _isSwinging         = true;
            _swingReach         = profile.Reach;
            _hitServerTime      = now + profile.StartupDuration;
            _recoveryServerTime = _hitServerTime + profile.RecoveryDuration;

            _comboStage = (profile.Reach == AttackReach.Melee)
                ? (((_comboStage > 0) && (now <= _comboExpireServerTime)) ? ((_comboStage % Constants.Combat.MELEE_COMBO_STAGE_COUNT) + 1) : 1)
                : 0;
            _comboExpireServerTime = _recoveryServerTime + GameDB.Combat.MeleeComboWindowDuration;

            _activePresentationActionId = TakeNextServerActionId();
            double gameplayStart = RunTimeLocator.Current.GameplayElapsedSeconds;
            PlayerActionCue cue = new PlayerActionCue
            {
                ExecutionId = _activePresentationActionId,
                Kind = PlayerActionCueKind.Attack,
                Weapon = _weapon.Weapon,
                ComboStage = (byte) Mathf.Clamp(_comboStage, 0, byte.MaxValue),
                StartGameplayTime = gameplayStart,
                ImpactGameplayTime = gameplayStart + profile.StartupDuration,
                EndGameplayTime = gameplayStart + profile.StartupDuration +
                    profile.RecoveryDuration,
            };
            RpcAttackStarted(cue);
        }

        /****************************************
        *              Client Rpc
        ****************************************/

        /// <summary> 공격이 수락됐음을 모든 화면에 알린다 </summary>
        [ClientRpc]
        private void RpcAttackStarted(PlayerActionCue cue)
        {
            if (_animation != null)
            {
                _animation.PlayActionCue(in cue);
            }
        }

        [ClientRpc]
        private void RpcActionCancelled(uint executionId)
        {
            _animation?.CancelActionCue(PlayerActionCueKind.Attack, executionId);
        }

        [ClientRpc]
        private void RpcMarkApplied(uint executionId, Vector3 worldPosition,
            double appliedGameplayTime)
        {
            _animation?.PlayMarkCue(executionId, worldPosition,
                appliedGameplayTime);
        }

        /// <summary> 요청이 거부된 사유를 요청자에게만 돌려준다 </summary>
        [TargetRpc]
        private void TargetActionRejected(ActionRejection rejection)
        {
            GDebug.Fail($"공격 거부: {rejection}");
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /// <summary> 근접 판정 결과를 모든 화면에 알린다 </summary>
        [ClientRpc]
        private void RpcMeleeResult(uint executionId, bool didHit,
            bool wasCritical, double impactGameplayTime)
        {
            _animation?.PlayActionImpact(PlayerActionCueKind.Attack,
                executionId, didHit, wasCritical, impactGameplayTime);

            // 남의 공격까지 찍으면 3인에서 화면이 덮인다. 자기 것만 본다
            if (!isLocalPlayer)
            {
                return;
            }

            if (didHit)
            {
                float hitStop = 0f;
                if (GameDB.IsInitialized && (GameDB.Presentation != null))
                {
                    hitStop = wasCritical
                        ? GameDB.Presentation.CriticalHitStopSeconds
                        : GameDB.Presentation.LightHitStopSeconds;
                }

                CombatFeedbackLocator.Current.PlayHit(
                    wasCritical ? HitStrength.Critical : HitStrength.Light,
                    hitStop);
            }

            if (!didHit)
            {
                GDebug.Warn($"{GDebug.Who(netId)} 공격 헛침 {_comboStage}단");
                return;
            }

            GDebug.Success(wasCritical
                ? $"{GDebug.Who(netId)} 치명타 {_comboStage}단"
                : $"{GDebug.Who(netId)} 적중 {_comboStage}단");
        }

        /****************************************
        *              Server Side
        ****************************************/

        /// <summary> 선딜이 끝나는 시점에 한 번만 판정한다 </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            EnsureRunResumedSubscription();
            UpdateBurstShots(now);

            if (!_isSwinging)
            {
                return;
            }

            if ((_status.IsDead) || _status.IsSwallowed || (IsRunStopped()))
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
                FireArrow(combat, now);
            }
            else
            {
                ApplySweepHit(combat, _swingReach);
            }

            _isSwinging = false;
            _swingReach = AttackReach.None;
            _committedCritical = false;
            _committedConsumeGauge = false;
            _activePresentationActionId = 0u;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 무기가 판정 방식과 수치를 정한다. 캐릭터가 무기를 정하므로 곧 직업별 수치다 </summary>
        private static AttackProfile ResolveProfile(WeaponKind weapon, CombatConfigSO combat,
            in CombatStatsSnapshot loadout)
        {
            switch (weapon.ToReach())
            {
                case AttackReach.Melee:
                    return new AttackProfile(
                        AttackReach.Melee,
                        loadout.ResolveAttackDuration(combat.MeleeStartupDuration),
                        loadout.ResolveAttackDuration(combat.MeleeRecoveryDuration),
                        loadout.ResolveStaminaCost(combat.MeleeStaminaCost));

                case AttackReach.Ranged:
                    return new AttackProfile(
                        AttackReach.Ranged,
                        loadout.ResolveAttackDuration(combat.RangedStartupDuration),
                        loadout.ResolveAttackDuration(combat.RangedRecoveryDuration),
                        loadout.ResolveStaminaCost(combat.RangedStaminaCost));

                case AttackReach.Area:
                    return new AttackProfile(
                        AttackReach.Area,
                        loadout.ResolveAttackDuration(combat.BellStartupDuration),
                        loadout.ResolveAttackDuration(combat.BellRecoveryDuration),
                        loadout.ResolveStaminaCost(combat.BellStaminaCost));

                default:
                    return default;
            }
        }

        private PlayerActionContext BuildContext(double now, AttackProfile profile)
        {
            bool isOtherActionInProgress = ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(now)))
                || ((_precisionShot != null) && (_precisionShot.IsActionInProgress));

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
        /// 클라이언트가 보낸 조준을 이번 공격의 방향으로 받는다.
        /// 값이 이상하면 버리고 기존 방향을 쓴다 - 개발 도구가 아니라 판정 입력이므로 조용히 넘기지 않고 남긴다
        /// </summary>
        [Server]
        private void ServerApplyAim(Vector2 aim)
        {
            _hasRequestedAim = false;

            if ((!IsFinite(aim.x)) || (!IsFinite(aim.y)) || (aim.sqrMagnitude <= Mathf.Epsilon))
            {
                GDebug.Warn($"조준 방향이 이상해 무시했다: {aim}");
                return;
            }

            _requestedAim    = aim.normalized;
            _hasRequestedAim = true;

            // 모델도 그 방향을 보게 한다. 판정과 보이는 방향이 다르면 맞았는지 눈으로 못 가린다.
            // 락온 중에는 PlayerPresentation이 락온 방향을 그대로 쓴다
            if (_presentation != null)
            {
                _presentation.ServerSetFacing(_requestedAim);
            }
        }

        /// <summary> 이번 공격이 향하는 평면 방향 </summary>
        [Server]
        private Vector3 ResolveAimForward()
        {
            if (_presentation == null)
            {
                Debug.LogError("[PlayerAttack] PlayerPresentation이 없어 조준 방향을 정할 수 없다", this);
                return transform.forward;
            }

            // 락온이 걸려 있으면 표현이 락온 방향을 준다. 그때는 조준보다 락온이 이긴다
            Vector2 facing = ((_hasRequestedAim) && (!_presentation.IsLockedOn))
                ? _requestedAim
                : _presentation.AimDirection;

            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                Debug.LogWarning("[PlayerAttack] 조준 방향이 비어 있어 이번 공격은 루트 정면으로 나간다", this);
                return transform.forward;
            }

            return new Vector3(facing.x, 0f, facing.y).normalized;
        }

        /// <summary> 검과 방울이 함께 쓰는 즉시 판정. 반경과 각도만 무기가 정한다 </summary>
        [Server]
        private void ApplySweepHit(CombatConfigSO combat, AttackReach reach)
        {
            bool  isBell    = (reach == AttackReach.Area);
            float range     = (isBell) ? combat.BellRange     : combat.MeleeRange;
            float angle     = (isBell) ? combat.BellAngle     : combat.MeleeAngle;
            float basePower = _committedLoadout.MeleeAttackPower;

            if ((!_committedLoadout.IsValid) || (!IsFinite(basePower)) || (basePower <= 0f))
            {
                Debug.LogError("[PlayerAttack] 수락된 전투 수치가 없어 근접 판정을 실행할 수 없다", this);
                return;
            }

            Vector3 origin  = transform.position;
            Vector3 forward = ResolveAimForward();
            float   cosHalf = Mathf.Cos(angle * 0.5f * Mathf.Deg2Rad);

            // 360도는 각도 검사 자체를 건너뛴다. cos(180도)가 정확히 -1이 아니라
            // 바로 등 뒤에 선 적의 내적이 문턱을 아슬아슬하게 밑돌 수 있다
            bool ignoresFacing = (angle >= Constants.Combat.FULL_CIRCLE_DEGREES);

            bool  isCritical = _committedCritical;
            float multiplier = isCritical ? _committedCriticalMultiplier : 1f;
            if ((!isBell) && (_comboStage == Constants.Combat.MELEE_COMBO_STAGE_COUNT))
            {
                multiplier *= _committedLoadout.ThirdComboDamageMultiplier;
            }

            int   hitCount   = 0;
            float spiritGain = 0f;

            // 안정 신원과 표식 소유자는 한 번만 찾는다. 맞은 수만큼 다시 조회하지 않는다.
            ParticipantId attacker = ResolveParticipantId();
            ParticipantId markOwner = isBell ? attacker : ParticipantId.None;

            _hitTargets.Clear();

            DrawSweepGizmo(origin, forward, angle, range, ignoresFacing);

            int count = Physics.OverlapSphereNonAlloc(origin, range, _hitBuffer, _targetMask);

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

                if ((!ignoresFacing) && (Vector3.Dot(forward, toTarget.normalized) < cosHalf))
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
                    basePower * multiplier,
                    netId,
                    attacker,
                    (isCritical) ? HitStrength.Critical : HitStrength.Light));

                ++hitCount;

                // 적 하나가 콜라이더를 여러 개 가져도 _hitTargets가 걸러 주므로 한 번만 오른다
                if (isBell)
                {
                    spiritGain = Mathf.Min(spiritGain + combat.SpiritGainPerHit, combat.SpiritGainMaxPerSwing);

                    // 표식이 거부돼도 이 타격의 피해와 신력은 그대로다
                    TryApplyMark(markOwner, damageable, combat);
                }
            }

            if (_committedConsumeGauge && (hitCount > 0))
            {
                _gauge.ServerTryConsume();
            }

            if (spiritGain > 0f)
            {
                _status.ServerAddSpirit(spiritGain);
                GDebug.Server($"방울 {hitCount}명 적중, 신력 +{spiritGain:F0}");
            }

            // 맞았을 때만, 그리고 한글 없이 남긴다.
            // 월드 글자는 IMGUI 기본 폰트로 그려서 빌드에서는 한글 글리프가 없어 깨진 사각형이 뜬다
            if (hitCount > 0)
            {
                GGizmo.Label3D(GGizmoChannel.Combat, origin + (Vector3.up * GIZMO_LABEL_HEIGHT),
                    (isBell ? "bell x" : "hit x") + hitCount, Color.white, GIZMO_SECONDS);
            }

            RpcMeleeResult(_activePresentationActionId, hitCount > 0,
                (isCritical) && (hitCount > 0),
                RunTimeLocator.Current.GameplayElapsedSeconds);
        }

        /// <summary>
        /// 방금 판정한 범위를 눈에 보이게 남긴다.
        /// 반경과 각도를 숫자로만 보면 "왜 안 맞았는지"를 찾을 수 없다 - 그림으로 봐야 찾는다.
        ///
        /// 판정은 서버에서만 하므로 이 선도 서버(호스트) 화면에만 뜬다.
        /// 원격 클라이언트에서 같은 범위를 보려면 그쪽에서 따로 그려야 한다
        /// </summary>
        private static void DrawSweepGizmo(Vector3 origin, Vector3 forward, float angle, float range,
                                           bool ignoresFacing)
        {
            // 한 프레임만 그리면 스윙이 지나간 뒤에는 못 본다. 잠깐 남긴다
            if (ignoresFacing)
            {
                GGizmo.Circle(GGizmoChannel.Combat, origin, Vector3.up, range,
                    GIZMO_SWEEP_COLOR, GIZMO_SECONDS);
                return;
            }

            GGizmo.Arc(GGizmoChannel.Combat, origin, forward, angle * 0.5f, range,
                GIZMO_SWEEP_COLOR, GIZMO_SECONDS);
        }

        /// <summary> 이 플레이어의 영속 신원. 표식 소유권이 재접속에도 유지되도록 netId가 아니라 이 값을 쓴다 </summary>
        [Server]
        private ParticipantId ResolveParticipantId()
        {
            if ((connectionToClient == null) || (GeumeumAuthenticator.ServerInstance == null))
            {
                return ParticipantId.None;
            }

            if (!GeumeumAuthenticator.ServerInstance.TryGetPlatformId(
                    connectionToClient.connectionId, out PlayerPlatformId platformId))
            {
                return ParticipantId.None;
            }

            return new ParticipantId(platformId.Value);
        }

        private void HandleWeaponChanged(WeaponKind newWeapon)
        {
            if (!isServer)
            {
                return;
            }

            MarkRegistry.ClearOwner(ResolveParticipantId());
        }

        /// <summary> 맞은 대상의 고정 PartId에 표식을 건다. 계약이 없으면 기존 body 0이다. </summary>
        [Server]
        private bool TryApplyMark(ParticipantId owner, IDamageable damageable, CombatConfigSO combat)
        {
            if (!owner.IsValid)
            {
                return false;
            }

            Component component = damageable as Component;
            if (component == null)
            {
                return false;
            }

            NetworkIdentity identity = component.GetComponentInParent<NetworkIdentity>();
            if (identity == null)
            {
                return false;
            }

            byte partId = TargetRef.BODY_PART_ID;
            if (damageable is ITargetPartIdSource part)
            {
                if (!part.IsTargetable)
                {
                    return false;
                }

                partId = part.TargetPartId;
            }

            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;

            TargetRef target = TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                identity.netId,
                partId);

            double gameplayTime = RunTimeLocator.Current.GameplayElapsedSeconds;
            bool applied = MarkRegistry.TryApply(
                owner, target, gameplayTime, combat.MarkDuration, combat.MarkSlotCount,
                out MarkRejection rejection);

            if (applied)
            {
                GDebug.Server($"표식 부여 netId={identity.netId} partId={partId}");
                RpcMarkApplied(_activePresentationActionId,
                    component.transform.position, gameplayTime);
                return true;
            }

            // 이미 걸린 대상에 또 치는 것은 정상이라 실패로 띄우지 않는다
            if (rejection != MarkRejection.AlreadyMarked)
            {
                GDebug.Warn($"표식 거부: {rejection}");
            }

            return false;
        }

        /// <summary> 다음 근접 공격이 치명의 일격인가. 소비하지 않고 보기만 한다 </summary>
        [Server]
        private bool IsCriticalReady(CombatConfigSO combat)
        {
            return ((_gauge != null) && (_gauge.Gauge >= combat.ParryGaugeMax));
        }

        /// <summary> 화살을 띄운다. 판정은 화살이 서버 틱을 돌며 직접 한다 </summary>
        [Server]
        private void FireArrow(CombatConfigSO combat, double now)
        {
            if (_arrowPrefab == null)
            {
                Debug.LogError("[PlayerAttack] 화살 프리팹이 비어 있어 원거리 공격이 나가지 않는다", this);
                return;
            }

            if ((!_committedLoadout.IsValid) || (!IsFinite(_committedLoadout.RangedAttackPower))
                || (_committedLoadout.RangedAttackPower <= 0f))
            {
                Debug.LogError("[PlayerAttack] 수락된 원거리 전투 수치가 없어 화살을 발사할 수 없다", this);
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

            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;

            uint actionId = _activePresentationActionId;
            if (actionId == 0u)
            {
                Debug.LogError("[PlayerAttack] 서버 공격 action ID가 고갈됐다", this);
                return;
            }

            float damage = _committedLoadout.RangedAttackPower *
                (_committedCritical ? _committedCriticalMultiplier : 1f);

            ArrowFireSnapshot snapshot = new ArrowFireSnapshot(
                origin,
                forward,
                damage,
                _committedCritical ? HitStrength.Critical : HitStrength.Light,
                _committedLoadout.ArrowSpeed,
                _committedLoadout.ArrowHitRadius,
                _committedLoadout.GuidanceDuration,
                _committedLoadout.PierceCount,
                _committedLoadout.BurstShotCount,
                _committedLoadout.BurstShotInterval,
                0f,
                0f,
                netId,
                ResolveParticipantId(),
                actionId,
                0,
                false,
                (run == null) ? 0u : run.RunId.Value,
                (director == null) ? 0u : director.Revision.Value,
                _targetMask.value,
                guidanceTargetNetId,
                guidanceTargetPart);

            SpawnArrow(snapshot, arrowConfig);

            _burstSnapshot = snapshot;
            _remainingBurstShots = Mathf.Max(0, snapshot.BurstShotCount - 1);
            _nextBurstShotServerTime = now + snapshot.BurstShotInterval;
        }

        /// <summary> 정밀 사격 해제 시점의 조준과 공격 수치를 검증해 한 번 쓸 snapshot을 만든다 </summary>
        [Server]
        public bool ServerTryBuildPrecisionShot(
            in PrecisionAimRay aim,
            uint actionId,
            out ArrowFireSnapshot snapshot)
        {
            snapshot = default;

            if ((!GameDB.IsInitialized) || (_arrowPrefab == null) || (_weapon == null)
                || (!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Bow)
                || (_loadoutProvider == null) || (actionId == 0u))
            {
                return false;
            }

            ArrowConfigSO arrowConfig = GameDB.Arrow;
            CameraConfigSO cameraConfig = GameDB.Camera;
            if ((arrowConfig == null) || (cameraConfig == null))
            {
                return false;
            }

            float aimRange = arrowConfig.ArrowSpeed * arrowConfig.ArrowLifetime;
            float maxOriginDistance = ResolveMaxCameraOriginDistance(cameraConfig);
            if ((!IsFinite(aimRange)) || (aimRange <= 0f)
                || (!PrecisionShotRules.IsValidAimRay(aim, transform.position, maxOriginDistance)))
            {
                return false;
            }

            ParticipantId participant = ResolveParticipantId();
            if ((!participant.IsValid)
                || (!_loadoutProvider.TryBuildLoadout(participant, out CombatStatsSnapshot loadout))
                || (!loadout.IsValid) || (!IsFinite(loadout.RangedAttackPower))
                || (loadout.RangedAttackPower <= 0f))
            {
                return false;
            }

            aimRange = loadout.ArrowSpeed * arrowConfig.ArrowLifetime;

            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (director == null))
            {
                return false;
            }

            Vector3 bodyForward = ResolveAimForward();
            Vector3 muzzle = transform.position
                + (Vector3.up * GameDB.Combat.RangedMuzzleHeight)
                + (bodyForward * GameDB.Combat.RangedMuzzleForward);

            if (IsMuzzleOverlappingWorld(muzzle, arrowConfig))
            {
                return false;
            }

            if (!TryResolveAimPoint(aim, aimRange, arrowConfig, out Vector3 aimPoint))
            {
                return false;
            }

            Vector3 toAimPoint = aimPoint - muzzle;
            if ((!IsFinite(toAimPoint)) || (toAimPoint.sqrMagnitude <= Mathf.Epsilon))
            {
                return false;
            }

            Vector3 direction = toAimPoint.normalized;
            uint guidanceTargetNetId = 0u;
            byte guidanceTargetPart  = 0;

            if (TryResolveLockedAim(arrowConfig, muzzle, direction, out Vector3 snapped))
            {
                direction               = snapped;
                guidanceTargetNetId     = _lockOn.TargetNetId;
                guidanceTargetPart      = _lockOn.TargetPartIndex;
            }

            float muzzleLineDistance = Mathf.Min(toAimPoint.magnitude, aimRange);
            if (!TryValidateMuzzleLine(
                    muzzle, direction, muzzleLineDistance, arrowConfig,
                    out bool muzzleLineBlocked))
            {
                return false;
            }

            if (muzzleLineBlocked)
            {
                guidanceTargetNetId = 0u;
                guidanceTargetPart  = 0;
            }

            bool isCritical = Random.value < loadout.CriticalChance;
            float criticalMultiplier = isCritical
                ? Random.Range(loadout.CriticalMultiplierMin, loadout.CriticalMultiplierMax)
                : 1f;

            snapshot = new ArrowFireSnapshot(
                muzzle,
                direction,
                loadout.RangedAttackPower * criticalMultiplier,
                isCritical ? HitStrength.Critical : HitStrength.Light,
                loadout.ArrowSpeed,
                loadout.ArrowHitRadius,
                loadout.GuidanceDuration,
                loadout.PierceCount,
                1,
                0f,
                loadout.ResolveStaminaCost(GameDB.Combat.PrecisionShotStaminaCost),
                loadout.ResolveAttackDuration(GameDB.Combat.RangedRecoveryDuration),
                netId,
                participant,
                actionId,
                0,
                true,
                run.RunId.Value,
                director.Revision.Value,
                _targetMask.value,
                guidanceTargetNetId,
                guidanceTargetPart);
            return true;
        }

        /// <summary> 수락한 정밀 발사 snapshot을 그대로 화살에 넘긴다 </summary>
        [Server]
        public void ServerFirePrecisionShot(in ArrowFireSnapshot snapshot)
        {
            if ((!snapshot.IsPrecision) || (snapshot.ActionId == 0u)
                || (snapshot.AttackerNetId != netId))
            {
                return;
            }

            SpawnArrow(snapshot, GameDB.Arrow);
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
        private void SpawnArrow(in ArrowFireSnapshot snapshot, ArrowConfigSO arrowConfig)
        {
            if ((_arrowPrefab == null) || (arrowConfig == null))
            {
                Debug.LogError("[PlayerAttack] 화살 프리팹이나 설정이 없어 원거리 공격이 나가지 않는다", this);
                return;
            }

            SpawnAcceptedArrow(snapshot, arrowConfig);
        }

        [Server]
        private void UpdateBurstShots(double now)
        {
            if (_remainingBurstShots <= 0)
            {
                return;
            }

            if ((_status == null) || _status.IsDead || _status.IsSwallowed || IsRunStopped())
            {
                _remainingBurstShots = 0;
                _burstSnapshot = default;
                return;
            }

            while ((_remainingBurstShots > 0) && (now >= _nextBurstShotServerTime))
            {
                int shotIndex = _burstSnapshot.BurstShotCount - _remainingBurstShots;
                SpawnArrow(_burstSnapshot.WithShotIndex(shotIndex), GameDB.Arrow);
                --_remainingBurstShots;
                _nextBurstShotServerTime += _burstSnapshot.BurstShotInterval;
            }

            if (_remainingBurstShots == 0)
            {
                _burstSnapshot = default;
            }
        }

        /// <summary> 검증이 끝난 화살 하나를 생성한다. 서버 경로 테스트는 이 경계에서 생성 횟수를 센다 </summary>
        protected virtual void SpawnAcceptedArrow(
            in ArrowFireSnapshot snapshot,
            ArrowConfigSO arrowConfig)
        {
            ArrowProjectile arrow = Instantiate(
                _arrowPrefab,
                snapshot.Origin,
                Quaternion.LookRotation(snapshot.Direction));
            arrow.ServerInitialize(snapshot, arrowConfig);
            NetworkServer.Spawn(arrow.gameObject);
        }

        private static float ResolveMaxCameraOriginDistance(CameraConfigSO camera)
        {
            float maxBoom = Mathf.Max(
                Mathf.Max(camera.Distance, camera.LockOnDistanceEnemy),
                Mathf.Max(camera.LockOnDistanceBossBody, camera.LockOnDistanceBossPart));

            return maxBoom + Mathf.Abs(camera.SideOffset) + Mathf.Abs(camera.PivotHeight);
        }

        [Server]
        private bool IsMuzzleOverlappingWorld(Vector3 muzzle, ArrowConfigSO arrow)
        {
            if (arrow.WorldMask == 0)
            {
                return false;
            }

            int count = Physics.OverlapSphereNonAlloc(
                muzzle,
                arrow.UnlockedHitRadius,
                _muzzleOverlapBuffer,
                arrow.WorldMask,
                QueryTriggerInteraction.Ignore);

            return count > 0;
        }

        [Server]
        private bool TryResolveAimPoint(
            in PrecisionAimRay aim,
            float aimRange,
            ArrowConfigSO arrow,
            out Vector3 aimPoint)
        {
            aimPoint = aim.Origin + (aim.Direction * aimRange);
            int queryMask = _targetMask.value | arrow.WorldMask;
            if (queryMask == 0)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(
                aim.Origin,
                aim.Direction,
                _aimHitBuffer,
                aimRange,
                queryMask,
                QueryTriggerInteraction.Ignore);

            // 포화된 결과는 가장 가까운 충돌이 빠졌을 수 있어 발사를 확정하지 않는다
            if (count >= _aimHitBuffer.Length)
            {
                return false;
            }

            float nearestDistance = aimRange;
            for (int i = 0; i < count; ++i)
            {
                if (_aimHitBuffer[i].distance < nearestDistance)
                {
                    nearestDistance = _aimHitBuffer[i].distance;
                }
            }

            aimPoint = aim.Origin + (aim.Direction * nearestDistance);
            return true;
        }

        [Server]
        private bool TryValidateMuzzleLine(
            Vector3 muzzle,
            Vector3 direction,
            float distance,
            ArrowConfigSO arrow,
            out bool blocked)
        {
            blocked = false;
            if ((arrow.WorldMask == 0) || (distance <= 0f))
            {
                return true;
            }

            int count = Physics.SphereCastNonAlloc(
                muzzle,
                arrow.UnlockedHitRadius,
                direction,
                _aimHitBuffer,
                distance,
                arrow.WorldMask,
                QueryTriggerInteraction.Ignore);

            if (count >= _aimHitBuffer.Length)
            {
                return false;
            }

            float nearestDistance = distance;
            for (int i = 0; i < count; ++i)
            {
                if (_aimHitBuffer[i].distance < nearestDistance)
                {
                    nearestDistance = _aimHitBuffer[i].distance;
                    blocked = true;
                }
            }

            return true;
        }

        [Server]
        private void Cancel()
        {
            if (_activePresentationActionId != 0u)
            {
                RpcActionCancelled(_activePresentationActionId);
                _activePresentationActionId = 0u;
            }

            _isSwinging         = false;
            _swingReach         = AttackReach.None;
            _pendingCritical    = false;
            _committedCritical  = false;
            _pendingConsumeGauge = false;
            _committedConsumeGauge = false;
            _pendingCriticalMultiplier = 1f;
            _committedCriticalMultiplier = 1f;
            _remainingBurstShots = 0;
            _burstSnapshot = default;
            _recoveryServerTime = 0d;
            _comboStage         = 0;
        }

        private static bool IsRunStopped()
        {
            RunManager run = RunManager.ServerInstance;
            return ((run == null) || (run.IsPaused));
        }

        [Server]
        private void EnsureRunResumedSubscription()
        {
            if (_hasRunResumedSubscription || !GameEventBus.HasInstance)
            {
                return;
            }

            GameEventBus.Instance.Subscribe<RunResumedEvent>(OnRunResumed);
            _hasRunResumedSubscription = true;
        }

        private void UnsubscribeRunResumed()
        {
            if (!_hasRunResumedSubscription || !GameEventBus.HasInstance)
            {
                _hasRunResumedSubscription = false;
                return;
            }

            GameEventBus.Instance.Unsubscribe<RunResumedEvent>(OnRunResumed);
            _hasRunResumedSubscription = false;
        }

        private void OnRunResumed(RunResumedEvent resumed)
        {
            double resumeServerTime = NetworkTime.time;
            _hitServerTime = ShiftPausedDeadline(
                _hitServerTime, resumeServerTime, resumed.PausedSeconds);
            _recoveryServerTime = ShiftPausedDeadline(
                _recoveryServerTime, resumeServerTime, resumed.PausedSeconds);
            _comboExpireServerTime = ShiftPausedDeadline(
                _comboExpireServerTime, resumeServerTime, resumed.PausedSeconds);
            _nextBurstShotServerTime = ShiftPausedDeadline(
                _nextBurstShotServerTime, resumeServerTime, resumed.PausedSeconds);
        }

        private static double ShiftPausedDeadline(double deadline,
            double resumeServerTime, double pausedSeconds)
        {
            if (double.IsNaN(deadline) || double.IsInfinity(deadline) ||
                double.IsNaN(resumeServerTime) || double.IsInfinity(resumeServerTime) ||
                double.IsNaN(pausedSeconds) || double.IsInfinity(pausedSeconds) ||
                (deadline <= 0.0) || (pausedSeconds <= 0.0))
            {
                return deadline;
            }

            double pauseStartServerTime = resumeServerTime - pausedSeconds;
            return (deadline > pauseStartServerTime) ? deadline + pausedSeconds : deadline;
        }

        private uint TakeNextServerActionId()
        {
            uint actionId = _nextServerActionId;
            _nextServerActionId = (_nextServerActionId == uint.MaxValue)
                ? 1u
                : _nextServerActionId + 1u;
            return actionId;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
