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
        private const int    NO_CONNECTION   = 0;

        public static RunSceneDirector ServerInstance { get; private set; }
        public static RunSceneDirector LocalInstance  { get; private set; }

        [SyncVar] private RunTransitionState _state         = RunTransitionState.Idle;
        [SyncVar] private RunSceneId         _currentScene  = RunSceneId.None;
        [SyncVar] private uint               _revisionValue = 0u;

        // 전환마다 다시 바인딩할 대상 목록
        private static readonly List<IRunSceneBinding> G_Bindings = new List<IRunSceneBinding>();

        // 이 세대에 도착을 알린 연결
        private readonly HashSet<int> _arrivedConnections = new HashSet<int>();

        private SceneTransitionSnapshot _transition;
        private Coroutine               _routine;
        private WaitForSeconds          _waitArrivalPoll;

        /****************************************
        *              Properties
        ****************************************/

        public RunTransitionState State        => _state;
        public RunSceneId         CurrentScene => _currentScene;
        public SceneRevision      Revision     => new SceneRevision(_revisionValue);

        /// <summary> 지금 씬을 옮기는 중인가 </summary>
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
        }

        public override void OnStartServer()
        {
            ServerInstance = this;
            _currentScene  = RunSceneCatalog.FromSceneName(SceneManager.GetActiveScene().name);
        }

        public override void OnStopServer()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _arrivedConnections.Clear();

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
            if (binding == null)
            {
                return;
            }

            if (G_Bindings.Contains(binding))
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
            if (_state != RunTransitionState.Idle)
            {
                Debug.LogWarning($"[RunSceneDirector] 이미 전환 중이라 무시한다: {_state} -> {toScene}");
                return false;
            }

            string sceneName;
            if (!RunSceneCatalog.TryGetSceneName(toScene, out sceneName))
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

            SceneRevision next = new SceneRevision(_revisionValue).Next();
            _revisionValue = next.Value;
            _transition    = SceneTransitionSnapshot.Create(
                run.RunId, next, _currentScene, toScene, RunSceneCatalog.ShouldRestoreParticipants(toScene));

            _arrivedConnections.Clear();
            _routine = StartCoroutine(CoRunTransition(sceneName));
            return true;
        }

        private IEnumerator CoRunTransition(string sceneName)
        {
            RunManager run = RunManager.ServerInstance;

            _state = RunTransitionState.Freezing;
            run.ServerAddPauseReason(RunPauseReason.SceneTransition, PAUSE_DETAIL);
            yield return null;

            _state = RunTransitionState.Capturing;
            ServerCaptureParticipants();
            yield return null;

            for (int i = 0; i < G_Bindings.Count; ++i)
            {
                G_Bindings[i].Unbind();
            }

            _state = RunTransitionState.Loading;
            NetworkManager.singleton.ServerChangeScene(sceneName);

            while (SceneManager.GetActiveScene().name != sceneName)
            {
                yield return _waitArrivalPoll;
            }

            _currentScene = _transition.ToScene;

            _state = RunTransitionState.WaitingForParticipant;
            float waited = 0f;
            while ((!ServerHasEveryoneArrived()) && (waited < ARRIVAL_TIMEOUT))
            {
                waited += ARRIVAL_POLL;
                yield return _waitArrivalPoll;
            }

            if (!ServerHasEveryoneArrived())
            {
                Debug.LogWarning(
                    $"[RunSceneDirector] 도착 대기가 {ARRIVAL_TIMEOUT}초를 넘겼다, 도착 {_arrivedConnections.Count}");
            }

            _state = RunTransitionState.Binding;
            for (int i = 0; i < G_Bindings.Count; ++i)
            {
                G_Bindings[i].Bind(in _transition);
            }
            yield return null;

            _state = RunTransitionState.Restoring;
            if (_transition.RestoreParticipants)
            {
                ServerRestoreParticipants();
            }
            yield return null;

            if (_state == RunTransitionState.Failed)
            {
                _routine = null;
                yield break;
            }

            _state = RunTransitionState.Committing;
            run.ServerClearPauseReason(RunPauseReason.SceneTransition);
            _state   = RunTransitionState.Idle;
            _routine = null;
        }

        [Server]
        private void ServerCaptureParticipants()
        {
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if ((connection == null) || (connection.identity == null))
                {
                    continue;
                }

                IPlayerRunLifecycle lifecycle = connection.identity.GetComponent<IPlayerRunLifecycle>();
                if (lifecycle == null)
                {
                    continue;
                }

                ParticipantId participant;
                if (!TryGetParticipant(connection, out participant))
                {
                    continue;
                }

                RunParticipantSnapshot snapshot = new RunParticipantSnapshot();
                snapshot.RunId         = _transition.RunId;
                snapshot.Participant   = participant;
                snapshot.SceneRevision = _transition.Revision;

                if (!lifecycle.CaptureRunState(snapshot))
                {
                    continue;
                }

                RunParticipantRegistry.ServerStore(snapshot);
            }
        }

        [Server]
        private void ServerRestoreParticipants()
        {
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if (connection == null)
                {
                    continue;
                }

                if (connection.identity == null)
                {
                    _state = RunTransitionState.Failed;
                    Debug.LogError(
                        $"[RunSceneDirector] 도착했는데 플레이어 오브젝트가 없다, 연결 {connection.connectionId}");
                    continue;
                }

                IPlayerRunLifecycle lifecycle = connection.identity.GetComponent<IPlayerRunLifecycle>();
                if (lifecycle == null)
                {
                    continue;
                }

                ParticipantId participant;
                if (!TryGetParticipant(connection, out participant))
                {
                    continue;
                }

                RunParticipantSnapshot snapshot;
                if (!RunParticipantRegistry.TryTake(_transition.RunId, participant, out snapshot))
                {
                    continue;
                }

                if (lifecycle.RestoreRunState(snapshot))
                {
                    continue;
                }

                _state = RunTransitionState.Failed;
                Debug.LogError($"[RunSceneDirector] 참가자 복구 실패, 연결 {connection.connectionId}");
            }
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

            PlayerPlatformId platformId;
            if (!authenticator.TryGetPlatformId(connection.connectionId, out platformId))
            {
                Debug.LogError($"[RunSceneDirector] 인증 목록에 없는 연결이다: {connection.connectionId}");
                return false;
            }

            participant = ParticipantId.FromPlatformId(platformId);
            return participant.IsValid;
        }

        /// <summary> 전원이 새 씬에 도착했는가 </summary>
        [Server]
        private bool ServerHasEveryoneArrived()
        {
            int expected = 0;
            int arrived  = 0;
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if (connection == null)
                {
                    continue;
                }

                ++expected;

                if (connection.identity == null)
                {
                    continue;
                }

                if ((connection.isReady) || (_arrivedConnections.Contains(connection.connectionId)))
                {
                    ++arrived;
                }
            }

            if (expected == NO_CONNECTION)
            {
                return true;
            }

            return (arrived >= expected);
        }

        /// <summary> 클라이언트가 도착을 알린다 </summary>
        [Command(requiresAuthority = false)]
        private void CmdReportArrived(uint revisionValue, NetworkConnectionToClient sender = null)
        {
            if (sender == null)
            {
                return;
            }

            if (revisionValue != _revisionValue)
            {
                return;
            }

            bool isAccepting = ((_state == RunTransitionState.Loading) ||
                                (_state == RunTransitionState.WaitingForParticipant));
            if (!isAccepting)
            {
                return;
            }

            _arrivedConnections.Add(sender.connectionId);
        }

        /// <summary> 클라이언트가 자기 씬 로드를 끝냈을 때 부른다 </summary>
        public void ClientReportArrived()
        {
            if (!NetworkClient.active)
            {
                return;
            }

            CmdReportArrived(_revisionValue);
        }
    }
}
