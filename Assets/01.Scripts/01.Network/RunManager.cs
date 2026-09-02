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
    public class RunManager : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private float      _elapsedSeconds = 0f;
        [SyncVar] private RunPhase   _phase          = RunPhase.Lobby;
        [SyncVar] private RunOutcome _outcome        = RunOutcome.None;

        private RunPhaseTimeline _timeline               = null;  // 서버 전용
        private double           _runStartServerTime     = 0.0;   // 서버 전용, NetworkTime.time 기준
        private float            _previousElapsedSeconds = 0f;    // 서버 전용, 이번 틱에 지나간 경계를 가려낸다
        private Coroutine        _tickRoutine            = null;
        private Coroutine        _returnRoutine          = null;   // 서버 전용, Result -> Lobby 복귀 대기

        private readonly List<RunPhase> _enteredBuffer = new List<RunPhase>();
        private WaitForSeconds          _tickWait      = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static RunManager ServerInstance { get; private set; }

        public float      ElapsedSeconds => _elapsedSeconds;
        public RunPhase   Phase          => _phase;
        public RunOutcome Outcome        => _outcome;

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

        private void Awake()
        {
            // 남은 시간 표시는 클라가 보간하므로 매 틱 보낼 이유가 없다
            syncInterval = Constants.Network.RUN_TIME_SYNC_INTERVAL;
            _tickWait    = new WaitForSeconds(Constants.Network.SERVER_TICK_INTERVAL);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;

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

        /// <summary> 런을 시작한다. LobbyController가 3명 준비와 역할 배분을 마친 뒤에만 호출한다 </summary>
        [Server]
        public void StartRun()
        {
            if (_phase != RunPhase.Lobby)
            {
                Debug.LogWarning($"[RunManager] Lobby가 아닌 상태에서 런 시작을 요청했다: {_phase}", this);
                return;
            }

            if (!_timeline.TryGetPhaseAt(0f, out RunPhase firstPhase))
            {
                Debug.LogError("[RunManager] 0초에 해당하는 구간이 없어 런을 시작할 수 없다", this);
                return;
            }

            _runStartServerTime     = NetworkTime.time;
            _previousElapsedSeconds = 0f;
            _elapsedSeconds         = 0f;
            _outcome                = RunOutcome.None;

            AuthorityTickScheduler.ServerInstance?.SetSimulationEnabled(true);

            CommitPhase(firstPhase);
            StartTick();
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

            _outcome = outcome;
            StopTick();
            CommitPhase(RunPhase.Result);

            // 등록은 그대로 두고 이동, AI, 자원 회복 tick만 멈춘다. 새 런 시작 때만 다시 켠다
            AuthorityTickScheduler.ServerInstance?.SetSimulationEnabled(false);

            StartReturnToLobby();
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

        /// <summary> 같은 씬에서 서버가 재시작해도 이전 런의 phase, 시각, outcome이 남지 않게 한다 </summary>
        [Server]
        public void ServerResetState()
        {
            StopTick();
            StopReturnToLobby();

            _phase                  = RunPhase.Lobby;
            _outcome                = RunOutcome.None;
            _elapsedSeconds         = 0f;
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;
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
            _previousElapsedSeconds = 0f;
            _runStartServerTime     = 0.0;

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

            LobbyController.ServerInstance?.ServerResetForNewRun();
            Debug.Log("[RunManager] 로비로 복귀했다, 입장 잠금 해제");
        }

        /****************************************
        *                Methods
        ****************************************/

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

        // 경과 시간을 누적하지 않고 서버 시각에서 매번 환산한다, 틱이 밀려도 시계가 어긋나지 않는다
        private IEnumerator CoServerTick()
        {
            float runTimeLimitSeconds = GameDB.Barrier.RunTimeLimitSeconds;

            while (_phase != RunPhase.Result)
            {
                float elapsedSeconds = (float) (NetworkTime.time - _runStartServerTime);
                bool  isTimeUp       = (elapsedSeconds >= runTimeLimitSeconds);
                if (isTimeUp)
                {
                    elapsedSeconds = runTimeLimitSeconds;
                }

                AdvancePhases(elapsedSeconds);

                _previousElapsedSeconds = elapsedSeconds;
                _elapsedSeconds         = elapsedSeconds;

                // TODO(민성): 보스 사망으로도 Result에 들어가야 한다, 보스 파트 연결 시 추가
                if (isTimeUp)
                {
                    TryFinishRun(RunOutcome.TimeExpired);
                    break;
                }

                yield return _tickWait;
            }

            _tickRoutine = null;
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
