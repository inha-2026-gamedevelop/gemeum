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
    /// <summary>
    /// 런 시계와 상태 머신. 경과 시간과 RunPhase의 원본은 여기 하나뿐이다.
    /// 각 파트는 시간을 다시 계산하지 않고 RunPhaseChangedEvent만 본다
    /// </summary>
    public class RunManager : NetworkBehaviour, IRunTimeSource
    {
        /****************************************
        *                Fields
        ****************************************/

        // 시계는 둘이고 서로 독립이다. 필드는 0~30분 상한, 보스는 진입 순간부터 14분이다.
        // 값을 쓰는 곳은 여기 하나뿐이고 보스전은 시작 시점만 요청한다
        [SyncVar] private float      _elapsedSeconds     = 0f;
        [SyncVar] private float      _bossElapsedSeconds = 0f;
        [SyncVar] private RunOutcome _outcome            = RunOutcome.None;

        // 훅은 서버 판정이 아니라 각 클라이언트의 로컬 표현 전용이다
        [SyncVar(hook = nameof(OnPhaseChangedLocal))] private RunPhase _phase = RunPhase.Lobby;

        // 일시정지는 phase가 아니라 별도 상태다. 보스전에서 멈췄다 보스전으로 돌아오므로
        // RunPhase에 값을 더하면 전진 전용 가드와 타임라인 순서 의미가 깨진다
        [SyncVar(hook = nameof(OnPausedChangedLocal))] private bool _isPaused = false;

        private RunPhaseTimeline _timeline               = null;  // 서버 전용
        private double           _runStartServerTime     = 0.0;   // 서버 전용, NetworkTime.time 기준
        private float            _previousElapsedSeconds = 0f;    // 서버 전용, 이번 틱에 지나간 경계를 가려낸다
        private Coroutine        _tickRoutine            = null;
        private Coroutine        _returnRoutine          = null;   // 서버 전용, Result -> Lobby 복귀 대기
        private double           _pauseStartServerTime   = 0.0;    // 서버 전용, 멈춘 시각. 재개할 때 시계를 이만큼 민다
        private double           _bossStartServerTime    = 0.0;    // 서버 전용, 보스 시계의 기준 시각
        private bool             _hasBossClockStarted    = false;  // 서버 전용, 보스 시계가 도는 중인지

        private readonly List<RunPhase> _enteredBuffer = new List<RunPhase>();
        private WaitForSeconds          _tickWait      = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static RunManager ServerInstance { get; private set; }

        /// <summary> 서버와 클라이언트 양쪽에서 읽는 인스턴스. 판정이 아니라 표현과 예측 차단에만 쓴다 </summary>
        public static RunManager LocalInstance { get; private set; }

        /// <summary> 이탈자를 기다리며 세계가 얼어 있다. 시계도 멈춘다 </summary>
        public bool IsPaused => _isPaused;

        /// <summary> 필드 구간의 경과 시간(초). 보스전에서는 더 이상 늘지 않는다 </summary>
        public float ElapsedSeconds => _elapsedSeconds;

        /// <summary> 보스전 경과 시간(초). 보스 시계가 시작하기 전에는 0이다 </summary>
        public float BossElapsedSeconds => _bossElapsedSeconds;

        /// <summary> 점수가 쓰는 기준 시간. 두 시계의 합이고 전실, 컷신, 이탈 대기는 들어가지 않는다 </summary>
        public float ScoreElapsedSeconds => (_elapsedSeconds + _bossElapsedSeconds);

        /****************************************
        *           Interface Members
        ****************************************/

        // 결계, 스폰, 보스, UI가 이 클래스를 직접 참조하지 않고 시간을 읽는 창구다.
        // 값은 SyncVar라 클라이언트에서도 같은 답이 나온다
        double IRunTimeSource.FieldElapsedSeconds    => _elapsedSeconds;
        double IRunTimeSource.BossElapsedSeconds     => _bossElapsedSeconds;
        double IRunTimeSource.GameplayElapsedSeconds => ScoreElapsedSeconds;

        /// <summary>
        /// 지금 걸려 있는 정지 사유.
        ///
        /// 아직 사유가 하나뿐이라 bool 하나에서 옮겨 적는다. 사유를 여럿 관리하는 것은
        /// 씬 전환과 보스 연출이 들어오는 시점의 일이고, 그때 이 프로퍼티의 안쪽만 바뀐다
        /// </summary>
        public RunPauseReason PauseReasons => (_isPaused ? RunPauseReason.ParticipantMissing : RunPauseReason.None);

        public RunPhase   Phase   => _phase;
        public RunOutcome Outcome => _outcome;

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

        // 로케이터 등록을 서버 콜백에 두면 클라이언트에는 구현이 없어 UI가 시간을 읽지 못한다.
        // 결계가 같은 이유로 OnEnable에서 등록한다
        private void OnEnable()  => RunTimeLocator.Register(this);
        private void OnDisable() => RunTimeLocator.Unregister(this);

        private void Awake()
        {
            // 남은 시간 표시는 클라가 보간하므로 매 틱 보낼 이유가 없다
            syncInterval = Constants.Network.RUN_TIME_SYNC_INTERVAL;
            _tickWait    = new WaitForSeconds(Constants.Network.SERVER_TICK_INTERVAL);
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        // 소리는 각 클라이언트가 자기 화면에서 내는 로컬 표현이라 여기서 전파하지 않는다
        private void OnPhaseChangedLocal(RunPhase oldPhase, RunPhase newPhase)
        {
            RunSoundDirector.OnPhaseChanged(newPhase);
        }

        private void OnPausedChangedLocal(bool oldPaused, bool newPaused)
        {
            RunSoundDirector.OnPausedChanged(newPaused);
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
            LocalInstance  = this; // 호스트가 아닌 전용 서버에서는 OnStartClient가 오지 않는다

            // 같은 씬에서 StopHost->StartHost로 재시작해도 이전 런의 phase, 시각이 남지 않게 한다
            ServerResetState();

            // 부트 게이트를 통과했으면 반드시 초기화되어 있다, 아니면 타임라인이 비어 StartRun이 막힌다
            _timeline = GameDB.IsInitialized ? RunTimelineBuilder.Build(GameDB.Barrier) : RunTimelineBuilder.Build(null);
        }

        public override void OnStopServer()
        {
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

        /// <summary>
        /// 런을 시작한다. LobbyController가 3명 준비와 역할 배분을 마친 뒤에만 호출한다.
        /// 실패하면 false를 돌려주며, 이때 호출부는 반드시 되돌려야 한다 -
        /// 입장 잠금과 역할 배분이 커밋된 채 런이 열리지 않으면 세션이 영구히 잠긴다
        /// </summary>
        [Server]
        public bool StartRun()
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

            _runStartServerTime     = NetworkTime.time;
            _previousElapsedSeconds = 0f;
            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
            _outcome                = RunOutcome.None;

            SetSimulationEnabled(true);

            CommitPhase(firstPhase);
            StartTick();
            return true;
        }

        /// <summary>
        /// 시간 초과, 이탈, 전멸, 보스 처치 중 어느 경로가 먼저 와도 Result로 한 번만 확정하는
        /// 단일 종료 경로다. 첫 호출만 outcome을 커밋하고 이후 호출은 값을 바꾸지 않는다
        /// </summary>
        [Server]
        public bool TryFinishRun(RunOutcome outcome)
        {
            // 로비에서의 이탈은 끝낼 런이 없다. 여기서 막지 않으면 시작도 안 한 런이 Result로 확정된다
            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (outcome == RunOutcome.None))
            {
                return false;
            }

            _outcome  = outcome;
            _isPaused = false; // 얼어 있던 채로 끝날 수 있다, 남겨 두면 다음 런까지 따라간다

            StopTick();
            CommitPhase(RunPhase.Result);

            // 등록은 그대로 두고 이동, AI, 자원 회복 tick만 멈춘다. 새 런 시작 때만 다시 켠다
            SetSimulationEnabled(false);

            StartReturnToLobby();
            return true;
        }

        /// <summary>
        /// 이탈한 사람을 기다리며 런을 얼린다. Result로 확정하지 않으므로 phase와 경과 시간이 보존된다.
        /// 보스전 도중에 멈췄다면 재개도 보스전에서 이어진다
        /// </summary>
        [Server]
        public bool PauseRun(string reason)
        {
            if ((_isPaused) || (_phase == RunPhase.Lobby) || (_phase == RunPhase.Result))
            {
                return false;
            }

            _isPaused             = true;
            _pauseStartServerTime = NetworkTime.time;

            StopTick();
            SetSimulationEnabled(false);

            Debug.Log($"[RunManager] 런을 일시정지했다: {reason}, phase={_phase}, 경과={_elapsedSeconds:F1}초");
            return true;
        }

        /// <summary> 기다리던 사람이 돌아와 런을 재개한다. 멈춰 있던 시간만큼 시계를 밀어 경과 시간을 보존한다 </summary>
        [Server]
        public bool ResumeRun()
        {
            if (!_isPaused)
            {
                return false;
            }

            // 시계는 NetworkTime.time에서 역산하므로, 시작 시각을 멈춘 만큼 미뤄야 경과 시간이 그대로다.
            // 두 시계를 함께 밀지 않으면 보스전에서 멈췄다 돌아왔을 때 그만큼 제한이 줄어든다
            double pausedSeconds = NetworkTime.time - _pauseStartServerTime;

            if (_hasBossClockStarted)
            {
                _bossStartServerTime += pausedSeconds;
            }

            _runStartServerTime  += pausedSeconds;
            _pauseStartServerTime = 0.0;
            _isPaused             = false;

            SetSimulationEnabled(true);
            StartTick();

            // 런 시계 말고도 절대 시각으로 잡아 둔 타이머가 있다. 그쪽은 여기서 직접 만지지 않고
            // 밀린 양만 알린다 - 관전 30초처럼 얼마나 남았는지가 중요한 값은 각자 소유자가 민다
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

        /// <summary>
        /// 보스 구간을 열고 보스 시계를 시작한다.
        ///
        /// 지금은 필드 상한에 닿았을 때 여기서 바로 넘어간다. 씬 전환 소유자가 생기면
        /// 전실을 지나 마당에 진입한 것을 확인한 뒤 그쪽이 이 메서드를 부른다 -
        /// 로딩과 전실 대기가 보스 14분에 들어가면 안 되기 때문이다
        /// </summary>
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
            _isPaused               = false;
            _pauseStartServerTime   = 0.0;
            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;
        }

        /// <summary>
        /// Result로 끝난 런을 로비 상태로 되돌린다. 입장 잠금을 풀고 로비 슬롯의 준비 상태를 비워
        /// 서버를 껐다 켜지 않고도 다음 런을 시작할 수 있게 한다. _outcome은 지우지 않는다,
        /// 다음 StartRun이 None으로 되돌리므로 그때까지는 직전 런의 결과를 읽을 수 있다
        /// </summary>
        [Server]
        public void ServerReturnToLobby()
        {
            if (_phase != RunPhase.Result)
            {
                return;
            }

            StopReturnToLobby();

            _elapsedSeconds         = 0f;
            _bossElapsedSeconds     = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
            _bossStartServerTime    = 0.0;
            _hasBossClockStarted    = false;

            // TryFinishRun이 껐던 시뮬레이션을 여기서 되돌린다. 켜는 곳이 StartRun과 ResumeRun뿐이면
            // 로비로 돌아온 순간부터 다음 런이 시작될 때까지 이동, 스태미나 회복이 서버에서 죽어 있다
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

        // 스케줄러는 씬에 없을 수도 있고 종료 중에 파괴되기도 한다. Unity 오브젝트라 == null로 봐야
        // 파괴된 뒤를 걸러낸다
        private static void SetSimulationEnabled(bool isEnabled)
        {
            AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
            if (scheduler == null)
            {
                return;
            }

            scheduler.SetSimulationEnabled(isEnabled);
        }

        // 보스 시계를 연다. 두 번 부르면 이미 흐른 시간이 0으로 돌아가므로 첫 호출만 받는다
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

        // Result를 같은 프레임에 Lobby로 덮으면 SyncVar가 마지막 값만 보내 클라이언트가 Result를 못 본다
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

        // 경과 시간을 누적하지 않고 서버 시각에서 매번 환산한다, 틱이 밀려도 시계가 어긋나지 않는다.
        // 필드 상한에 닿으면 실패가 아니라 보스 구간으로 넘어가고, 그 뒤로는 보스 시계를 센다
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

        // 필드 시계. 상한에 닿으면 실패가 아니라 보스 구간으로 넘긴다
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

        // 보스 시계. 상한에 닿으면 실패로 끝낸다, 끝냈으면 true
        [Server]
        private bool ServerTickBossClock(float bossTimeLimitSeconds)
        {
            // 보스 구간인데 시계가 아직 없으면 여기서 연다.
            // 씬 전환이 붙기 전의 Dev 경로와 서버 재시작 bootstrap이 이 자리를 지난다
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

        // 틱 한 번에 경계를 여러 개 지나갔으면 정의된 순서로 각각 한 번씩 전환한다
        private void AdvancePhases(float elapsedSeconds)
        {
            int enteredCount = _timeline.CollectEnteredPhases(_previousElapsedSeconds, elapsedSeconds, _enteredBuffer);
            for (int i = 0; i < enteredCount; ++i)
            {
                CommitPhase(_enteredBuffer[i]);
            }
        }

        // 런 진행 중의 전진 전환이다. 시계로 지나간 경계만 커밋하므로 뒤로는 가지 않는다
        private void CommitPhase(RunPhase next)
        {
            if (next <= _phase)
            {
                return;
            }

            SetPhase(next);
        }

        // 상태를 먼저 커밋한 뒤 서버 로컬 이벤트를 한 번만 발행한다.
        // Result -> Lobby 복귀는 유일한 역방향 전환이라 CommitPhase의 전진 조건을 거치지 않는다
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
