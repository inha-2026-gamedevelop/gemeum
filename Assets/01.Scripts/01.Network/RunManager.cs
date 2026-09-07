// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary> 런 시계와 상태 머신 </summary>
    public class RunManager : NetworkBehaviour, IRunTimeSource
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private uint    _runIdValue = 0u;
        [SyncVar] private RunMode _mode       = RunMode.Standard;

        [SyncVar] private RunResultSnapshot _result = default;

        [SyncVar] private float      _elapsedSeconds     = 0f;
        [SyncVar] private float      _bossElapsedSeconds = 0f;
        [SyncVar] private RunOutcome _outcome            = RunOutcome.None;

        [SyncVar(hook = nameof(OnPhaseChangedLocal))] private RunPhase _phase = RunPhase.Lobby;

        [SyncVar(hook = nameof(OnPauseReasonsChangedLocal))] private RunPauseReason _pauseReasons = RunPauseReason.None;

        private RunPhaseTimeline _timeline               = null;  // 서버 전용
        private double           _runStartServerTime     = 0.0;   // 서버 전용
        private float            _previousElapsedSeconds = 0f;    // 서버 전용
        private Coroutine        _tickRoutine            = null;
        private Coroutine        _returnRoutine          = null;   // 서버 전용
        private double           _pauseStartServerTime   = 0.0;    // 서버 전용
        private double           _bossStartServerTime    = 0.0;    // 서버 전용
        private bool             _hasBossClockStarted    = false;  // 서버 전용

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

        public RunPhase   Phase   => _phase;
        public RunOutcome Outcome => _outcome;

        /// <summary> 지금 런의 식별자. 로비에서는 직전 런의 값이 남아 있다 </summary>
        public RunId RunId => new RunId(_runIdValue);

        /// <summary> 정식 런인지 트라이인지. 리더보드 등록 여부가 여기서 갈린다 </summary>
        public RunMode Mode => _mode;

        /// <summary> 확정된 결과. 아직 끝나지 않았으면 Outcome이 None이다 </summary>
        public RunResultSnapshot Result => _result;

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

            RunSoundDirector.OnPausedChanged(isPaused);
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

            ServerResetState();

            _timeline = GameDB.IsInitialized ? RunTimelineBuilder.Build(GameDB.Barrier) : RunTimelineBuilder.Build(null);

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[RunManager] 권위 틱 스케줄러가 없어 종료 확정을 매 틱 돌릴 수 없다", this);
            }
            else
            {
                AuthorityTickScheduler.ServerInstance.RegisterControl(ServerResolveOutcome);
            }
        }

        public override void OnStopServer()
        {
            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.UnregisterControl(ServerResolveOutcome);
            }

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

            if (!_timeline.TryGetPhaseAt(0f, out RunPhase firstPhase))
            {
                Debug.LogError("[RunManager] 0초에 해당하는 구간이 없어 런을 시작할 수 없다", this);
                return false;
            }

            _runIdValue = RunId.Next().Value;
            _mode       = mode;
            _result     = default;

            _runStartServerTime     = NetworkTime.time;
            _previousElapsedSeconds = 0f;
            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
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
            if (TryFinishRun(RunOutcome.PlayerLeft))
            {
                Debug.Log($"[RunManager] 런 중단: {reason}");
            }
        }

        /// <summary> 보스 구간을 열고 보스 시계를 시작한다 </summary>
        [Server]
        public bool ServerBeginBossPhase()
        {
            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (_phase == RunPhase.Boss))
            {
                return false;
            }

            CommitPhase(RunPhase.Boss);
            ServerStartBossClock();
            return true;
        }

        /// <summary> 같은 씬에서 서버가 재시작해도 이전 런의 phase, 시각, outcome이 남지 않게 한다 </summary>
        [Server]
        public void ServerResetState()
        {
            StopTick();
            StopReturnToLobby();

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
        }

        /// <summary> Result로 끝난 런을 로비 상태로 되돌린다 </summary>
        [Server]
        public void ServerReturnToLobby()
        {
            if (_phase != RunPhase.Result)
            {
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

            _outcome      = outcome;
            _pauseReasons = RunPauseReason.None;

            _result = BuildResultSnapshot(outcome);

            StopTick();
            CommitPhase(RunPhase.Result);

            SetSimulationEnabled(false);

            StartReturnToLobby();

            Debug.Log($"[RunManager] 런을 끝냈다: {outcome}, 필드={_elapsedSeconds:F1}초, 보스={_bossElapsedSeconds:F1}초");
        }

        [Server]
        private RunResultSnapshot BuildResultSnapshot(RunOutcome outcome)
        {
            RunResultSnapshot snapshot = new RunResultSnapshot
            {
                RunIdValue          = _runIdValue,
                Mode                = _mode,
                Outcome             = outcome,
                FieldElapsedSeconds = _elapsedSeconds,
                BossElapsedSeconds  = _bossElapsedSeconds,
                BossKilled          = (outcome == RunOutcome.Victory),

                // TODO(민성): 처치 수·사망 수·잔액·남은 HP는 점수, 경제, 성장 시스템이 생기면 채운다
                EnemyKills          = 0,
                DeathCount          = 0,
                RemainingCurrency   = 0,
                RemainingHealthSum  = 0f,
            };

            IRunResultProvider provider = RunResultProviderLocator.Current;

            snapshot.ScoreRuleVersion = provider.ScoreRuleVersion;
            snapshot.Score            = provider.CalculateScore(snapshot);

            return snapshot;
        }

        /// <summary> 다음 판을 열 수 있도록 런이 소유한 것들을 되돌린다 </summary>
        [Server]
        private void ServerResetRunOwnedState()
        {
            uint finishedRunIdValue = _runIdValue;

            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
            _candidateOutcome       = RunOutcome.None;
            _pauseReasons           = RunPauseReason.None;
            _pauseStartServerTime   = 0.0;

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

            if (isFieldOver)
            {
                ServerBeginBossPhase();
            }
        }

        [Server]
        private bool ServerTickBossClock(float bossTimeLimitSeconds)
        {
            if (!_hasBossClockStarted)
            {
                ServerStartBossClock();
            }

            float bossElapsedSeconds = (float) (NetworkTime.time - _bossStartServerTime);
            bool  isTimeUp           = (bossElapsedSeconds >= bossTimeLimitSeconds);
            if (isTimeUp)
            {
                bossElapsedSeconds = bossTimeLimitSeconds;
            }

            _bossElapsedSeconds = bossElapsedSeconds;

            // TODO(민성): 보스 사망으로도 Result에 들어가야 한다, 보스 파트 연결 시 추가
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

            if (!GameEventBus.HasInstance)
            {
                Debug.LogError($"[RunManager] GameEventBus가 없어 {previous} -> {next} 전환을 알리지 못했다", this);
                return;
            }

            GameEventBus.Instance.Publish(new RunPhaseChangedEvent(previous, next));
        }
    }
}
