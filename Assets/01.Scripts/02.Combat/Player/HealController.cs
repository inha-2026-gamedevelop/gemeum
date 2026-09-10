// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 방울의 범위 치유. 신력을 써서 주변 파티원의 HP를 채운다 </summary>
    public class HealController : NetworkBehaviour, IPlayerAction
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("치유 입력 키. 조작 변경은 씬이 아니라 여기서 한다")]
        [SerializeField] private KeyCode _healKey = KeyCode.Mouse1;

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryController      _parry;
        private PlayerAttack         _attack;
        private PlayerAnimationController _animation;
        private ICombatLoadoutProvider _loadoutProvider;

        private CombatStatsSnapshot _pendingLoadout;
        private CombatStatsSnapshot _committedLoadout;

        private RequestIdTracker _requestIds;
        private uint             _nextRequestId = 1u;
        private uint             _nextPresentationActionId = 1u;
        private uint             _activePresentationActionId;

        private bool   _isCasting               = false;
        private double _castEndServerTime       = 0d;
        private double _actionLockEndServerTime = 0d;
        private double _cooldownEndServerTime   = 0d;
        private bool   _hasRunResumedSubscription;

        // 회복 대상 이름을 화면 로그에 적기 위한 버퍼. 매 시전마다 새로 만들면 GC가 쌓인다
        private readonly System.Collections.Generic.List<string> _healedNames = new System.Collections.Generic.List<string>(Constants.Player.PARTY_SIZE);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _parry  = GetComponent<ParryController>();
            _attack = GetComponent<PlayerAttack>();
            _animation = GetComponent<PlayerAnimationController>();
            _loadoutProvider = GetComponent<ICombatLoadoutProvider>();
        }

        private void Update()
        {
            if ((!isLocalPlayer) || (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!Input.GetKeyDown(_healKey)))
            {
                return;
            }

            // 치유는 방울 전용이다. 무기를 안 보고 보내면 검·활을 든 사람이 우클릭할 때마다
            // 서버까지 갔다가 NoWeapon으로 거부돼 화면에 거부 로그가 뜬다.
            // 우클릭은 무기마다 다른 행동에 걸려 있으므로, 내 무기가 아니면 이 요청은 아예 안 보낸다.
            // 서버는 그대로 검증한다 - 여기는 입력을 걸러 주는 것뿐이다
            if ((_weapon == null) || (!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Bell))
            {
                return;
            }

            CmdRequestHeal(_nextRequestId);
            ++_nextRequestId;
        }

        private void OnDestroy()
        {
            UnsubscribeRunResumed();
        }

        public override void OnStartServer()
        {
            EnsureRunResumedSubscription();
        }

        public override void OnStopServer()
        {
            UnsubscribeRunResumed();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 시전이나 후딜이 진행 중인가. 다른 행동이 이 값을 보고 겹치기를 막는다 </summary>
        public bool IsActionInProgress(double serverTime)
        {
            return serverTime < _actionLockEndServerTime;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestHeal(uint requestId)
        {
            EnsureRunResumedSubscription();

            ActionRejection rejection = PlayerActionPipeline.TryExecute(
                ref _requestIds, requestId, this, NetworkTime.time);

            if (rejection == ActionRejection.None)
            {
                return;
            }

            TargetHealRejected(rejection);
        }

        /****************************************
        *              Player Action
        ****************************************/

        /// <summary> 이번 치유의 게이트 입력을 만든다 </summary>
        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return false;
            }

            ParticipantId participant = _status.Participant;
            if ((_loadoutProvider == null) || (!participant.IsValid) ||
                (!_loadoutProvider.TryBuildLoadout(participant, out _pendingLoadout)) ||
                (!_pendingLoadout.IsValid))
            {
                Debug.LogError("[HealController] 유효한 치유 전투 수치를 만들지 못했다", this);
                return false;
            }

            bool isOtherActionInProgress = ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(serverTime)))
                || ((_attack != null) && (_attack.IsActionInProgress(serverTime)))
                || (_isCasting);

            RunPhase phase    = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool     isPaused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                ((_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Bell)),
                isOtherActionInProgress,
                serverTime,
                _cooldownEndServerTime,
                _status.Spirit,
                _pendingLoadout.ResolveSpiritCost(GameDB.Combat.HealSpiritCost));

            return true;
        }

        public bool TrySpendResource(in PlayerActionContext context)
        {
            return _status.TrySpendSpirit(context.ResourceCost);
        }

        /// <summary> 시전을 시작한다. 회복은 시전이 끝나는 시점에 한 번 일어난다 </summary>
        public void CommitAction(in PlayerActionContext context)
        {
            CombatConfigSO combat = GameDB.Combat;

            _committedLoadout = _pendingLoadout;

            _isCasting               = true;
            _castEndServerTime       = context.ServerTime + combat.HealCastDuration;
            _actionLockEndServerTime = _castEndServerTime + combat.HealRecoveryDuration;

            // 쿨다운은 수락 시점부터 잰다. 시전이 끊겨도 신력과 쿨다운은 돌려주지 않는다
            _cooldownEndServerTime = context.ServerTime + combat.HealCooldownDuration;

            _activePresentationActionId = TakeNextPresentationActionId();
            double gameplayStart = RunTimeLocator.Current.GameplayElapsedSeconds;
            PlayerActionCue cue = new PlayerActionCue
            {
                ExecutionId = _activePresentationActionId,
                Kind = PlayerActionCueKind.Heal,
                Weapon = WeaponKind.Bell,
                ComboStage = 0,
                StartGameplayTime = gameplayStart,
                ImpactGameplayTime = gameplayStart + combat.HealCastDuration,
                EndGameplayTime = gameplayStart + combat.HealCastDuration +
                    combat.HealRecoveryDuration,
            };
            RpcHealStarted(cue);

            GDebug.Server($"치유 시전 시작, 신력 -{context.ResourceCost:F0}");
        }

        /****************************************
        *              Server Side
        ****************************************/

        /// <summary> 시전이 끝나는 시점에 한 번만 회복시킨다 </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            EnsureRunResumedSubscription();

            if (!_isCasting)
            {
                return;
            }

            // 공격과 같은 규칙이다. 쓰러지거나 런이 멈추면 시전을 버린다
            if ((_status.IsDead) || _status.IsSwallowed || (IsRunStopped()))
            {
                _isCasting = false;
                CancelPresentation();
                GDebug.Warn("치유 시전 취소, 사망이나 런 정지");
                return;
            }

            if (now < _castEndServerTime)
            {
                return;
            }

            _isCasting = false;
            ApplyHeal();
        }

        [Server]
        private void ApplyHeal()
        {
            if (!_committedLoadout.IsValid)
            {
                CancelPresentation();
                Debug.LogError("[HealController] 수락된 치유 snapshot이 없다", this);
                return;
            }

            Vector3 origin      = transform.position;
            float   radiusSqr   = _committedLoadout.HealRadius *
                _committedLoadout.HealRadius;
            int     healedCount = 0;
            int     buffedCount = 0;
            ParticipantId healer = _status.Participant;
            _healedNames.Clear();

            // 파티가 세 명이라 연결 목록을 도는 편이 물리 쿼리보다 싸고 레이어 설정에 기대지 않는다
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if ((conn == null) || (conn.identity == null))
                {
                    continue;
                }

                if (!conn.identity.TryGetComponent(out PlayerStatus target))
                {
                    continue;
                }

                Vector3 offset = (target.transform.position - origin);
                if (offset.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                // 결계 회복 배수와 최대 HP는 TryRestoreHealth가 이미 적용한다. 여기서 또 곱하면 두 번 걸린다.
                // 쓰러진 사람도 그 안에서 거부되므로 치유가 부활을 대신하지 않는다
                if (target.TryRestoreHealth(_committedLoadout.HealAmount, healer, out _))
                {
                    ++healedCount;
                    _healedNames.Add(GDebug.Who(target.netId));
                }

                if ((!target.IsDead) && (!target.IsSwallowed) &&
                    (_committedLoadout.SupportBuffDamageMultiplier > 1f) &&
                    target.TryGetComponent(out ISupportBuffTarget buffTarget) &&
                    buffTarget.ServerApplySupportBuff(
                        _committedLoadout.SupportBuffDamageMultiplier,
                        _committedLoadout.SupportBuffDuration))
                {
                    ++buffedCount;
                }
            }

            GDebug.Server($"{GDebug.Who(netId)} 치유 -> {healedCount}명, 지원 {buffedCount}명 " +
                $"[{string.Join(", ", _healedNames)}]");
            _healedNames.Clear();
            TargetHealResult(healedCount);
            RpcHealResolved(_activePresentationActionId, healedCount,
                RunTimeLocator.Current.GameplayElapsedSeconds);
            _activePresentationActionId = 0u;
        }

        [ClientRpc]
        private void RpcHealStarted(PlayerActionCue cue)
        {
            _animation?.PlayActionCue(in cue);
        }

        [ClientRpc]
        private void RpcHealCancelled(uint executionId)
        {
            _animation?.CancelActionCue(PlayerActionCueKind.Heal, executionId);
        }

        [ClientRpc]
        private void RpcHealResolved(uint executionId, int healedCount,
            double impactGameplayTime)
        {
            _animation?.PlayActionImpact(PlayerActionCueKind.Heal,
                executionId, healedCount > 0, false, impactGameplayTime);
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
            _castEndServerTime = ShiftPausedDeadline(
                _castEndServerTime, resumeServerTime, resumed.PausedSeconds);
            _actionLockEndServerTime = ShiftPausedDeadline(
                _actionLockEndServerTime, resumeServerTime, resumed.PausedSeconds);
            _cooldownEndServerTime = ShiftPausedDeadline(
                _cooldownEndServerTime, resumeServerTime, resumed.PausedSeconds);
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

        [Server]
        private void CancelPresentation()
        {
            if (_activePresentationActionId == 0u)
            {
                return;
            }

            RpcHealCancelled(_activePresentationActionId);
            _activePresentationActionId = 0u;
        }

        private uint TakeNextPresentationActionId()
        {
            uint result = _nextPresentationActionId;
            _nextPresentationActionId = (_nextPresentationActionId == uint.MaxValue)
                ? 1u
                : _nextPresentationActionId + 1u;
            return result;
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetHealRejected(ActionRejection rejection)
        {
            GDebug.Fail($"치유 거부: {rejection}");
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /// <summary> 시전자 화면에만 결과를 띄운다. 서버가 센 숫자라 클라가 다시 세지 않는다 </summary>
        [TargetRpc]
        private void TargetHealResult(int healedCount)
        {
            if (healedCount <= 0)
            {
                GDebug.Warn("치유 완료, 회복된 사람 없음");
                return;
            }

            GDebug.Success($"치유 완료, {healedCount}명 회복");
        }
    }
}
