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
        // 런마다 새로 발급한다. 이전 판의 늦은 요청과 이벤트를 이 값으로 거른다
        [SyncVar] private uint    _runIdValue = 0u;
        [SyncVar] private RunMode _mode       = RunMode.Standard;

        // 확정된 결과. 씬이 파괴되기 전에 떠 두고 정산 화면과 기록은 이것만 읽는다
        [SyncVar] private RunResultSnapshot _result = default;

        [SyncVar] private float      _elapsedSeconds     = 0f;
        [SyncVar] private float      _bossElapsedSeconds = 0f;
        [SyncVar] private RunOutcome _outcome            = RunOutcome.None;

        // 훅은 서버 판정이 아니라 각 클라이언트의 로컬 표현 전용이다
        [SyncVar(hook = nameof(OnPhaseChangedLocal))] private RunPhase _phase = RunPhase.Lobby;

        // 일시정지는 phase가 아니라 별도 상태다. 보스전에서 멈췄다 보스전으로 돌아오므로
        // RunPhase에 값을 더하면 전진 전용 가드와 타임라인 순서 의미가 깨진다.
        //
        // 사유가 여럿 겹칠 수 있어 플래그다. 참 거짓 하나로 두면 이탈자가 돌아왔다는 이유로
        // 아직 로드 중인 씬 전환까지 함께 재개된다
        [SyncVar(hook = nameof(OnPauseReasonsChangedLocal))] private RunPauseReason _pauseReasons = RunPauseReason.None;

        private RunPhaseTimeline _timeline               = null;  // 서버 전용
        private double           _runStartServerTime     = 0.0;   // 서버 전용, NetworkTime.time 기준
        private float            _previousElapsedSeconds = 0f;    // 서버 전용, 이번 틱에 지나간 경계를 가려낸다
        private Coroutine        _tickRoutine            = null;
        private Coroutine        _returnRoutine          = null;   // 서버 전용, Result -> Lobby 복귀 대기
        private double           _pauseStartServerTime   = 0.0;    // 서버 전용, 멈춘 시각. 재개할 때 시계를 이만큼 민다
        private double           _bossStartServerTime    = 0.0;    // 서버 전용, 보스 시계의 기준 시각
        private bool             _hasBossClockStarted    = false;  // 서버 전용, 보스 시계가 도는 중인지

        // 종료 후보. 같은 틱에 들어온 것들을 모아 두고 다음 틱 시작에서 하나만 확정한다
        private RunOutcome _candidateOutcome = RunOutcome.None; // 서버 전용
        private ulong      _candidateTickId  = 0UL;             // 서버 전용, 후보가 들어온 틱

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

        // 결계, 스폰, 보스, UI가 이 클래스를 직접 참조하지 않고 시간을 읽는 창구다.
        // 값은 SyncVar라 클라이언트에서도 같은 답이 나온다
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

        // 스케줄러가 없는 Dev 씬에서도 후보 등록이 동작해야 한다. 그때는 번호가 늘 0이라
        // 다음 틱 조건이 성립하지 않으므로, 스케줄러가 없으면 후보를 곧바로 확정한다
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

        // 사유가 하나에서 둘로 늘어나는 것은 화면에 달라질 것이 없다.
        // 멈춤과 재개의 전이에서만 알린다
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
            LocalInstance  = this; // 호스트가 아닌 전용 서버에서는 OnStartClient가 오지 않는다

            // 같은 씬에서 StopHost->StartHost로 재시작해도 이전 런의 phase, 시각이 남지 않게 한다
            ServerResetState();

            // 부트 게이트를 통과했으면 반드시 초기화되어 있다, 아니면 타임라인이 비어 StartRun이 막힌다
            _timeline = GameDB.IsInitialized ? RunTimelineBuilder.Build(GameDB.Barrier) : RunTimelineBuilder.Build(null);

            // 종료 확정은 정지 게이트를 지나지 않는 제어 콜백이다.
            // 시뮬레이션과 함께 멈추면 대기를 끊어도 런이 끝나지 않는다
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

        /// <summary>
        /// 런을 시작한다. LobbyController가 3명 준비와 역할 배분을 마친 뒤에만 호출한다.
        /// 실패하면 false를 돌려주며, 이때 호출부는 반드시 되돌려야 한다 -
        /// 입장 잠금과 역할 배분이 커밋된 채 런이 열리지 않으면 세션이 영구히 잠긴다
        /// </summary>
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

            // 이전 판의 늦은 요청이 새 판에 섞이지 않도록 식별자를 먼저 새로 발급한다
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

        /// <summary>
        /// 종료 사유를 후보로 등록한다. 여기서 곧바로 끝내지 않는다.
        ///
        /// 보스 사망, 전멸, 기한 만료는 같은 물리 스텝 안에서 겹칠 수 있고, 그때 먼저 도착한 콜백이
        /// 결과를 정하면 같은 상황에서 사람마다 다른 결말을 본다. 후보를 모아 두고 다음 틱 시작에
        /// RunOutcomeArbiter가 하나만 고른다.
        ///
        /// 돌려주는 값은 "이 후보가 채택됐다"이지 "런이 끝났다"가 아니다
        /// </summary>
        [Server]
        public bool TryFinishRun(RunOutcome outcome)
        {
            // 로비에서의 이탈은 끝낼 런이 없다. 여기서 막지 않으면 시작도 안 한 런이 Result로 확정된다
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

            // 스케줄러가 없으면 제어 틱도 없어 후보가 영원히 남는다.
            // 부트 게이트가 스케줄러를 요구하므로 정상 경로에서는 오지 않는 자리다
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                ServerCommitOutcome(outcome);
            }

            return true;
        }

        /// <summary>
        /// 이탈한 사람을 기다리며 런을 얼린다. Result로 확정하지 않으므로 phase와 경과 시간이 보존된다.
        /// 보스전 도중에 멈췄다면 재개도 보스전에서 이어진다
        /// </summary>
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

        /// <summary>
        /// 정지 사유를 하나 건다. 이미 걸려 있던 사유면 아무것도 하지 않는다.
        /// 처음 멈추는 순간에만 시계와 시뮬레이션을 세운다
        /// </summary>
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

        /// <summary>
        /// 정지 사유를 하나 푼다. 남은 사유가 있으면 여전히 멈춘 채다 -
        /// 한 명이 돌아왔다고 아직 로드 중인 씬 전환까지 재개하면 안 된다
        /// </summary>
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

            // 시계는 NetworkTime.time에서 역산하므로, 시작 시각을 멈춘 만큼 미뤄야 경과 시간이 그대로다.
            // 두 시계를 함께 밀지 않으면 보스전에서 멈췄다 돌아왔을 때 그만큼 제한이 줄어든다
            double pausedSeconds = NetworkTime.time - _pauseStartServerTime;

            if (_hasBossClockStarted)
            {
                _bossStartServerTime += pausedSeconds;
            }

            _runStartServerTime  += pausedSeconds;
            _pauseStartServerTime = 0.0;

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

            ServerResetRunOwnedState();

            // 결과 확정이 껐던 시뮬레이션을 여기서 되돌린다. 켜는 곳이 StartRun과 ResumeRun뿐이면
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

        // 후보가 들어온 틱이 닫힌 뒤에 한 번만 확정한다. 같은 틱에 더 높은 사유가 들어오면 그것이 이긴다.
        // 스케줄러가 없는 Dev 경로에서는 번호가 늘 0이라 다음 틱을 기다리지 못하므로 바로 확정한다
        [Server]
        private void ServerResolveOutcome()
        {
            if (_candidateOutcome == RunOutcome.None)
            {
                return;
            }

            if ((CurrentTickId != 0UL) && (CurrentTickId <= _candidateTickId))
            {
                return; // 아직 이 틱이 닫히지 않았다, 남은 판정이 더 들어올 수 있다
            }

            ServerCommitOutcome(_candidateOutcome);
        }

        /// <summary>
        /// 결과를 확정하고 Result로 넘긴다. 확정은 런당 한 번뿐이고, 이후 도착한 후보는 버린다.
        /// 이미 보낸 결과와 씬 전환은 되돌릴 수 없으므로 덮어쓰지 않는다
        /// </summary>
        [Server]
        private void ServerCommitOutcome(RunOutcome outcome)
        {
            _candidateOutcome = RunOutcome.None;

            if ((_phase == RunPhase.Lobby) || (_phase == RunPhase.Result) || (outcome == RunOutcome.None))
            {
                return;
            }

            _outcome      = outcome;
            _pauseReasons = RunPauseReason.None; // 얼어 있던 채로 끝날 수 있다, 남겨 두면 다음 런까지 따라간다

            // 씬과 개체가 사라지기 전에 결과를 떠 둔다. 정산 화면이 살아 있는 플레이어와
            // 잡귀 목록을 다시 훑으면 그때는 이미 읽을 것이 없다
            _result = BuildResultSnapshot(outcome);

            StopTick();
            CommitPhase(RunPhase.Result);

            // 등록은 그대로 두고 이동, AI, 자원 회복 tick만 멈춘다. 새 런 시작 때만 다시 켠다
            SetSimulationEnabled(false);

            StartReturnToLobby();

            Debug.Log($"[RunManager] 런을 끝냈다: {outcome}, 필드={_elapsedSeconds:F1}초, 보스={_bossElapsedSeconds:F1}초");
        }

        // 지금 이 클래스가 아는 사실만 담는다. 처치 수, 사망 수, 잔액, 남은 HP는
        // 각각의 소유자가 생기면 그쪽이 채운다 - 없는 값을 지어내면 점수가 조용히 틀린다
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

        /// <summary>
        /// 다음 판을 열 수 있도록 런이 소유한 것들을 되돌린다.
        ///
        /// 초기화를 이벤트에만 맡기지 않는다 - 발행했다는 사실과 각자 되돌렸다는 사실은 다르다.
        /// 여기가 직접 되돌릴 수 있는 것은 직접 되돌리고, 씬에 사는 것들에게만 통지한다
        /// </summary>
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

            // 맡아 둔 참가자 상태는 이 런의 것이다. 다음 판까지 들고 있으면 이전 판 값을 붓게 된다
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
