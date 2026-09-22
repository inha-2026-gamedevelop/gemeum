// System
using System;
using System.Collections;
using System.Collections.Generic;

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
    /// <summary> 사망 후 관전과 부활을 소유한다. 자동 부활과 협동 부활 둘 다 여기가 확정한다 </summary>
    public class ReviveController : NetworkBehaviour, IRunStateCarrier, IRunResettable
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint NO_TARGET = 0u;

        private const uint NO_GENERATION = 0u;

        // Movement callbacks are priority 0. Resolve every player's revive request after all movement callbacks
        private const int REVIVE_RESOLUTION_PRIORITY = Constants.Network.TICK_PRIORITY_AI - 1;

        private const int RESCUE_BUFFER_SIZE = 8;

        private const float RESCUE_MOVE_EPSILON = 0.15f; // m/s

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private double _spectateEndServerTime = 0.0;

        [SyncVar(hook = nameof(OnRescueTargetChanged))] private uint  _rescueTargetNetId = NO_TARGET;
        [SyncVar]                                      private float _rescueProgress    = 0f;
        [SyncVar] private PlayerRescueTargetKind _rescueTargetKind = PlayerRescueTargetKind.None;
        [SyncVar] private uint _rescueTargetGeneration = NO_GENERATION;

        private uint _rescueStartDamageGeneration = NO_GENERATION;

        private PlayerStatus         _status;
        private PlayerController     _movement;
        private PlayerRollController _roll;
        private PlayerAttack         _attack;
        private ParryController      _parry;
        private Rigidbody            _rigidbody;
        private CapsuleCollider      _capsule;

        private int _playerLayerMask = 0;

        private readonly Collider[] _rescueBuffer = new Collider[RESCUE_BUFFER_SIZE];

        private static readonly List<ReviveController> G_ServerControllers =
            new List<ReviveController>();

        private static AuthorityTickScheduler G_ResolverScheduler;
        private static bool                  G_ResolverRegistered;
        private static ulong                 G_ResolverServerGeneration;

        private bool _isHoldingRescue = false;

        private uint _holdGeneration = 0u; // 소유 클라이언트 전용
        private uint _serverHoldGeneration = 0u; // 서버 전용

        [SyncVar] private uint _deathGeneration = NO_GENERATION;
        private uint _reviveCommittedGeneration = NO_GENERATION;

        private bool      _pendingAutomaticRevive     = false;
        private uint      _pendingAutomaticGeneration = NO_GENERATION;
        private bool      _pendingRescueResolution    = false;
        private ulong     _lastResolutionTick         = 0UL;
        private bool      _spectateRestoredWhilePaused         = false;
        private double    _spectateRestoreServerTime           = 0.0;
        private double    _spectateRestorePauseStartServerTime = 0.0;
        private Coroutine _schedulerWaitRoutine;
        private Coroutine _eventBusWaitRoutine;
        private bool      _hasRunResumedSubscription;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 누군가를 일으키기 시작했거나 그만뒀다 </summary>
        public event Action<bool> RescuingChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 관전이 끝나는 서버 시각 </summary>
        public double SpectateEndServerTime => _spectateEndServerTime;

        /// <summary> 관전이 끝나기까지 남은 초. 쓰러진 상태가 아니면 0이다 </summary>
        public double SpectateRemainingSeconds
        {
            get
            {
                return ResolveSpectateRemainingSeconds();
            }
        }

        /// <summary> 내가 누군가를 일으키는 중이면 0~1, 아니면 0 </summary>
        public float RescueProgress => _rescueProgress;

        /// <summary> 지금 누군가를 일으키는 중인지 </summary>
        public bool IsRescuing => (_rescueTargetNetId != NO_TARGET);

        public uint DeathGeneration => _deathGeneration;

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnRescueTargetChanged(uint oldNetId, uint newNetId)
        {
            bool wasRescuing = (oldNetId != NO_TARGET);
            bool isRescuing  = (newNetId != NO_TARGET);

            if (wasRescuing == isRescuing)
            {
                return;
            }

            RescuingChanged?.Invoke(isRescuing);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status    = GetComponent<PlayerStatus>();
            _movement  = GetComponent<PlayerController>();
            _roll      = GetComponent<PlayerRollController>();
            _attack    = GetComponent<PlayerAttack>();
            _parry     = GetComponent<ParryController>();
            _rigidbody = GetComponent<Rigidbody>();
            _capsule   = GetComponent<CapsuleCollider>();

            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            HandleRescueInput();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if (!G_ServerControllers.Contains(this))
            {
                G_ServerControllers.Add(this);
            }

            EnsureResolutionScheduler();
            if ((AuthorityTickScheduler.ServerInstance == null) &&
                (_schedulerWaitRoutine == null))
            {
                _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
            }

            if (!GameEventBus.HasInstance)
            {
                Debug.LogError("[ReviveController] GameEventBus가 없어 재개 통지를 받을 수 없다", this);
                if (_eventBusWaitRoutine == null)
                {
                    _eventBusWaitRoutine = StartCoroutine(CoWaitForEventBus());
                }

                return;
            }

            SubscribeRunResumed();
        }

        public override void OnStopServer()
        {
            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (_eventBusWaitRoutine != null)
            {
                StopCoroutine(_eventBusWaitRoutine);
                _eventBusWaitRoutine = null;
            }

            G_ServerControllers.Remove(this);

            if (!_hasRunResumedSubscription || !GameEventBus.HasInstance)
            {
                _hasRunResumedSubscription = false;
                return;
            }

            GameEventBus.Instance.Unsubscribe<RunResumedEvent>(OnRunResumed);
            _hasRunResumedSubscription = false;
        }

        /// <summary>Subscribes to pause resume notifications once the event bus is ready</summary>
        [Server]
        private void SubscribeRunResumed()
        {
            if (_hasRunResumedSubscription || !GameEventBus.HasInstance)
            {
                return;
            }

            GameEventBus.Instance.Subscribe<RunResumedEvent>(OnRunResumed);
            _hasRunResumedSubscription = true;
        }

        private IEnumerator CoWaitForEventBus()
        {
            while (isServer && !GameEventBus.HasInstance)
            {
                yield return null;
            }

            _eventBusWaitRoutine = null;
            if (isServer)
            {
                SubscribeRunResumed();
            }
        }

        [Server]
        private void EnsureResolutionScheduler()
        {
            AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
            if (scheduler == null)
            {
                return;
            }

            if (G_ResolverRegistered && (G_ResolverScheduler == scheduler) &&
                (G_ResolverServerGeneration == AuthorityTickScheduler.ServerGeneration))
            {
                return;
            }

            if (G_ResolverRegistered && (G_ResolverScheduler != null) &&
                (G_ResolverScheduler != scheduler))
            {
                G_ResolverScheduler.Unregister(ServerResolvePending);
            }

            G_ResolverScheduler         = scheduler;
            G_ResolverScheduler.Register(REVIVE_RESOLUTION_PRIORITY, ServerResolvePending);
            G_ResolverRegistered        = true;
            G_ResolverServerGeneration = AuthorityTickScheduler.ServerGeneration;
        }

        private IEnumerator CoWaitForScheduler()
        {
            while (isServer && (AuthorityTickScheduler.ServerInstance == null))
            {
                yield return null;
            }

            _schedulerWaitRoutine = null;
            if (isServer)
            {
                EnsureResolutionScheduler();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetServerControllers()
        {
            G_ServerControllers.Clear();
            G_ResolverScheduler         = null;
            G_ResolverRegistered        = false;
            G_ResolverServerGeneration = 0UL;
        }

        /// <summary>Moves the spectate deadline by the portion of the pause not restored into the snapshot</summary>
        private void OnRunResumed(RunResumedEvent resumed)
        {
            if (_spectateEndServerTime <= 0.0)
            {
                ClearSpectateRestoreMarker();
                return;
            }

            double extension = CalculateResumeExtensionSeconds(
                resumed.PausedSeconds,
                NetworkTime.time,
                _spectateRestoreServerTime,
                _spectateRestoredWhilePaused);

            _spectateEndServerTime += extension;
            ClearSpectateRestoreMarker();
        }

        private double ResolveSpectateRemainingSeconds()
        {
            double referenceServerTime = NetworkTime.time;
            RunManager run = RunManager.ServerInstance;

            if ((run != null) && run.IsPaused)
            {
                referenceServerTime = run.PauseStartServerTime;

                if (_spectateRestoredWhilePaused &&
                    (referenceServerTime == _spectateRestorePauseStartServerTime))
                {
                    referenceServerTime = _spectateRestoreServerTime;
                }
            }

            return CalculateSpectateRemainingSeconds(_spectateEndServerTime,
                referenceServerTime);
        }

        private static double CalculateSpectateRemainingSeconds(double spectateEndServerTime,
            double referenceServerTime)
        {
            if (double.IsNaN(spectateEndServerTime) ||
                double.IsInfinity(spectateEndServerTime) ||
                (spectateEndServerTime <= 0.0) ||
                double.IsNaN(referenceServerTime) ||
                double.IsInfinity(referenceServerTime))
            {
                return 0.0;
            }

            return Math.Max(0.0, spectateEndServerTime - referenceServerTime);
        }

        private static bool IsValidSpectateRemainingSeconds(double remainingSeconds)
        {
            return !double.IsNaN(remainingSeconds) &&
                !double.IsInfinity(remainingSeconds) &&
                (remainingSeconds >= 0.0);
        }

        private static double CalculateResumeExtensionSeconds(double pausedSeconds,
            double resumeServerTime, double restoreServerTime, bool restoredWhilePaused)
        {
            if (double.IsNaN(pausedSeconds) || double.IsInfinity(pausedSeconds) ||
                (pausedSeconds <= 0.0))
            {
                return 0.0;
            }

            if (!restoredWhilePaused)
            {
                return pausedSeconds;
            }

            if (double.IsNaN(resumeServerTime) || double.IsInfinity(resumeServerTime) ||
                double.IsNaN(restoreServerTime) || double.IsInfinity(restoreServerTime))
            {
                return 0.0;
            }

            double elapsedAfterRestore = Math.Max(0.0, resumeServerTime - restoreServerTime);
            return Math.Min(pausedSeconds, elapsedAfterRestore);
        }

        private void ClearSpectateRestoreMarker()
        {
            _spectateRestoredWhilePaused         = false;
            _spectateRestoreServerTime           = 0.0;
            _spectateRestorePauseStartServerTime = 0.0;
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 40;

        /// <summary> 관전 남은 시간을 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            snapshot.SpectateRemainingSeconds = SpectateRemainingSeconds;
            snapshot.ReviveDeathGeneration   = _deathGeneration;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) ||
                !IsValidSpectateRemainingSeconds(snapshot.SpectateRemainingSeconds))
            {
                failure = "관전 남은 시간이 유효하지 않다";
                return false;
            }

            failure = null;
            return true;
        }

        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) ||
                !IsValidSpectateRemainingSeconds(snapshot.SpectateRemainingSeconds))
            {
                return false;
            }

            ClearSpectateRestoreMarker();

            _deathGeneration           = snapshot.ReviveDeathGeneration;
            _reviveCommittedGeneration =
                (snapshot.LifeState == PlayerLifeSnapshotState.Spectating)
                    ? NO_GENERATION : _deathGeneration;
            _pendingAutomaticRevive     = false;
            _pendingAutomaticGeneration = NO_GENERATION;
            _pendingRescueResolution    = false;
            _lastResolutionTick         = 0UL;
            _rescueTargetNetId           = NO_TARGET;
            _rescueProgress              = 0f;
            _rescueTargetKind            = PlayerRescueTargetKind.None;
            _rescueTargetGeneration      = NO_GENERATION;
            _rescueStartDamageGeneration = NO_GENERATION;

            if ((snapshot.LifeState == PlayerLifeSnapshotState.Spectating) &&
                (_deathGeneration == NO_GENERATION))
            {
                _deathGeneration = 1u;
            }

            if (snapshot.SpectateRemainingSeconds <= 0.0)
            {
                _spectateEndServerTime = 0.0;
                return true;
            }

            _spectateEndServerTime = (NetworkTime.time + snapshot.SpectateRemainingSeconds);

            RunManager run = RunManager.ServerInstance;
            if ((run != null) && run.IsPaused)
            {
                _spectateRestoredWhilePaused         = true;
                _spectateRestoreServerTime           = NetworkTime.time;
                _spectateRestorePauseStartServerTime = run.PauseStartServerTime;
            }

            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 관전 타이머와 구조 진행을 한 틱 진행한다 </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            EnsureResolutionScheduler();

            if (!GameDB.IsInitialized)
            {
                return;
            }

            ServerUpdateSpectate(now);
            ServerUpdateRescue(now);
        }

        [Server]
        private void ServerUpdateSpectate(double now)
        {
            CombatConfigSO combat = GameDB.Combat;

            if (_status.LifeState == PlayerLifeState.Downed)
            {
                if (_status.ServerBeginSpectate())
                {
                    AdvanceDeathGeneration();
                    _reviveCommittedGeneration = NO_GENERATION;
                    _pendingAutomaticRevive     = false;
                    _pendingAutomaticGeneration = NO_GENERATION;
                    _pendingRescueResolution    = false;
                    _lastResolutionTick         = 0UL;
                    ClearSpectateRestoreMarker();
                    _spectateEndServerTime = now + combat.SpectateDuration;
                    RecordDeathFact();

                    GDebug.Fail($"사망, {combat.SpectateDuration:F0}초 뒤 자동 부활");

                    PlayerStatus.ServerCheckPartyWipe();
                }

                return;
            }

            if (_status.LifeState != PlayerLifeState.Spectating)
            {
                return;
            }

            if (now < _spectateEndServerTime)
            {
                return;
            }

            if (_pendingAutomaticRevive)
            {
                return;
            }

            if (_deathGeneration == NO_GENERATION)
            {
                AdvanceDeathGeneration();
            }

            _pendingAutomaticRevive     = true;
            _pendingAutomaticGeneration = _deathGeneration;
        }

        [Server]
        private void ServerUpdateRescue(double now)
        {
            if (_rescueTargetNetId == NO_TARGET)
            {
                return;
            }

            if (!TryGetRescueTarget(_rescueTargetNetId, out ReviveController target))
            {
                ServerCancelRescue();
                return;
            }

            if (!ServerCanResolveRescue(now, target))
            {
                ServerCancelRescue();
                return;
            }

            float duration = Mathf.Max(ResolveRescueDuration(_rescueTargetKind),
                Constants.Combat.DAMAGE_EPSILON);

            _rescueProgress += (Time.fixedDeltaTime / duration);

            if (_rescueProgress < 1f)
            {
                return;
            }

            _rescueProgress          = 1f;
            _pendingRescueResolution = true;
        }

        private const float DEV_REVIVE_CURRENCY_LOSS = 0f; // 개발용 부활은 재화를 깎지 않는다

        /// <summary>Resolves rescue before automatic revive for the current server tick</summary>
        [Server]
        private static void ServerResolvePending(float fixedDeltaTime)
        {
            _ = fixedDeltaTime;

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || run.IsPaused || (run.Phase == RunPhase.Lobby) ||
                (run.Phase == RunPhase.Result) || !GameDB.IsInitialized)
            {
                return;
            }

            AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
            if (scheduler == null)
            {
                return;
            }

            ulong tickId = scheduler.TickId;

            // Rescue candidates are resolved first for the whole party. This is independent of
            // which player's movement callback registered the candidate first
            for (int i = 0; i < G_ServerControllers.Count; ++i)
            {
                ReviveController rescuer = G_ServerControllers[i];
                if ((rescuer == null) || !rescuer._pendingRescueResolution)
                {
                    continue;
                }

                if (!TryGetRescueTarget(rescuer._rescueTargetNetId,
                    out ReviveController target))
                {
                    rescuer.ServerCancelRescue();
                    continue;
                }

                bool automaticReady =
                    (rescuer._rescueTargetKind == PlayerRescueTargetKind.Spectating) &&
                    target._pendingAutomaticRevive &&
                    (target._pendingAutomaticGeneration == target._deathGeneration);
                if ((ReviveResolutionRules.Select(true, automaticReady) !=
                    ReviveResolutionKind.Rescue) ||
                    (target._status == null) ||
                    (target._lastResolutionTick == tickId))
                {
                    rescuer.ServerCancelRescue();
                    continue;
                }

                // Player movement callbacks run before this resolver. Revalidate immediately
                // before committing so the completion tick cannot use stale rescue state
                if (!rescuer.ServerCanResolveRescue(NetworkTime.time, target))
                {
                    rescuer.ServerCancelRescue();
                    continue;
                }

                uint targetGeneration = rescuer._rescueTargetGeneration;
                bool resolved;
                if (rescuer._rescueTargetKind == PlayerRescueTargetKind.Swallowed)
                {
                    resolved = rescuer.ServerRescueSwallowed(target, targetGeneration);
                    if (resolved)
                    {
                        GDebug.Success("삼켜짐 구조 완료");
                    }
                }
                else
                {
                    resolved = target.ServerRevive(GameDB.Combat.RescueCurrencyLossRatio,
                        targetGeneration);
                    if (resolved)
                    {
                        RecordRescueFact(rescuer, target, targetGeneration);
                        GDebug.Success("rescue revive complete");
                    }
                }

                if (resolved)
                {
                    target._lastResolutionTick = tickId;
                }

                rescuer.ServerCancelRescue();
            }

            for (int i = 0; i < G_ServerControllers.Count; ++i)
            {
                ReviveController target = G_ServerControllers[i];
                if ((target == null) || !target._pendingAutomaticRevive)
                {
                    continue;
                }

                if ((target._status == null) ||
                    (target._status.LifeState != PlayerLifeState.Spectating) ||
                    (target._pendingAutomaticGeneration != target._deathGeneration) ||
                    (target._deathGeneration == NO_GENERATION))
                {
                    target.ClearPendingAutomaticRevive();
                    continue;
                }

                if (target._lastResolutionTick == tickId)
                {
                    continue;
                }

                if (ReviveResolutionRules.Select(false, true) !=
                    ReviveResolutionKind.Automatic)
                {
                    continue;
                }

                if (target.ServerRevive(GameDB.Combat.ReviveCurrencyLossRatio,
                    target._pendingAutomaticGeneration))
                {
                    target._lastResolutionTick = tickId;
                }
            }
        }

        private void AdvanceDeathGeneration()
        {
            unchecked
            {
                ++_deathGeneration;
            }

            if (_deathGeneration == NO_GENERATION)
            {
                _deathGeneration = 1u;
            }
        }

        private void ClearPendingAutomaticRevive()
        {
            _pendingAutomaticRevive     = false;
            _pendingAutomaticGeneration = NO_GENERATION;
        }

        /// <summary>Revives every dead player for development fixtures without a currency loss</summary>
        [Server]
        public static int ServerReviveAll()
        {
            int revived = 0;

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if (!identity.TryGetComponent(out ReviveController controller))
                {
                    continue;
                }

                if ((controller._status == null) || (!controller._status.IsDead))
                {
                    continue;
                }

                if (controller._deathGeneration == NO_GENERATION)
                {
                    controller.AdvanceDeathGeneration();
                }

                if (controller.ServerRevive(DEV_REVIVE_CURRENCY_LOSS,
                    controller._deathGeneration))
                {
                    ++revived;
                }
            }

            GDebug.Server($"전체 부활: {revived}명");
            return revived;
        }

        /// <summary> 부활을 확정한다 </summary>
        [Server]
        private bool ServerRevive(float currencyLossRatio)
        {
            return ServerRevive(currencyLossRatio, _deathGeneration);
        }

        [Server]
        private bool ServerRevive(float currencyLossRatio, uint deathGeneration)
        {
            if ((_status == null) || (!_status.IsDead) ||
                (deathGeneration == NO_GENERATION) ||
                (_reviveCommittedGeneration == deathGeneration))
            {
                return false;
            }

            if (float.IsNaN(currencyLossRatio) || float.IsInfinity(currencyLossRatio) ||
                (currencyLossRatio < 0f) || (currencyLossRatio > 1f))
            {
                return false;
            }

            if (currencyLossRatio > 0f)
            {
                ParticipantId participant = ResolveParticipantId();
                if ((!participant.IsValid) || (!PartyWalletLocator.HasProvider))
                {
                    return false;
                }

                if (!PartyWalletLocator.Current.TryApplyReviveLoss(participant,
                    deathGeneration, currencyLossRatio, out int _))
                {
                    return false;
                }
            }

            CombatConfigSO combat = GameDB.Combat;

            Vector3 position = ResolveRevivePosition(combat);

            if (_movement != null)
            {
                _movement.ServerTeleport(position);
            }

            _status.ServerRevive(combat.ReviveHealthRatio, combat.ReviveInvincibleDuration);

            _spectateEndServerTime = 0.0;
            ClearSpectateRestoreMarker();

            _reviveCommittedGeneration = deathGeneration;
            ClearPendingAutomaticRevive();
            _pendingRescueResolution    = false;

            GDebug.Success($"부활, HP {combat.ReviveHealthRatio * 100f:F0}% / 무적 {combat.ReviveInvincibleDuration:F1}초");

            return true;
        }

        /// <summary>Resolves the participant identity that survives reconnects</summary>
        [Server]
        private ParticipantId ResolveParticipantId()
        {
            if ((_status != null) && _status.Participant.IsValid)
            {
                return _status.Participant;
            }

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

        private static float ResolveRescueDuration(PlayerRescueTargetKind targetKind)
        {
            if (!GameDB.IsInitialized)
            {
                return 0f;
            }

            if (targetKind == PlayerRescueTargetKind.Swallowed)
            {
                return (GameDB.Boss == null) ? 0f :
                    GameDB.Boss.SwallowRescueChannelSeconds;
            }

            return GameDB.Combat.RescueDuration;
        }

        [Server]
        private bool ServerRescueSwallowed(ReviveController target,
            uint expectedGeneration)
        {
            ParticipantId rescuer = ResolveParticipantId();
            if ((target == null) || (target._status == null) || (!rescuer.IsValid) ||
                (!GameDB.IsInitialized) || (GameDB.Boss == null) ||
                (!(target._status is IBossSwallowTarget swallowTarget)))
            {
                return false;
            }

            return swallowTarget.ServerTryRescueSwallowed(rescuer,
                expectedGeneration, GameDB.Boss.SwallowRestoreHealthRatio);
        }

        private void RecordDeathFact()
        {
            RunManager run = RunManager.ServerInstance;
            ParticipantId participant = ResolveParticipantId();
            if ((run == null) || (!participant.IsValid) || (_deathGeneration == NO_GENERATION))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordLife(RunPlayerLifeFact.Death(
                    run.RunId, participant, _deathGeneration));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReviveController] 사망 통계 기록 실패: {e}", this);
            }
        }

        private static void RecordRescueFact(ReviveController rescuer,
            ReviveController target, uint deathGeneration)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (rescuer == null) || (target == null))
            {
                return;
            }

            ParticipantId actor = rescuer.ResolveParticipantId();
            ParticipantId subject = target.ResolveParticipantId();
            if ((!actor.IsValid) || (!subject.IsValid))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordLife(RunPlayerLifeFact.Rescue(
                    run.RunId, actor, subject, deathGeneration));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReviveController] 구조 통계 기록 실패: {e}", rescuer);
            }
        }

        /// <summary>Moves a revive point toward the active barrier center when needed</summary>
        [Server]
        private Vector3 ResolveRevivePosition(CombatConfigSO combat)
        {
            Vector3      position = transform.position;
            IBarrierState barrier = BarrierStateLocator.Current;

            if ((barrier == null) || (!barrier.IsActive) || (!barrier.IsOutside(position)))
            {
                return position;
            }

            Vector3 center = barrier.Center;
            Vector3 delta  = position - center;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0f)
            {
                return position;
            }

            Vector3 inset = center + (delta.normalized * (barrier.Radius * combat.ReviveBarrierInsetRatio));
            inset.y = position.y;

            return ResolveGroundedPosition(inset, position);
        }

        /// <summary> 후보 지점을 지면에 앉힌다. 찾지 못하면 원래 자리를 쓴다 </summary>
        [Server]
        private Vector3 ResolveGroundedPosition(Vector3 candidate, Vector3 fallback)
        {
            Vector3 probeOrigin = candidate + (Vector3.up * Constants.Player.REVIVE_PROBE_UP);
            float   probeLength = (Constants.Player.REVIVE_PROBE_UP + Constants.Player.REVIVE_PROBE_DISTANCE);
            int     groundMask  = LayerMask.GetMask(Constants.Layer.GROUND, Constants.Layer.DEFAULT);

            if (!Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, probeLength, groundMask, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"[ReviveController] 부활 지점 아래에서 지면을 찾지 못해 사망 지점을 쓴다: {candidate}", this);
                return fallback;
            }

            Vector3 grounded = hit.point + (Vector3.up * Constants.Player.REVIVE_GROUND_OFFSET);

            if (IsBlocked(grounded))
            {
                Debug.LogWarning($"[ReviveController] 부활 지점이 막혀 있어 사망 지점을 쓴다: {grounded}", this);
                return fallback;
            }

            return grounded;
        }

        [Server]
        private bool IsBlocked(Vector3 groundPosition)
        {
            if (_capsule == null)
            {
                return false;
            }

            float radius = _capsule.radius;
            float height = Mathf.Max(_capsule.height, (radius * 2f));

            Vector3 bottom = groundPosition + (Vector3.up * radius);
            Vector3 top    = groundPosition + (Vector3.up * (height - radius));

            int blockingMask = LayerMask.GetMask(Constants.Layer.GROUND, Constants.Layer.DEFAULT);

            return Physics.CheckCapsule(bottom, top, radius, blockingMask, QueryTriggerInteraction.Ignore);
        }

        [Server]
        private void ServerCancelRescue()
        {
            _rescueTargetNetId           = NO_TARGET;
            _rescueTargetKind            = PlayerRescueTargetKind.None;
            _rescueTargetGeneration      = NO_GENERATION;
            _rescueStartDamageGeneration = NO_GENERATION;
            _rescueProgress              = 0f;
            _pendingRescueResolution     = false;
        }

        [Server]
        private bool ServerCanResolveRescue(double now, ReviveController target)
        {
            if (!IsRescueTargetCurrent(target, _rescueTargetKind,
                _rescueTargetGeneration))
            {
                return false;
            }

            return ServerCanContinueRescue(now, target);
        }

        private static bool IsRescueTargetCurrent(ReviveController target,
            PlayerRescueTargetKind expectedKind, uint expectedGeneration)
        {
            if ((target == null) || (target._status == null) ||
                (expectedGeneration == NO_GENERATION))
            {
                return false;
            }

            if (expectedKind == PlayerRescueTargetKind.Spectating)
            {
                return (target._status.LifeState == PlayerLifeState.Spectating) &&
                    (target._deathGeneration == expectedGeneration);
            }

            if (expectedKind == PlayerRescueTargetKind.Swallowed)
            {
                return target._status.IsSwallowed &&
                    (target._status.SwallowGeneration == expectedGeneration);
            }

            return false;
        }

        [Server]
        private bool ServerCanContinueRescue(double now, ReviveController target)
        {
            if (!IsRescueTargetCurrent(target, _rescueTargetKind,
                _rescueTargetGeneration))
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if ((_status == null) ||
                ((run != null) && !PlayerActionGate.CanAct(
                    run.Phase, _status.LifeState, run.IsPaused)) ||
                ((run == null) && (_status.IsDead || _status.IsSwallowed)) ||
                (_status.DamageGeneration != _rescueStartDamageGeneration))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.Combat;
            Vector3        delta  = target.transform.position - transform.position;

            if (delta.sqrMagnitude > (combat.RescueRadius * combat.RescueRadius))
            {
                return false;
            }

            if (!HasRescueLineOfSight(target))
            {
                return false;
            }

            if ((_roll != null) && (_roll.IsRolling))
            {
                return false;
            }

            if ((_attack != null) && (_attack.IsActionInProgress(now)))
            {
                return false;
            }

            if ((_parry != null) && (_parry.IsActionInProgress(now)))
            {
                return false;
            }

            return !IsMovingOnServer();
        }

        private bool HasRescueLineOfSight(ReviveController target)
        {
            Vector3 origin = ResolveRescueProbePosition(this);
            Vector3 destination = ResolveRescueProbePosition(target);
            int blockingMask = LayerMask.GetMask(Constants.Layer.GROUND,
                Constants.Layer.DEFAULT);

            return !Physics.Linecast(origin, destination, blockingMask,
                QueryTriggerInteraction.Ignore);
        }

        private static Vector3 ResolveRescueProbePosition(ReviveController controller)
        {
            if ((controller == null) || (controller._capsule == null))
            {
                return (controller == null) ? Vector3.zero :
                    controller.transform.position + Vector3.up;
            }

            return controller.transform.TransformPoint(controller._capsule.center);
        }

        [Server]
        private bool IsMovingOnServer()
        {
            if (_rigidbody == null)
            {
                return false;
            }

            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = 0f;

            return (velocity.sqrMagnitude > (RESCUE_MOVE_EPSILON * RESCUE_MOVE_EPSILON));
        }

        private static bool TryGetRescueTarget(uint netId, out ReviveController target)
        {
            target = null;

            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
            {
                return false;
            }

            target = identity.GetComponent<ReviveController>();
            return (target != null);
        }

        [Server]
        public void ResetForNewRun()
        {
            _spectateEndServerTime       = 0.0;
            _rescueTargetNetId           = NO_TARGET;
            _rescueTargetKind            = PlayerRescueTargetKind.None;
            _rescueTargetGeneration      = NO_GENERATION;
            _rescueStartDamageGeneration = NO_GENERATION;
            _rescueProgress              = 0f;
            ClearSpectateRestoreMarker();
            _deathGeneration             = NO_GENERATION;
            _reviveCommittedGeneration   = NO_GENERATION;
            _pendingAutomaticRevive      = false;
            _pendingAutomaticGeneration  = NO_GENERATION;
            _pendingRescueResolution     = false;
            _lastResolutionTick          = 0UL;
            _serverHoldGeneration        = 0u;
            _holdGeneration              = 0u;
            _isHoldingRescue             = false;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdBeginRescue(uint targetNetId, PlayerRescueTargetKind targetKind,
            uint targetGeneration, uint holdGeneration)
        {
            ServerBeginRescue(targetNetId, targetKind, targetGeneration, holdGeneration);
        }

        /// <summary> 사람과 서버 제어자가 동일한 대상, 홀드 세대 검증으로 구조를 시작한다 </summary>
        [Server]
        public bool ServerBeginRescue(uint targetNetId, PlayerRescueTargetKind targetKind,
            uint targetGeneration, uint holdGeneration)
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            if (targetNetId == netId)
            {
                return false;
            }

            if (holdGeneration <= _serverHoldGeneration)
            {
                return false;
            }

            _serverHoldGeneration = holdGeneration;

            if (!TryGetRescueTarget(targetNetId, out ReviveController target))
            {
                ServerCancelRescue();
                return false;
            }

            _rescueTargetNetId           = targetNetId;
            _rescueTargetKind            = targetKind;
            _rescueTargetGeneration      = targetGeneration;
            _rescueStartDamageGeneration = (_status == null) ? NO_GENERATION :
                _status.DamageGeneration;
            _rescueProgress              = 0f;

            if (!ServerCanContinueRescue(NetworkTime.time, target))
            {
                ServerCancelRescue();
                return false;
            }

            return true;
        }

        /// <summary> 홀드를 놓는다 </summary>
        [Command]
        private void CmdCancelRescue(uint holdGeneration)
        {
            ServerCancelRescueRequest(holdGeneration);
        }

        /// <summary> 늦게 도착한 취소가 새 홀드를 취소하지 않도록 세대를 확인한다 </summary>
        [Server]
        public bool ServerCancelRescueRequest(uint holdGeneration)
        {
            if ((holdGeneration == 0u) || (holdGeneration < _serverHoldGeneration))
            {
                return false;
            }

            _serverHoldGeneration = holdGeneration;

            ServerCancelRescue();
            return true;
        }

        /****************************************
        *                Input
        ****************************************/

        private void HandleRescueInput()
        {
            if (!LocalGameplayInputGate.AllowsGameplayInput)
            {
                if (_isHoldingRescue)
                {
                    _isHoldingRescue = false;
                    CmdCancelRescue(_holdGeneration);
                }

                return;
            }

            bool isHeld = Input.GetKey(Constants.Player.KEY_INTERACT);

            if ((isHeld) && (!_isHoldingRescue))
            {
                _isHoldingRescue = true;
                ++_holdGeneration;

                if (TryFindNearestRescueTarget(out uint targetNetId,
                    out PlayerRescueTargetKind targetKind, out uint targetGeneration))
                {
                    CmdBeginRescue(targetNetId, targetKind,
                        targetGeneration, _holdGeneration);
                }

                return;
            }

            if ((!isHeld) && (_isHoldingRescue))
            {
                _isHoldingRescue = false;

                CmdCancelRescue(_holdGeneration);
            }
        }

        /// <summary> 사거리 안에서 가장 가까운 구조 가능한 팀원을 찾는다 </summary>
        private bool TryFindNearestRescueTarget(out uint targetNetId,
            out PlayerRescueTargetKind targetKind, out uint targetGeneration)
        {
            targetNetId = NO_TARGET;
            targetKind = PlayerRescueTargetKind.None;
            targetGeneration = NO_GENERATION;

            if (!GameDB.IsInitialized)
            {
                return false;
            }

            float radius = GameDB.Combat.RescueRadius;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, _rescueBuffer, _playerLayerMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                Collider candidate = _rescueBuffer[i];
                if (candidate == null)
                {
                    continue;
                }

                Transform root = candidate.transform.root;
                if (root == transform)
                {
                    continue;
                }

                PlayerStatus status = root.GetComponent<PlayerStatus>();
                ReviveController target = root.GetComponent<ReviveController>();
                if ((status == null) || (target == null) ||
                    (!TryResolveRescueTarget(status, target,
                        out PlayerRescueTargetKind candidateKind,
                        out uint candidateGeneration)))
                {
                    continue;
                }

                NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
                if (identity == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, root.position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                targetNetId  = identity.netId;
                targetKind = candidateKind;
                targetGeneration = candidateGeneration;
            }

            return (targetNetId != NO_TARGET);
        }

        private static bool TryResolveRescueTarget(PlayerStatus status,
            ReviveController controller, out PlayerRescueTargetKind targetKind,
            out uint targetGeneration)
        {
            targetKind = PlayerRescueTargetKind.None;
            targetGeneration = NO_GENERATION;
            if ((status == null) || (controller == null))
            {
                return false;
            }

            if ((status.LifeState == PlayerLifeState.Spectating) &&
                (controller._deathGeneration != NO_GENERATION))
            {
                targetKind = PlayerRescueTargetKind.Spectating;
                targetGeneration = controller._deathGeneration;
                return true;
            }

            if (status.IsSwallowed && (status.SwallowGeneration != NO_GENERATION))
            {
                targetKind = PlayerRescueTargetKind.Swallowed;
                targetGeneration = status.SwallowGeneration;
                return true;
            }

            return false;
        }
    }
}
