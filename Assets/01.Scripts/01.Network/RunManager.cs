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

namespace Geumeum.Network
{
    /// <summary> 런 시계와 상태 머신 </summary>
    public class RunManager : NetworkBehaviour, IRunTimeSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCORE_CONFIGURATION_ERROR_MESSAGE =
            "점수 설정 오류로 이번 런의 점수와 등급을 확정하지 못했다.";

        [SyncVar] private uint    _runIdValue = 0u;
        [SyncVar] private RunMode _mode       = RunMode.Standard;

        [SyncVar(hook = nameof(OnResultChangedLocal))] private RunResultSnapshot _result = default;

        private ulong _sessionIdValue = 0UL; // 서버 프로세스 수명 동안 결과 기록 키에 쓰는 값

        [SyncVar] private float      _elapsedSeconds     = 0f;
        [SyncVar] private float      _bossElapsedSeconds = 0f;
        [SyncVar] private RunOutcome _outcome            = RunOutcome.None;

        [SyncVar(hook = nameof(OnPhaseChangedLocal))] private RunPhase _phase = RunPhase.Lobby;

        [SyncVar(hook = nameof(OnPauseReasonsChangedLocal))] private RunPauseReason _pauseReasons = RunPauseReason.None;

        [SyncVar] private BossEntranceState _bossEntranceState                = BossEntranceState.Idle;
        [SyncVar] private float             _bossEntranceDeadlineFieldSeconds = 0f;

        private RunPhaseTimeline _timeline               = null;  // 서버 전용
        private double           _runStartServerTime     = 0.0;   // 서버 전용
        private float            _previousElapsedSeconds = 0f;    // 서버 전용
        private Coroutine        _tickRoutine            = null;
        private Coroutine        _returnRoutine          = null;   // 서버 전용
        private Coroutine        _schedulerWaitRoutine   = null;   // 서버 전용
        private double           _pauseStartServerTime   = 0.0;    // 서버 전용
        private double           _bossStartServerTime    = 0.0;    // 서버 전용
        private bool             _hasBossClockStarted    = false;  // 서버 전용

        private bool      _hasEnemyDiedSubscription = false;  // 서버 전용
        private Coroutine _enemyDiedWaitRoutine     = null;   // 서버 전용

        // 종료 후보
        private RunOutcome _candidateOutcome = RunOutcome.None; // 서버 전용
        private ulong      _candidateTickId  = 0UL;             // 서버 전용

        private readonly List<RunPhase> _enteredBuffer = new List<RunPhase>();
        private WaitForSeconds          _tickWait      = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static RunManager ServerInstance { get; private set; }

        /// <summary> 서버와 클라이언트 양쪽에서 읽는 인스턴스. 판정이 아니라 표현과 예측 차단에만 쓴다 </summary>
        public static RunManager LocalInstance { get; private set; }

        /// <summary> 어떤 사유로든 세계가 얼어 있다. 시계도 멈춘다 </summary>
        public bool IsPaused => (_pauseReasons != RunPauseReason.None);

        /// <summary>Server NetworkTime when the current pause began; zero while the run is active.</summary>
        public double PauseStartServerTime => _pauseStartServerTime;

        /// <summary> 필드 구간의 경과 시간(초). 보스전에서는 더 이상 늘지 않는다 </summary>
        public float ElapsedSeconds => _elapsedSeconds;

        /// <summary> 보스전 경과 시간(초). 보스 시계가 시작하기 전에는 0이다 </summary>
        public float BossElapsedSeconds => _bossElapsedSeconds;

        /// <summary> 점수가 쓰는 기준 시간. 두 시계의 합이고 전실, 컷신, 이탈 대기는 들어가지 않는다 </summary>
        public float ScoreElapsedSeconds => (_elapsedSeconds + _bossElapsedSeconds);

        /****************************************
        *           Interface Members
        ****************************************/

        double IRunTimeSource.FieldElapsedSeconds    => _elapsedSeconds;
        double IRunTimeSource.BossElapsedSeconds     => _bossElapsedSeconds;
        double IRunTimeSource.GameplayElapsedSeconds => ScoreElapsedSeconds;

        /// <summary> 지금 걸려 있는 정지 사유. 하나라도 남아 있으면 런은 멈춘 채다 </summary>
        public RunPauseReason PauseReasons => _pauseReasons;

        /// <summary> 정식 보스 입구의 카운트다운과 전실 경유 상태 </summary>
        public BossEntranceState EntranceState => _bossEntranceState;

        /// <summary> 카운트다운이 끝나는 필드 시각. 시작 전에는 0이다 </summary>
        public float BossEntranceDeadlineFieldSeconds => _bossEntranceDeadlineFieldSeconds;

        /// <summary> UI가 표시할 남은 초. 필드 시계가 멈추면 이 값도 그대로다 </summary>
        public float BossEntranceCountdownRemainingSeconds => BossEntranceRules.GetRemaining(
            _bossEntranceState, _elapsedSeconds, _bossEntranceDeadlineFieldSeconds);

        /// <summary> 이 프로세스의 호스트 UI가 참가자 이탈 또는 전환 실패 대기를 포기할 수 있는가 </summary>
        public bool CanLocalAbandonParticipantWait
        {
            get
            {
                RunSceneDirector director = RunSceneDirector.LocalInstance;
                bool hasTransitionFailure = ((director != null) &&
                                             (director.State == RunTransitionState.Failed));
                return BossEntranceRules.CanAbandonParticipantWait(
                    NetworkServer.activeHost, _phase, _pauseReasons, hasTransitionFailure);
            }
        }

        public RunPhase   Phase   => _phase;
        public RunOutcome Outcome => _outcome;

        /// <summary> 지금 런의 식별자. 로비에서는 직전 런의 값이 남아 있다 </summary>
        public RunId RunId => new RunId(_runIdValue);

        /// <summary> 정식 런인지 트라이인지. 리더보드 등록 여부가 여기서 갈린다 </summary>
        public RunMode Mode => _mode;

        /// <summary> 확정된 결과. 아직 끝나지 않았으면 Outcome이 None이다 </summary>
        public RunResultSnapshot Result => _result;

        /// <summary> 서버 확정 결과가 클라이언트에 도착했을 때 발생한다 </summary>
        public event Action<RunResultSnapshot> ResultChanged;

        /// <summary> 참가자 대기 포기 요청을 서버가 수락했는지 요청한 호스트에게 알린다 </summary>
        public event Action<bool> AbandonParticipantWaitRequestCompleted;

        private ulong CurrentTickId
        {
            get
            {
                AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
                return ((scheduler == null) ? 0UL : scheduler.TickId);
            }
        }

        /// <summary> 드랍 가중치, 잡귀 강화 배수의 키. Phase에서 파생되므로 따로 동기화하지 않는다 </summary>
        public int FieldLevel
        {
            get
            {
                if (!GameDB.IsInitialized)
                {
                    return 0;
                }

                if (!GameDB.Barrier.TryGetPhase(_phase, out BarrierPhaseSO.PhaseEntry entry))
                {
                    return 0;
                }

                return entry.FieldLevel;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()  => RunTimeLocator.Register(this);
        private void OnDisable() => RunTimeLocator.Unregister(this);

        private void Awake()
        {
            syncInterval = Constants.Network.RUN_TIME_SYNC_INTERVAL;
            _tickWait    = new WaitForSeconds(Constants.Network.SERVER_TICK_INTERVAL);
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnPhaseChangedLocal(RunPhase oldPhase, RunPhase newPhase)
        {
            GDebug.Log(GLogKind.Server, $"런 단계 {oldPhase} -> {newPhase}");

            RunSoundDirector.OnPhaseChanged(newPhase);
        }

        private void OnPauseReasonsChangedLocal(RunPauseReason oldReasons, RunPauseReason newReasons)
        {
            bool wasPaused = (oldReasons != RunPauseReason.None);
            bool isPaused  = (newReasons != RunPauseReason.None);

            if (wasPaused == isPaused)
            {
                return;
            }

            // 이탈 대기로 멈춘 것인지 씬 전환인지 사유까지 봐야 3인 테스트에서 원인을 안다
            if (isPaused)
            {
                GDebug.Warn($"런 정지: {newReasons}");
            }
            else
            {
                GDebug.Log(GLogKind.Server, "런 재개");
            }

            RunSoundDirector.OnPausedChanged(isPaused);
        }

        private void OnResultChangedLocal(RunResultSnapshot oldResult, RunResultSnapshot newResult)
        {
            ResultChanged?.Invoke(newResult);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            LocalInstance = this;
        }

        public override void OnStopClient()
        {
            if (LocalInstance == this)
            {
                LocalInstance = null;
            }
        }

        public override void OnStartServer()
        {
            ServerInstance = this;
            LocalInstance  = this;

            if (_sessionIdValue == 0UL)
            {
                _sessionIdValue = CreateSessionId();
            }

            ServerResetState();

            _timeline = GameDB.IsInitialized ? RunTimelineBuilder.Build(GameDB.Barrier) : RunTimelineBuilder.Build(null);

            ServerSubscribeEnemyDied();

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.RegisterControl(ServerResolveOutcome);
                return;
            }

            // 씬 오브젝트 간 OnStartServer 순서는 Mirror가 보장하지 않는다. 여기서 포기하면
            // 런 종료 판정이 한 판 내내 돌지 않는다 - 뜰 때까지 기다렸다가 등록한다
            _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
        }

        /// <summary>
        /// 보스 사망은 AI가 사실로만 발행하고 런 종료 판단은 여기서 한다.
        /// 버스가 아직 없으면 뜰 때까지 기다린다 - 포기하면 보스를 죽여도 런이 끝나지 않는다
        /// </summary>
        [Server]
        private void ServerSubscribeEnemyDied()
        {
            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
                _hasEnemyDiedSubscription = true;
                return;
            }

            _enemyDiedWaitRoutine = StartCoroutine(CoWaitForEventBus());
        }

        private IEnumerator CoWaitForEventBus()
        {
            while (!GameEventBus.HasInstance)
            {
                yield return null;
            }

            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
            _hasEnemyDiedSubscription = true;
            _enemyDiedWaitRoutine     = null;
        }

        [Server]
        private void HandleEnemyDied(EnemyDiedEvent evt)
        {
            if (!evt.IsBoss)
            {
                return;
            }

            // 승리는 RunOutcomeArbiter에서 우선순위가 가장 높다. 같은 틱에 시간 초과나
            // 전멸이 함께 와도 TryFinishRun 안의 중재가 하나만 남긴다
            TryFinishRun(RunOutcome.Victory);
        }

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.RegisterControl(ServerResolveOutcome);
            _schedulerWaitRoutine = null;
        }

        public override void OnStopServer()
        {
            IRunResultFactsProvider factsProvider = RunResultFactsProviderLocator.Current;
            if (factsProvider != null)
            {
                factsProvider.ResetRun(RunId);
            }

            MarkRegistry.ClearAll();
            CombatClock.ClearAll();

            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.UnregisterControl(ServerResolveOutcome);
            }

            if (_enemyDiedWaitRoutine != null)
            {
                StopCoroutine(_enemyDiedWaitRoutine);
                _enemyDiedWaitRoutine = null;
            }

            if (_hasEnemyDiedSubscription && GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
            }

            _hasEnemyDiedSubscription = false;

            StopTick();
            StopReturnToLobby();

            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런을 시작한다 </summary>
        [Server]
        public bool StartRun(RunMode mode = RunMode.Standard)
        {
            if (_phase != RunPhase.Lobby)
            {
                Debug.LogWarning($"[RunManager] Lobby가 아닌 상태에서 런 시작을 요청했다: {_phase}", this);
                return false;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((director != null) && (director.CurrentScene != RunSceneId.None) &&
                ((director.State != RunTransitionState.Idle) ||
                 (director.CurrentScene != RunSceneId.Lobby)))
            {
                Debug.LogWarning(
                    $"[RunManager] 로비 씬 준비가 끝나지 않아 런을 시작할 수 없다: " +
                    $"scene={director.CurrentScene}, transition={director.State}", this);
                return false;
            }

            if (!_timeline.TryGetPhaseAt(0f, out RunPhase firstPhase))
            {
                Debug.LogError("[RunManager] 0초에 해당하는 구간이 없어 런을 시작할 수 없다", this);
                return false;
            }

            MarkRegistry.ClearAll();
            CombatClock.ClearAll();
            _runIdValue = RunId.Next().Value;
            _mode       = mode;
            _result     = default;

            IRunResultFactsProvider factsProvider = RunResultFactsProviderLocator.Current;
            if (factsProvider == null)
            {
                Debug.LogError("[RunManager] 결과 사실 provider가 없어 이 런의 정산은 미집계로 남는다", this);
            }
            else if (!factsProvider.BeginRun(RunId))
            {
                Debug.LogError($"[RunManager] 결과 사실 수집을 시작하지 못했다: run={RunId}", this);
            }

            _runStartServerTime     = NetworkTime.time;
            _previousElapsedSeconds = 0f;
            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
            _bossEntranceState                = BossEntranceState.Idle;
            _bossEntranceDeadlineFieldSeconds = 0f;
            _outcome                = RunOutcome.None;
            _candidateOutcome       = RunOutcome.None;

            SetSimulationEnabled(true);

            CommitPhase(firstPhase);
            StartTick();
            return true;
        }

        /// <summary> 종료 사유를 후보로 등록한다 </summary>
        [Server]
        public bool TryFinishRun(RunOutcome outcome)
        {
            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (outcome == RunOutcome.None))
            {
                return false;
            }

            if (!RunOutcomeArbiter.ShouldReplace(_candidateOutcome, outcome))
            {
                return false;
            }

            _candidateOutcome = outcome;
            _candidateTickId  = CurrentTickId;

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                ServerCommitOutcome(outcome);
            }

            return true;
        }

        /// <summary> 이탈한 사람을 기다리며 런을 얼린다 </summary>
        [Server]
        public bool PauseRun(string reason)
        {
            return ServerAddPauseReason(RunPauseReason.ParticipantMissing, reason);
        }

        /// <summary> 기다리던 사람이 돌아와 런을 재개한다 </summary>
        [Server]
        public bool ResumeRun()
        {
            return ServerClearPauseReason(RunPauseReason.ParticipantMissing);
        }

        /// <summary> 정지 사유를 하나 건다 </summary>
        [Server]
        public bool ServerAddPauseReason(RunPauseReason reason, string detail)
        {
            if ((reason == RunPauseReason.None) || (_phase == RunPhase.Lobby) || (_phase == RunPhase.Result))
            {
                return false;
            }

            if ((_pauseReasons & reason) != 0)
            {
                return false;
            }

            bool wasPaused = IsPaused;
            _pauseReasons |= reason;

            if (!wasPaused)
            {
                _pauseStartServerTime = NetworkTime.time;

                StopTick();
                SetSimulationEnabled(false);

                if (!GameEventBus.HasInstance)
                {
                    Debug.LogError("[RunManager] GameEventBus가 없어 정지를 알리지 못했다, 물리 이동이 계속될 수 있다", this);
                }
                else
                {
                    GameEventBus.Instance.Publish(new RunPausedEvent(_pauseReasons));
                }
            }

            Debug.Log($"[RunManager] 정지 사유를 걸었다: {reason}({detail}), 현재 사유={_pauseReasons}, phase={_phase}");
            return true;
        }

        /// <summary> 정지 사유를 하나 푼다 </summary>
        [Server]
        public bool ServerClearPauseReason(RunPauseReason reason)
        {
            if ((reason == RunPauseReason.None) || ((_pauseReasons & reason) == 0))
            {
                return false;
            }

            _pauseReasons &= ~reason;

            if (IsPaused)
            {
                Debug.Log($"[RunManager] 정지 사유를 풀었다: {reason}, 남은 사유={_pauseReasons}");
                return true;
            }

            double pausedSeconds = NetworkTime.time - _pauseStartServerTime;

            if (_hasBossClockStarted)
            {
                _bossStartServerTime += pausedSeconds;
            }

            _runStartServerTime  += pausedSeconds;
            _pauseStartServerTime = 0.0;

            SetSimulationEnabled(true);
            StartTick();

            if (!GameEventBus.HasInstance)
            {
                Debug.LogError("[RunManager] GameEventBus가 없어 재개를 알리지 못했다, 관전 타이머가 어긋난다", this);
            }
            else
            {
                GameEventBus.Instance.Publish(new RunResumedEvent(pausedSeconds));
            }

            Debug.Log($"[RunManager] 런을 재개했다: phase={_phase}, 경과={_elapsedSeconds:F1}초, 멈춘 시간={pausedSeconds:F1}초");
            return true;
        }

        /// <summary> 이탈처럼 런을 더 진행할 수 없을 때 Result로 확정한다 </summary>
        [Server]
        public void AbortRun(string reason)
        {
            ServerTryAbortRun(reason);
        }

        /// <summary>
        /// 필드의 정식 입구가 서버에서 감지한 참가자로 카운트다운을 시작한다.
        /// 같은 입구의 자식 collider가 겹쳐 호출돼도 최초 deadline만 유지한다
        /// </summary>
        [Server]
        public bool ServerTryStartBossEntranceCountdown(NetworkConnectionToClient sender)
        {
            if ((sender == null) || (IsPaused) || (!GameDB.IsInitialized))
            {
                return false;
            }

            if ((!NetworkServer.connections.TryGetValue(sender.connectionId,
                    out NetworkConnectionToClient registered)) || (!ReferenceEquals(sender, registered)) ||
                (sender.identity == null))
            {
                return false;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((director == null) || (director.CurrentScene != RunSceneId.Field))
            {
                return false;
            }

            IPlayerRunLifecycle lifecycle = sender.identity.GetComponent<IPlayerRunLifecycle>();
            if (lifecycle == null)
            {
                return false;
            }

            RunParticipantSnapshot snapshot = new RunParticipantSnapshot();
            if ((!lifecycle.CaptureRunState(snapshot)) ||
                (!BossEntranceRules.CanEnter(_phase, snapshot.LifeState, snapshot.Health)))
            {
                return false;
            }

            float fieldLimitSeconds   = GameDB.Barrier.FieldTimeLimitSeconds;
            float currentFieldSeconds = Mathf.Min(
                (float) (NetworkTime.time - _runStartServerTime), fieldLimitSeconds);
            return ServerStartBossEntranceCountdown(currentFieldSeconds, fieldLimitSeconds,
                GameDB.Barrier.BossEntryCountdownSeconds, "입구 진입");
        }

        /// <summary> 호스트 UI가 참가자 이탈 대기를 끝내 달라고 서버에 요청한다 </summary>
        public bool RequestAbandonParticipantWait()
        {
            if ((!NetworkClient.active) || (!NetworkClient.ready))
            {
                return false;
            }

            CmdRequestAbandonParticipantWait();
            return true;
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestAbandonParticipantWait(NetworkConnectionToClient sender = null)
        {
            bool accepted = ServerTryAbandonParticipantWait(sender);
            if ((sender != null) && NetworkServer.active)
            {
                TargetAbandonParticipantWaitRequestCompleted(sender, accepted);
            }
        }

        [TargetRpc]
        private void TargetAbandonParticipantWaitRequestCompleted(
            NetworkConnection target, bool accepted)
        {
            ObserverDispatch.Invoke(AbandonParticipantWaitRequestCompleted, accepted,
                this, nameof(RunManager), nameof(AbandonParticipantWaitRequestCompleted));
        }

        [Server]
        private bool ServerTryAbandonParticipantWait(NetworkConnectionToClient sender)
        {
            bool isHostConnection = (NetworkServer.activeHost && (sender != null) &&
                                     ReferenceEquals(sender, NetworkServer.localConnection));
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            bool hasTransitionFailure = ((director != null) &&
                                         (director.State == RunTransitionState.Failed));
            if (!BossEntranceRules.CanAbandonParticipantWait(
                    isHostConnection, _phase, _pauseReasons, hasTransitionFailure))
            {
                return false;
            }

            if (hasTransitionFailure && (_phase == RunPhase.Result))
            {
                return director.ServerRetryFailedResultTransition();
            }

            if (hasTransitionFailure && (_phase == RunPhase.Lobby))
            {
                return director.ServerStopSessionAfterFailedTransition();
            }

            if (hasTransitionFailure && (!director.ServerArmTerminalExit()))
            {
                return false;
            }

            return ServerTryAbortRun(hasTransitionFailure
                ? "호스트가 실패한 씬 전환을 끝냈다"
                : "호스트가 참가자 이탈 대기를 끊었다");
        }

        /// <summary>
        /// 보스 구간을 연다. 시계는 여기서 켜지 않는다 - 준비와 개시를 나눈다.
        /// 아레나 씬 전환과 바인딩이 끝나 RunSceneDirector가 Idle로 돌아온 뒤에
        /// ServerTickBossClock이 시계를 시작한다. 로딩 중에 제한 시간이 깎이면 안 된다
        /// </summary>
        [Server]
        public bool ServerBeginBossPhase()
        {
            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (_phase == RunPhase.Boss))
            {
                return false;
            }

            CommitPhase(RunPhase.Boss);
            return true;
        }

        /// <summary> 같은 씬에서 서버가 재시작해도 이전 런의 phase, 시각, outcome이 남지 않게 한다 </summary>
        [Server]
        public void ServerResetState()
        {
            IRunResultFactsProvider factsProvider = RunResultFactsProviderLocator.Current;
            if (factsProvider != null)
            {
                factsProvider.ResetRun(RunId);
            }

            StopTick();
            StopReturnToLobby();
            MarkRegistry.ClearAll();
            CombatClock.ClearAll();

            _phase                  = RunPhase.Lobby;
            _outcome                = RunOutcome.None;
            _candidateOutcome       = RunOutcome.None;
            _pauseReasons           = RunPauseReason.None;
            _pauseStartServerTime   = 0.0;
            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
            _bossEntranceState                = BossEntranceState.Idle;
            _bossEntranceDeadlineFieldSeconds = 0f;
            _result                  = default;
        }

        /// <summary> Result로 끝난 런을 로비 상태로 되돌린다 </summary>
        [Server]
        public void ServerReturnToLobby()
        {
            if (_phase != RunPhase.Result)
            {
                return;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            bool isDevelopmentScene = ((director == null) ||
                                       (director.CurrentScene == RunSceneId.None));
            bool isResultReady = ((director != null) &&
                                  (director.State == RunTransitionState.Idle) &&
                                  (director.CurrentScene == RunSceneId.Result));
            if ((!isDevelopmentScene) && (!isResultReady))
            {
                Debug.LogWarning(
                    $"[RunManager] Result 씬 준비 전에는 Lobby phase를 확정하지 않는다: " +
                    $"scene={director.CurrentScene}, transition={director.State}", this);
                return;
            }

            StopReturnToLobby();

            ServerResetRunOwnedState();

            SetSimulationEnabled(true);

            SetPhase(RunPhase.Lobby);

            GeumeumNetworkManager manager = NetworkManager.singleton as GeumeumNetworkManager;
            if (manager == null)
            {
                Debug.LogError("[RunManager] GeumeumNetworkManager를 찾지 못해 입장 잠금을 풀지 못했다", this);
            }
            else
            {
                manager.UnlockAdmission();
            }

            LobbyController lobby = LobbyController.ServerInstance;
            if (lobby != null)
            {
                lobby.ServerResetForNewRun();
            }

            Debug.Log("[RunManager] 로비로 복귀했다, 입장 잠금 해제");
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void SetSimulationEnabled(bool isEnabled)
        {
            AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
            if (scheduler == null)
            {
                return;
            }

            scheduler.SetSimulationEnabled(isEnabled);
        }

        [Server]
        private void ServerStartBossClock()
        {
            if (_hasBossClockStarted)
            {
                return;
            }

            _bossStartServerTime = NetworkTime.time;
            _hasBossClockStarted = true;
            _bossElapsedSeconds  = 0f;

            Debug.Log("[RunManager] 보스 시계를 시작했다: 필드 경과=" + _elapsedSeconds.ToString("F1") + "초");
        }

        [Server]
        private void ServerResolveOutcome()
        {
            if (_candidateOutcome == RunOutcome.None)
            {
                return;
            }

            if ((CurrentTickId != 0UL) && (CurrentTickId <= _candidateTickId))
            {
                return;
            }

            ServerCommitOutcome(_candidateOutcome);
        }

        /// <summary> 결과를 확정하고 Result로 넘긴다 </summary>
        [Server]
        private void ServerCommitOutcome(RunOutcome outcome)
        {
            _candidateOutcome = RunOutcome.None;

            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (outcome == RunOutcome.None))
            {
                return;
            }

            _outcome                          = outcome;
            _pauseReasons                     = RunPauseReason.None;
            _bossEntranceState                = BossEntranceState.Idle;
            _bossEntranceDeadlineFieldSeconds = 0f;

            _result = BuildResultSnapshot(outcome);

            MarkRegistry.ClearAll();
            CombatClock.ClearAll();

            StopTick();
            CommitPhase(RunPhase.Result);

            SetSimulationEnabled(false);

            ServerTryStartReturnToLobbyCountdown();

            Debug.Log($"[RunManager] 런을 끝냈다: {outcome}, 필드={_elapsedSeconds:F1}초, 보스={_bossElapsedSeconds:F1}초");
        }

        [Server]
        private RunResultSnapshot BuildResultSnapshot(RunOutcome outcome)
        {
            RunResultSnapshot snapshot = new RunResultSnapshot
            {
                SessionIdValue      = _sessionIdValue,
                RunIdValue          = _runIdValue,
                Mode                = _mode,
                Outcome             = outcome,
                FieldElapsedSeconds = _elapsedSeconds,
                BossElapsedSeconds  = _bossElapsedSeconds,
                ScoreTimeLimitSeconds = GameDB.Barrier.FieldTimeLimitSeconds +
                    GameDB.Boss.BossTimeLimitSeconds,
                BossKilled          = (outcome == RunOutcome.Victory),
            };

            IRunResultFactsProvider factsProvider = RunResultFactsProviderLocator.Current;
            bool hasFacts = ((factsProvider != null) && factsProvider.TryFreeze(ref snapshot));
            if (!hasFacts)
            {
                snapshot.FactsState      = RunResultFactsState.Unavailable;
                snapshot.ResultRevision = 0u;
                snapshot.Participants   = null;
                Debug.LogError($"[RunManager] 서버 결과 사실을 동결하지 못했다: run={RunId}", this);
                return snapshot;
            }

            ApplyScore(ref snapshot, outcome);

            return snapshot;
        }

        private void ApplyScore(ref RunResultSnapshot snapshot, RunOutcome outcome)
        {
            if (!RunResultProviderLocator.HasProvider)
            {
                snapshot.ScoreRuleVersion  = 0;
                snapshot.Score             = 0;
                snapshot.EvaluationGrade   = RunEvaluationGrade.None;
                snapshot.EvaluationMessage = SCORE_CONFIGURATION_ERROR_MESSAGE;
                Debug.LogError(
                    $"[RunManager] 결과 확정 시 점수 provider가 없다: run={RunId}", this);
                return;
            }

            IRunResultProvider provider = RunResultProviderLocator.Current;
            int scoreRuleVersion = provider.ScoreRuleVersion;
            if (scoreRuleVersion <= 0)
            {
                snapshot.ScoreRuleVersion  = 0;
                snapshot.Score             = 0;
                snapshot.EvaluationGrade   = RunEvaluationGrade.None;
                snapshot.EvaluationMessage = SCORE_CONFIGURATION_ERROR_MESSAGE;
                Debug.LogError(
                    $"[RunManager] 점수 규칙 판이 유효하지 않다: run={RunId}, version={scoreRuleVersion}",
                    this);
                return;
            }

            snapshot.ScoreRuleVersion = scoreRuleVersion;
            snapshot.Score            = provider.CalculateScore(snapshot);
            if (outcome != RunOutcome.Victory)
            {
                snapshot.EvaluationGrade   = RunEvaluationGrade.None;
                snapshot.EvaluationMessage = GetIncompleteRunMessage(outcome);
                return;
            }

            RunEvaluationGrade grade = provider.CalculateGrade(snapshot.Score);
            if ((!Enum.IsDefined(typeof(RunEvaluationGrade), grade)) ||
                (grade == RunEvaluationGrade.None))
            {
                snapshot.EvaluationGrade   = RunEvaluationGrade.None;
                snapshot.EvaluationMessage = SCORE_CONFIGURATION_ERROR_MESSAGE;
                Debug.LogError(
                    $"[RunManager] 승리 점수 등급이 유효하지 않다: run={RunId}, " +
                    $"version={scoreRuleVersion}, grade={grade}", this);
                return;
            }

            snapshot.EvaluationGrade   = grade;
            snapshot.EvaluationMessage = provider.GetEvaluationMessage(grade);
        }

        private static string GetIncompleteRunMessage(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.TimeExpired:
                    return "제한 시간이 끝나 이번 런을 마치지 못했다.";
                case RunOutcome.PartyWipe:
                    return "모든 참가자가 쓰러져 이번 런을 마치지 못했다.";
                case RunOutcome.PlayerLeft:
                    return "참가자 이탈로 이번 런을 마치지 못했다.";
                default:
                    return "이번 런은 완료되지 않았다.";
            }
        }

        private static ulong CreateSessionId()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong value = BitConverter.ToUInt64(bytes, 0);
            return (value == 0UL) ? 1UL : value;
        }

        /// <summary> 다음 판을 열 수 있도록 런이 소유한 것들을 되돌린다 </summary>
        [Server]
        private void ServerResetRunOwnedState()
        {
            uint finishedRunIdValue = _runIdValue;
            MarkRegistry.ClearAll();
            CombatClock.ClearAll();

            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
            _bossEntranceState                = BossEntranceState.Idle;
            _bossEntranceDeadlineFieldSeconds = 0f;
            _candidateOutcome       = RunOutcome.None;
            _pauseReasons           = RunPauseReason.None;
            _pauseStartServerTime   = 0.0;

            IRunResultFactsProvider factsProvider = RunResultFactsProviderLocator.Current;
            if (factsProvider != null)
            {
                factsProvider.ResetRun(new RunId(finishedRunIdValue));
            }

            RunParticipantRegistry.ServerClear();

            if (!GameEventBus.HasInstance)
            {
                Debug.LogError("[RunManager] GameEventBus가 없어 런 초기화를 알리지 못했다, 다음 판에 이전 개체가 남는다", this);
                return;
            }

            GameEventBus.Instance.Publish(new RunResetEvent(finishedRunIdValue));
        }

        private void StartReturnToLobby()
        {
            StopReturnToLobby();
            _returnRoutine = StartCoroutine(CoReturnToLobby());
        }

        [Server]
        private bool ServerTryStartReturnToLobbyCountdown()
        {
            if ((_phase != RunPhase.Result) || (_outcome == RunOutcome.None) ||
                (_returnRoutine != null))
            {
                return false;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            bool isDevelopmentScene = ((director == null) ||
                                       (director.CurrentScene == RunSceneId.None));
            bool isResultReady = ((director != null) &&
                                  (director.State == RunTransitionState.Idle) &&
                                  (director.CurrentScene == RunSceneId.Result));
            if ((!isDevelopmentScene) && (!isResultReady))
            {
                return false;
            }

            StartReturnToLobby();
            return true;
        }

        private void StopReturnToLobby()
        {
            if (_returnRoutine == null)
            {
                return;
            }

            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

        private IEnumerator CoReturnToLobby()
        {
            yield return new WaitForSeconds(Constants.Network.RESULT_TO_LOBBY_SECONDS);

            _returnRoutine = null;
            ServerReturnToLobby();
        }

        private void StartTick()
        {
            StopTick();
            _tickRoutine = StartCoroutine(CoServerTick());
        }

        private void StopTick()
        {
            if (_tickRoutine == null)
            {
                return;
            }

            StopCoroutine(_tickRoutine);
            _tickRoutine = null;
        }

        private IEnumerator CoServerTick()
        {
            float fieldTimeLimitSeconds = GameDB.Barrier.FieldTimeLimitSeconds;
            float bossTimeLimitSeconds  = GameDB.Boss.BossTimeLimitSeconds;

            while (_phase != RunPhase.Result)
            {
                if (_phase == RunPhase.Boss)
                {
                    if (ServerTickBossClock(bossTimeLimitSeconds))
                    {
                        break;
                    }

                    yield return _tickWait;
                    continue;
                }

                ServerTickFieldClock(fieldTimeLimitSeconds);

                yield return _tickWait;
            }

            _tickRoutine = null;
        }

        [Server]
        private void ServerTickFieldClock(float fieldTimeLimitSeconds)
        {
            float elapsedSeconds = (float) (NetworkTime.time - _runStartServerTime);
            bool  isFieldOver    = (elapsedSeconds >= fieldTimeLimitSeconds);
            if (isFieldOver)
            {
                elapsedSeconds = fieldTimeLimitSeconds;
            }

            AdvancePhases(elapsedSeconds);

            _previousElapsedSeconds = elapsedSeconds;
            _elapsedSeconds         = elapsedSeconds;

            if (_bossEntranceState == BossEntranceState.Idle)
            {
                float automaticStart = BossEntranceRules.GetAutomaticStart(
                    GameDB.Barrier.BossEntryCountdownSeconds, fieldTimeLimitSeconds);
                if (elapsedSeconds >= automaticStart)
                {
                    ServerStartBossEntranceCountdown(elapsedSeconds, fieldTimeLimitSeconds,
                        GameDB.Barrier.BossEntryCountdownSeconds, "필드 자동 개시");
                }
            }

            if (BossEntranceRules.HasExpired(
                    _bossEntranceState, elapsedSeconds, _bossEntranceDeadlineFieldSeconds))
            {
                _bossEntranceState = BossEntranceState.TransitionRequested;
            }

            ServerProgressBossEntranceRoute();
        }

        [Server]
        private bool ServerTickBossClock(float bossTimeLimitSeconds)
        {
            if (!_hasBossClockStarted)
            {
                // 아레나로 옮기고 참조를 다시 거는 동안에는 제한 시간이 흐르지 않는다.
                // 전환이 없는 경로(개발 진입 등)에서는 Idle이라 바로 시작한다
                if (RunSceneDirector.IsServerTransitioning)
                {
                    return false;
                }

                ServerStartBossClock();
            }

            float bossElapsedSeconds = (float) (NetworkTime.time - _bossStartServerTime);
            bool  isTimeUp           = (bossElapsedSeconds >= bossTimeLimitSeconds);
            if (isTimeUp)
            {
                bossElapsedSeconds = bossTimeLimitSeconds;
            }

            _bossElapsedSeconds = bossElapsedSeconds;

            // 보스 사망은 HandleEnemyDied가 Victory로 받는다. 여기서는 시간 초과만 본다
            if (!isTimeUp)
            {
                return false;
            }

            TryFinishRun(RunOutcome.TimeExpired);
            return true;
        }

        private void AdvancePhases(float elapsedSeconds)
        {
            int enteredCount = _timeline.CollectEnteredPhases(_previousElapsedSeconds, elapsedSeconds, _enteredBuffer);
            for (int i = 0; i < enteredCount; ++i)
            {
                CommitPhase(_enteredBuffer[i]);
            }
        }

        private void CommitPhase(RunPhase next)
        {
            if (next <= _phase)
            {
                return;
            }

            SetPhase(next);
        }

        private void SetPhase(RunPhase next)
        {
            if (next == _phase)
            {
                return;
            }

            RunPhase previous = _phase;
            _phase = next;

            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Publish(new RunPhaseChangedEvent(previous, next));
            }
            else
            {
                Debug.LogError($"[RunManager] GameEventBus가 없어 {previous} -> {next} 전환을 알리지 못했다", this);
            }

            // 구독자가 현재 씬에서 phase 변화를 처리한 뒤에 씬을 옮긴다.
            // 순서를 뒤집으면 전환으로 파괴되는 객체가 이벤트를 못 받는다
            ServerSyncSceneToPhase(next);
        }

        /// <summary>
        /// RunSceneDirector가 전원 도착, 바인딩, 참가자 복구를 마친 뒤 호출한다.
        /// 전실 다음 아레나를 열거나, 전환 중 확정된 Result 목적지를 다시 요청한다
        /// </summary>
        [Server]
        public void ServerHandleSceneTransitionReady()
        {
            if (_phase == RunPhase.Lobby)
            {
                LobbyController lobby = LobbyController.ServerInstance;
                if (lobby != null)
                {
                    lobby.ServerHandleLobbyIdle();
                }

                if (_phase == RunPhase.Lobby)
                {
                    ServerSyncSceneToPhase(_phase);
                }

                return;
            }

            if (_phase == RunPhase.Result)
            {
                if (!ServerTryStartReturnToLobbyCountdown())
                {
                    ServerSyncSceneToPhase(RunPhase.Result);
                }
                return;
            }

            if ((_bossEntranceState == BossEntranceState.TransitionRequested) ||
                (_bossEntranceState == BossEntranceState.Entering))
            {
                ServerProgressBossEntranceRoute();

                if ((_bossEntranceState == BossEntranceState.Entering) ||
                    (_bossEntranceState == BossEntranceState.Ready))
                {
                    return;
                }
            }

            ServerSyncSceneToPhase(_phase);
        }

        /// <summary>
        /// phase가 요구하는 씬으로 1회 전환한다. 씬 전환의 결정은 런 상태를 소유한 여기가 하고
        /// 실행은 RunSceneDirector가 한다.
        ///
        /// 개발 씬에서는 아무것도 하지 않는다 - Director가 시작 씬 이름으로 CurrentScene을 잡는데
        /// Dev_Combat 같은 씬은 카탈로그에 없어 None이 된다. 그 상태에서 자동 전환을 걸면
        /// 개발용 F5 경로가 매번 Field로 끌려간다
        /// </summary>
        [Server]
        private void ServerSyncSceneToPhase(RunPhase next)
        {
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if (director == null)
            {
                return;
            }

            if (director.CurrentScene == RunSceneId.None)
            {
                return;
            }

            if (!RunSceneCatalog.TryGetSceneForPhase(next, out RunSceneId target))
            {
                return;
            }

            // 결계 축소는 같은 Field 안에서 일어난다, 씬을 다시 열지 않는다
            if (director.CurrentScene == target)
            {
                return;
            }

            if (!director.ServerBeginTransition(target))
            {
                Debug.LogWarning($"[RunManager] {next} 전환에 필요한 씬 이동을 시작하지 못했다: {director.CurrentScene} -> {target}", this);
            }
        }

        [Server]
        private bool ServerStartBossEntranceCountdown(float currentFieldSeconds, float fieldLimitSeconds,
            float countdownSeconds, string reason)
        {
            if (!BossEntranceRules.TryGetDeadline(_bossEntranceState, currentFieldSeconds,
                    countdownSeconds, fieldLimitSeconds, out float deadlineFieldSeconds))
            {
                return false;
            }

            _bossEntranceDeadlineFieldSeconds = deadlineFieldSeconds;
            _bossEntranceState                = BossEntranceState.CountingDown;

            Debug.Log($"[RunManager] 보스 입구 카운트다운 시작: {reason}, " +
                      $"현재={currentFieldSeconds:F1}초, 만료={deadlineFieldSeconds:F1}초");
            return true;
        }

        [Server]
        private void ServerProgressBossEntranceRoute()
        {
            if ((_bossEntranceState != BossEntranceState.TransitionRequested) &&
                (_bossEntranceState != BossEntranceState.Entering))
            {
                return;
            }

            if (IsPaused)
            {
                return;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((director == null) || (director.CurrentScene == RunSceneId.None))
            {
                // 카탈로그 밖 개발 씬은 기존처럼 같은 씬에서 보스 구간을 시험할 수 있게 한다.
                _bossEntranceState = BossEntranceState.Ready;
                ServerBeginBossPhase();
                return;
            }

            if (RunSceneDirector.IsServerTransitioning)
            {
                return;
            }

            if (director.CurrentScene == RunSceneId.Boss)
            {
                _bossEntranceState = BossEntranceState.Ready;
                ServerBeginBossPhase();
                return;
            }

            if (!RunSceneCatalog.TryGetSceneForBossEntrance(
                    _bossEntranceState, director.CurrentScene, out RunSceneId target))
            {
                return;
            }

            if (director.ServerBeginTransition(target))
            {
                _bossEntranceState = BossEntranceState.Entering;
            }
        }

        [Server]
        private bool ServerTryAbortRun(string reason)
        {
            bool submitted = TryFinishRun(RunOutcome.PlayerLeft);
            if ((!submitted) && ((!IsPaused) || (_candidateOutcome == RunOutcome.None)))
            {
                return false;
            }

            // 참가자 이탈로 scheduler와 런 틱이 멈춘 상태에서는 다음 control tick이 없다.
            // 이 구간에는 전투 판정도 멈춰 있으므로 기존의 더 높은 후보까지 여기서 확정한다.
            if ((IsPaused) && (_candidateOutcome != RunOutcome.None))
            {
                ServerCommitOutcome(_candidateOutcome);
            }

            Debug.Log($"[RunManager] 런 중단: {reason}");
            return true;
        }
    }
}
