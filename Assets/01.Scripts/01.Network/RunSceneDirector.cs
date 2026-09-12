// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 런의 씬 전환을 소유한다. 서버가 단계를 진행하고 클라이언트는 도착만 알린다 </summary>
    public class RunSceneDirector : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PAUSE_DETAIL    = "씬 전환";
        private const float  ARRIVAL_POLL    = 0.1f; // 도착 확인 주기, 초
        private const float  ARRIVAL_TIMEOUT = 30f;  // 도착 대기 상한, 초
        private const float  LOADING_TIMEOUT = 60f;  // 서버 씬 로딩 상한, 초

        public static RunSceneDirector ServerInstance { get; private set; }
        public static RunSceneDirector LocalInstance  { get; private set; }

        [SyncVar] private RunTransitionState _state          = RunTransitionState.Idle;
        [SyncVar] private RunSceneId         _currentScene   = RunSceneId.None;
        [SyncVar] private uint               _revisionValue  = 0u;
        [SyncVar] private string             _failureMessage = string.Empty;

        // 전환마다 다시 바인딩할 대상 목록
        private static readonly List<IRunSceneBinding> G_Bindings = new List<IRunSceneBinding>();

        // 트랜잭션을 시작할 때 확정한 연결. 중간에 한 명이 사라져도 성공 조건이 줄어들지 않는다
        private readonly HashSet<int> _expectedConnections = new HashSet<int>();
        private readonly HashSet<int> _arrivedConnections  = new HashSet<int>();
        private readonly HashSet<int> _faultedConnections  = new HashSet<int>();
        private readonly Dictionary<int, ParticipantId> _participants = new Dictionary<int, ParticipantId>();
        private readonly Dictionary<ParticipantId, NetworkIdentity> _restoredPlayers = new Dictionary<ParticipantId, NetworkIdentity>();

        private SceneTransitionSnapshot _transition;
        private Coroutine               _routine;
        private WaitForSeconds          _waitArrivalPoll;
        private bool                    _terminalExitArmed;
        private bool                    _isTerminalTransition;
        private bool                    _waitingForRecovery;

        /****************************************
        *              Properties
        ****************************************/

        public RunTransitionState State          => _state;
        public RunSceneId         CurrentScene   => _currentScene;
        public SceneRevision      Revision       => new SceneRevision(_revisionValue);
        public string             FailureMessage => _failureMessage;
        public RunSceneId         TransitionTarget => _transition.ToScene;
        public bool IsWaitingForRecovery => _waitingForRecovery;

        /// <summary> 지금 씬을 옮기는 중이거나 실패 뒤 개입을 기다리는가 </summary>
        public static bool IsServerTransitioning
        {
            get
            {
                if (ServerInstance == null)
                {
                    return false;
                }

                return (ServerInstance._state != RunTransitionState.Idle);
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _waitArrivalPoll = new WaitForSeconds(ARRIVAL_POLL);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnClientSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnClientSceneLoaded;
            if (_routine == null)
            {
                return;
            }

            StopCoroutine(_routine);
            _routine = null;

            // 서버가 이미 내려간 뒤의 정상 종료까지 전환 실패로 남기지 않는다
            if (NetworkServer.active && (ServerInstance == this) && (_state != RunTransitionState.Idle))
            {
                ServerFailTransition("씬 전환 중 관리자가 비활성화되었습니다");
            }
        }

        public override void OnStartServer()
        {
            ServerInstance = this;
            _currentScene  = RunSceneCatalog.FromSceneName(SceneManager.GetActiveScene().name);
        }

        public override void OnStopServer()
        {
            MarkRegistry.ClearAll();

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            ClearTransactionCollections();
            _state = RunTransitionState.Idle;
            _failureMessage = string.Empty;
            _terminalExitArmed = false;
            _isTerminalTransition = false;

            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

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

        private void OnClientSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            ClientReportArrived();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 전환 때 참조를 다시 걸 대상을 등록한다. 중복 등록은 무시한다 </summary>
        public static void Register(IRunSceneBinding binding)
        {
            if ((binding == null) || (G_Bindings.Contains(binding)))
            {
                return;
            }

            G_Bindings.Add(binding);
        }

        public static void Unregister(IRunSceneBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            G_Bindings.Remove(binding);
        }

        /// <summary> 정적 상태를 초기화한다 </summary>
        public static void ResetStatic()
        {
            G_Bindings.Clear();
            ServerInstance = null;
            LocalInstance  = null;
        }

        /// <summary> 씬 전환을 시작한다. 이미 전환 중이거나 대상 씬을 모르면 아무것도 하지 않고 false다 </summary>
        [Server]
        public bool ServerBeginTransition(RunSceneId toScene)
        {
            bool isTerminal = (_terminalExitArmed && (_state == RunTransitionState.Failed) &&
                               (toScene == RunSceneId.Result));
            if ((_state != RunTransitionState.Idle) && (!isTerminal))
            {
                Debug.LogWarning($"[RunSceneDirector] 이미 전환 중이라 무시한다: {_state} -> {toScene}");
                return false;
            }

            if (!RunSceneCatalog.TryGetSceneName(toScene, out string sceneName))
            {
                Debug.LogError($"[RunSceneDirector] 씬 이름을 모르는 대상이다: {toScene}");
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                Debug.LogError("[RunSceneDirector] RunManager가 없어 전환을 시작할 수 없다");
                return false;
            }

            if (NetworkManager.singleton == null)
            {
                Debug.LogError("[RunSceneDirector] NetworkManager가 없어 씬을 옮길 수 없다");
                return false;
            }

            _terminalExitArmed    = false;
            _isTerminalTransition = isTerminal;
            _failureMessage       = string.Empty;
            _state                = RunTransitionState.Idle;

            // 씬을 나가면 이전 장면의 netId가 재사용될 수 있으므로 표식을 함께 비운다.
            MarkRegistry.ClearAll();
            SceneRevision next = new SceneRevision(_revisionValue).Next();
            _revisionValue = next.Value;
            _transition    = SceneTransitionSnapshot.Create(
                run.RunId, next, _currentScene, toScene, RunSceneCatalog.ShouldRestoreParticipants(toScene));

            ClearTransactionCollections();
            if (!isTerminal)
            {
                ServerCollectExpectedConnections();
            }

            _routine = StartCoroutine(CoRunTransition(sceneName));
            return true;
        }

        /// <summary>
        /// 실패한 전환을 Result로 끝내기 위한 다음 전환을 예약한다.
        /// 실패 원격 연결은 다시 ready를 기다리지 않도록 먼저 끊는다
        /// </summary>
        [Server]
        public bool ServerArmTerminalExit()
        {
            if ((_state != RunTransitionState.Failed) && (!_waitingForRecovery))
            {
                return false;
            }

            _terminalExitArmed = true;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            _state = RunTransitionState.Failed;
            _waitingForRecovery = false;

            foreach (int connectionId in _faultedConnections)
            {
                if (!NetworkServer.connections.TryGetValue(
                        connectionId, out NetworkConnectionToClient connection) || (connection == null))
                {
                    continue;
                }

                if (ReferenceEquals(connection, NetworkServer.localConnection))
                {
                    continue;
                }

                connection.Disconnect();
            }

            return true;
        }

        /// <summary> 이미 확정한 outcome을 유지한 채 실패한 Result 전환만 말단 경로로 다시 시도한다 </summary>
        [Server]
        public bool ServerRetryFailedResultTransition()
        {
            if ((_state != RunTransitionState.Failed) ||
                (_transition.ToScene != RunSceneId.Result) ||
                (!ServerArmTerminalExit()))
            {
                return false;
            }

            return ServerBeginTransition(RunSceneId.Result);
        }

        /// <summary> Lobby까지 옮기지 못한 세션을 더 진행하지 않고 네트워크와 함께 닫는다 </summary>
        [Server]
        public bool ServerStopSessionAfterFailedTransition()
        {
            if (_state != RunTransitionState.Failed)
            {
                return false;
            }

            if (_routine == null)
            {
                _routine = StartCoroutine(CoStopSessionAfterFailedTransition());
            }

            return true;
        }

        private IEnumerator CoRunTransition(string sceneName)
        {
            RunManager run = RunManager.ServerInstance;

            _state = RunTransitionState.Freezing;
            run.ServerAddPauseReason(RunPauseReason.SceneTransition, PAUSE_DETAIL);
            yield return null;

            if (_transition.RestoreParticipants && (!_isTerminalTransition))
            {
                _state = RunTransitionState.Capturing;
                if (!ServerCaptureParticipants())
                {
                    yield break;
                }

                yield return null;
            }

            for (int i = 0; i < G_Bindings.Count; ++i)
            {
                G_Bindings[i].Unbind();
            }

            _state = RunTransitionState.Loading;
            NetworkManager.singleton.ServerChangeScene(sceneName);

            double loadingDeadline = Time.realtimeSinceStartupAsDouble + LOADING_TIMEOUT;
            while ((SceneManager.GetActiveScene().name != sceneName) &&
                   (Time.realtimeSinceStartupAsDouble < loadingDeadline))
            {
                yield return _waitArrivalPoll;
            }

            if (SceneManager.GetActiveScene().name != sceneName)
            {
                if (_isTerminalTransition)
                {
                    ServerFailTransition($"종료 씬을 {LOADING_TIMEOUT:F0}초 안에 열지 못했습니다");
                    _routine = StartCoroutine(CoStopSessionAfterFailedTransition());
                    yield break;
                }

                ServerFailTransition($"서버가 {LOADING_TIMEOUT:F0}초 안에 씬을 열지 못했습니다");
                yield break;
            }

            _currentScene = _transition.ToScene;

            if (_isTerminalTransition)
            {
                _state = RunTransitionState.Binding;
                for (int i = 0; i < G_Bindings.Count; ++i)
                {
                    G_Bindings[i].Bind(in _transition);
                }
                yield return null;

                ServerCompleteTransition(run);
                yield break;
            }

            _state = RunTransitionState.WaitingForParticipant;
            double arrivalDeadline = Time.realtimeSinceStartupAsDouble + ARRIVAL_TIMEOUT;
            while ((!ServerHasEveryoneArrived()) &&
                   (Time.realtimeSinceStartupAsDouble < arrivalDeadline))
            {
                yield return _waitArrivalPoll;
            }

            if (!ServerHasEveryoneArrived())
            {
                ServerFailArrivalTimeout();
                if (!_waitingForRecovery)
                {
                    yield break;
                }
            }

            if (_transition.RestoreParticipants)
            {
                while (true)
                {
                    // 복구 대기 중에는 ServerWaitForRecovery가 세운 상태를 폴링마다 덮지 않는다
                    if (!_waitingForRecovery)
                    {
                        _state = RunTransitionState.Restoring;
                    }

                    if (ServerRestoreParticipants())
                    {
                        // 복구 결과를 다시 확인해야 하므로 아래 일반 경로와 달리 Bind 전에 한 프레임 쉬지 않는다.
                        // 다음 프레임에 끊긴 연결이나 바뀐 플레이어가 있으면 복구부터 다시 한다.
                        _state = RunTransitionState.Binding;
                        for (int i = 0; i < G_Bindings.Count; ++i)
                        {
                            G_Bindings[i].Bind(in _transition);
                        }
                        yield return null;
                        if (ServerHasRestoredParticipants())
                        {
                            ServerCompleteTransition(run);
                            yield break;
                        }
                    }

                    if (_state == RunTransitionState.Failed)
                    {
                        yield break;
                    }

                    yield return _waitArrivalPoll;
                }
            }
            yield return null;

            // 상태 복구가 위치를 포함하게 되더라도 도착 spawn이 마지막으로 적용되게 한다.
            _state = RunTransitionState.Binding;
            for (int i = 0; i < G_Bindings.Count; ++i)
            {
                G_Bindings[i].Bind(in _transition);
            }
            yield return null;

            ServerCompleteTransition(run);
        }

        private IEnumerator CoStopSessionAfterFailedTransition()
        {
            // ServerChangeScene의 비동기 콜백 스택을 빠져나온 뒤 네트워크를 닫는다.
            // 동기 LoadScene은 앞선 비동기 로드를 정리한 다음 Title을 열어 실제 씬과 상태가 갈라지지 않는다.
            yield return null;

            NetworkManager manager = NetworkManager.singleton;
            bool wasHost = NetworkServer.activeHost;
            _routine = null;

            if (manager == null)
            {
                yield break;
            }

            if (wasHost)
            {
                manager.StopHost();
                SceneManager.LoadScene(Constants.Scene.TITLE);
                yield break;
            }

            manager.StopServer();
        }

        [Server]
        private bool ServerCaptureParticipants()
        {
            HashSet<ParticipantId> capturedParticipants = new HashSet<ParticipantId>();

            foreach (int connectionId in _expectedConnections)
            {
                if (!NetworkServer.connections.TryGetValue(
                        connectionId, out NetworkConnectionToClient connection) || (connection == null))
                {
                    return ServerFailParticipant(connectionId, "캡처 전에 연결이 사라졌습니다");
                }

                if (connection.identity == null)
                {
                    return ServerFailParticipant(connectionId, "플레이어 오브젝트가 없습니다");
                }

                IPlayerRunLifecycle lifecycle = connection.identity.GetComponent<IPlayerRunLifecycle>();
                if (lifecycle == null)
                {
                    return ServerFailParticipant(connectionId, "런 상태 lifecycle이 없습니다");
                }

                if (!TryGetParticipant(connection, out ParticipantId participant))
                {
                    return ServerFailParticipant(connectionId, "인증된 참가자 신원을 찾지 못했습니다");
                }

                if (!capturedParticipants.Add(participant))
                {
                    return ServerFailParticipant(connectionId, "중복된 참가자 신원입니다");
                }

                RunParticipantSnapshot snapshot = new RunParticipantSnapshot
                {
                    RunId         = _transition.RunId,
                    Participant   = participant,
                    SceneRevision = _transition.Revision,
                };

                if (!lifecycle.CaptureRunState(snapshot))
                {
                    return ServerFailParticipant(connectionId, "런 상태를 저장하지 못했습니다");
                }

                if (!RunParticipantRegistry.ServerStore(snapshot))
                {
                    return ServerFailParticipant(connectionId, "런 상태 보관소가 snapshot을 거부했습니다");
                }

                _participants[connectionId] = participant;
            }

            return (_participants.Count == _expectedConnections.Count);
        }

        [Server]
        private bool ServerRestoreParticipants()
        {
            int restoredCount = 0;
            foreach (KeyValuePair<int, ParticipantId> pair in _participants)
            {
                int originalConnectionId = pair.Key;
                ParticipantId capturedParticipant = pair.Value;
                if (!TryFindCurrentConnection(capturedParticipant, out NetworkConnectionToClient connection))
                {
                    ServerWaitForRecovery(originalConnectionId, "복구할 참가자의 재접속을 기다립니다");
                    continue;
                }

                int connectionId = connection.connectionId;

                if (_faultedConnections.Contains(connectionId))
                {
                    ServerWaitForRecovery(connectionId, "복구할 참가자의 재접속을 기다립니다");
                    continue;
                }

                if (!ServerHasConnectionArrived(connectionId))
                {
                    ServerWaitForRecovery(connectionId, "재접속한 참가자의 도착을 기다립니다");
                    continue;
                }

                if (_restoredPlayers.TryGetValue(capturedParticipant, out NetworkIdentity restoredPlayer) &&
                    (restoredPlayer == connection.identity))
                {
                    ++restoredCount;
                    continue;
                }

                if ((!TryGetParticipant(connection, out ParticipantId currentParticipant)) ||
                    (currentParticipant != capturedParticipant))
                {
                    return ServerFailParticipant(connectionId, "도착한 참가자 신원이 캡처와 다릅니다");
                }

                if (!RunParticipantRegistry.TryPeek(
                        _transition.RunId, capturedParticipant, out RunParticipantSnapshot snapshot))
                {
                    return ServerFailParticipant(connectionId, "복구할 snapshot이 없습니다");
                }

                if ((!connection.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle)) ||
                    (!lifecycle.RestoreRunState(snapshot)))
                {
                    if (ReferenceEquals(connection, NetworkServer.localConnection))
                    {
                        return ServerFailParticipant(connectionId, "호스트 상태를 복구하지 못했습니다");
                    }

                    ServerWaitForRecovery(connectionId, "참가자 상태를 복구하지 못해 재접속을 기다립니다");
                    _faultedConnections.Add(connectionId);
                    connection.Disconnect();
                    continue;
                }

                _restoredPlayers[capturedParticipant] = connection.identity;
                ++restoredCount;
            }

            return (restoredCount == _expectedConnections.Count);
        }

        /// <summary> 전환에서 맡긴 상태는 재접속 로스터가 덮어쓰거나 먼저 소비하지 않는다 </summary>
        public bool OwnsParticipantSnapshot(ParticipantId participant)
        {
            return (_state != RunTransitionState.Idle) && _transition.RestoreParticipants &&
                _participants.ContainsValue(participant);
        }

        [Server]
        private bool ServerHasRestoredParticipants()
        {
            foreach (ParticipantId participant in _participants.Values)
            {
                if ((!TryFindCurrentConnection(participant, out NetworkConnectionToClient connection)) ||
                    (!ServerHasConnectionArrived(connection.connectionId)) ||
                    (!_restoredPlayers.TryGetValue(participant, out NetworkIdentity restoredPlayer)) ||
                    (restoredPlayer != connection.identity))
                {
                    return false;
                }
            }

            return (_restoredPlayers.Count == _expectedConnections.Count);
        }

        [Server]
        private void ServerWaitForRecovery(int connectionId, string reason)
        {
            _state = RunTransitionState.WaitingForParticipant;
            if (_waitingForRecovery)
            {
                return;
            }

            string message = $"참가자 복구 대기({NetworkPeerLabel.Describe(connectionId)}): {reason}";
            Debug.LogWarning($"[RunSceneDirector] {message}", this);
            _failureMessage = message;
            _waitingForRecovery = true;
            RunManager run = RunManager.ServerInstance;
            if (run != null)
            {
                run.PauseRun(message);
            }
        }

        /// <summary> 재접속으로 connectionId가 바뀌어도 같은 인증 참가자의 현재 연결을 찾는다 </summary>
        [Server]
        private static bool TryFindCurrentConnection(ParticipantId participant,
            out NetworkConnectionToClient matchedConnection)
        {
            matchedConnection = null;
            GeumeumAuthenticator authenticator = (NetworkManager.singleton == null)
                ? null : (NetworkManager.singleton.authenticator as GeumeumAuthenticator);
            if ((authenticator == null) || (!participant.IsValid))
            {
                return false;
            }

            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if ((connection == null) ||
                    (!authenticator.TryGetPlatformId(connection.connectionId, out PlayerPlatformId platformId)) ||
                    (ParticipantId.FromPlatformId(platformId) != participant))
                {
                    continue;
                }

                // 인증기가 막아야 하는 중복 신원이 생겼다면 어느 쪽에도 상태를 넘기지 않는다.
                if (matchedConnection != null)
                {
                    matchedConnection = null;
                    return false;
                }

                matchedConnection = connection;
            }

            return (matchedConnection != null);
        }

        /// <summary> 연결의 참가자 식별을 구한다 </summary>
        [Server]
        private static bool TryGetParticipant(NetworkConnectionToClient connection, out ParticipantId participant)
        {
            participant = ParticipantId.None;

            GeumeumAuthenticator authenticator = (NetworkManager.singleton == null)
                ? null : (NetworkManager.singleton.authenticator as GeumeumAuthenticator);
            if (authenticator == null)
            {
                Debug.LogError("[RunSceneDirector] GeumeumAuthenticator가 없어 참가자를 식별할 수 없다");
                return false;
            }

            if (!authenticator.TryGetPlatformId(
                    connection.connectionId, out PlayerPlatformId platformId))
            {
                Debug.LogError($"[RunSceneDirector] 인증 목록에 없는 연결이다: {NetworkPeerLabel.Describe(connection)}");
                return false;
            }

            participant = ParticipantId.FromPlatformId(platformId);
            return participant.IsValid;
        }

        /// <summary> 시작할 때 확정한 전원이 새 씬에 도착했는가 </summary>
        [Server]
        private bool ServerHasEveryoneArrived()
        {
            if (_transition.RestoreParticipants)
            {
                foreach (KeyValuePair<int, ParticipantId> pair in _participants)
                {
                    if ((!TryFindCurrentConnection(pair.Value, out NetworkConnectionToClient connection)) ||
                        (!ServerHasConnectionArrived(connection.connectionId)))
                    {
                        return false;
                    }
                }

                return (_participants.Count == _expectedConnections.Count);
            }

            foreach (int connectionId in _expectedConnections)
            {
                if (!ServerHasConnectionArrived(connectionId))
                {
                    return false;
                }
            }

            return true;
        }

        [Server]
        private void ServerCollectExpectedConnections()
        {
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                _expectedConnections.Add(pair.Key);
            }
        }

        [Server]
        private bool ServerHasConnectionArrived(int connectionId)
        {
            if (!NetworkServer.connections.TryGetValue(
                    connectionId, out NetworkConnectionToClient connection) ||
                (connection == null) || (connection.identity == null))
            {
                return false;
            }

            return (connection.isReady || _arrivedConnections.Contains(connectionId));
        }

        [Server]
        private void ServerFailArrivalTimeout()
        {
            if (_transition.RestoreParticipants)
            {
                foreach (KeyValuePair<int, ParticipantId> pair in _participants)
                {
                    if (TryFindCurrentConnection(pair.Value, out NetworkConnectionToClient connection))
                    {
                        if (!ServerHasConnectionArrived(connection.connectionId))
                        {
                            _faultedConnections.Add(connection.connectionId);
                        }
                    }
                    else
                    {
                        _faultedConnections.Add(pair.Key);
                    }
                }
            }
            else
            {
                foreach (int connectionId in _expectedConnections)
                {
                    if (!ServerHasConnectionArrived(connectionId))
                    {
                        _faultedConnections.Add(connectionId);
                    }
                }
            }

            // 호스트가 도착했고 전환 상태가 보관돼 있으면 원격만 다시 접속시킨다.
            // 정원과 참가자 신원은 줄이지 않으며 호스트는 기존 대기 포기로 런을 끝낼 수 있다.
            bool canRecover = _transition.RestoreParticipants &&
                (_participants.Count == _expectedConnections.Count) &&
                ((NetworkServer.localConnection == null) ||
                    (!_faultedConnections.Contains(NetworkServer.localConnection.connectionId)));
            if (canRecover)
            {
                foreach (int connectionId in _faultedConnections)
                {
                    ServerWaitForRecovery(connectionId, "씬 도착 제한 시간을 넘겨 재접속을 기다립니다");
                    if ((!NetworkServer.connections.TryGetValue(
                            connectionId, out NetworkConnectionToClient connection)) ||
                        (connection == null))
                    {
                        continue;
                    }

                    // 캡처 당시의 connectionId가 다른 사람에게 다시 쓰였을 수 있다.
                    // 이 전환의 참가자로 확인된 연결만 끊는다.
                    if (TryGetParticipant(connection, out ParticipantId participant) &&
                        _participants.ContainsValue(participant))
                    {
                        connection.Disconnect();
                    }
                }
                return;
            }

            ServerFailTransition(
                $"참가자 도착을 {ARRIVAL_TIMEOUT:F0}초 안에 확인하지 못했습니다");
        }

        [Server]
        private bool ServerFailParticipant(int connectionId, string reason)
        {
            _faultedConnections.Add(connectionId);
            ServerFailTransition($"참가자 전환 실패({NetworkPeerLabel.Describe(connectionId)}): {reason}");
            return false;
        }

        [Server]
        private void ServerFailTransition(string message)
        {
            _failureMessage = message;
            _state          = RunTransitionState.Failed;
            _routine        = null;

            Debug.LogError($"[RunSceneDirector] {message}", this);
        }

        [Server]
        private void ServerCompleteTransition(RunManager run)
        {
            _state = RunTransitionState.Committing;
            foreach (ParticipantId participant in _participants.Values)
            {
                if (RunParticipantRegistry.TryPeek(_transition.RunId, participant, out RunParticipantSnapshot snapshot))
                {
                    RunParticipantRegistry.ServerCompleteRestore(snapshot);
                }

                LobbyRoster roster = LobbyRoster.ServerInstance;
                if (roster != null)
                {
                    roster.ServerCompleteRecovery(participant);
                }
            }

            if (_transition.RestoreParticipants)
            {
                run.ResumeRun();
            }
            run.ServerClearPauseReason(RunPauseReason.SceneTransition);

            _state                = RunTransitionState.Idle;
            _routine              = null;
            _failureMessage       = string.Empty;
            _terminalExitArmed    = false;
            _isTerminalTransition = false;
            ClearTransactionCollections();

            // 전실과 아레나는 서로 다른 전환 트랜잭션이다. 첫 전환의 도착, 복구, 바인딩이
            // 모두 끝난 이 시점에만 RunManager가 다음 전환을 요청할 수 있다.
            run.ServerHandleSceneTransitionReady();
        }

        private void ClearTransactionCollections()
        {
            _expectedConnections.Clear();
            _arrivedConnections.Clear();
            _faultedConnections.Clear();
            _participants.Clear();
            _restoredPlayers.Clear();
            _waitingForRecovery = false;
        }

        /// <summary> 클라이언트가 새 씬에 도착했다고 알린다 </summary>
        [Command(requiresAuthority = false)]
        private void CmdReportArrived(uint revisionValue, NetworkConnectionToClient sender = null)
        {
            if ((sender == null) || (revisionValue != _revisionValue))
            {
                return;
            }

            bool isAccepting = ((_state == RunTransitionState.Loading) ||
                                (_state == RunTransitionState.WaitingForParticipant));
            if (!isAccepting)
            {
                return;
            }

            if (_transition.RestoreParticipants)
            {
                if ((!TryGetParticipant(sender, out ParticipantId participant)) ||
                    (!_participants.ContainsValue(participant)))
                {
                    return;
                }
            }
            else if (!_expectedConnections.Contains(sender.connectionId))
            {
                return;
            }

            _arrivedConnections.Add(sender.connectionId);
        }

        /// <summary>
        /// 클라이언트가 자기 씬 로드를 끝냈다고 서버를 부른다.
        ///
        /// SceneManager.sceneLoaded는 Mirror가 NetworkClient.Ready()를 부르기 전에 발생한다.
        /// 그 시점의 Command는 보내지 않고, 준비 뒤 GeumeumNetworkManager.OnClientSceneChanged가
        /// 같은 메서드를 다시 부르는 경로를 사용한다
        /// </summary>
        public void ClientReportArrived()
        {
            if ((!NetworkClient.active) || (!NetworkClient.ready))
            {
                return;
            }

            CmdReportArrived(_revisionValue);
        }
    }
}
