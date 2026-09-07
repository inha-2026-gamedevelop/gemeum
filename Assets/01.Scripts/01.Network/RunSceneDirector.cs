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
    /// <summary>
    /// 런의 씬 전환을 소유한다. 서버가 단계를 진행하고 클라이언트는 도착만 알린다.
    ///
    /// 전환을 각 씬이 알아서 하면 정지 사유, 상태 담기, 도착 확인이 씬마다 다르게 틀린다.
    /// 런당 전환이 다섯 번이고 그 다섯 번이 모두 전멸 오탐과 유령 참조의 후보다
    /// </summary>
    public class RunSceneDirector : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PAUSE_DETAIL    = "씬 전환";
        private const float  ARRIVAL_POLL    = 0.1f; // 전원 도착을 확인하는 주기, 초
        private const float  ARRIVAL_TIMEOUT = 30f;  // 이 시간이 지나면 기다리기를 멈추고 알린다
        private const int    NO_CONNECTION   = 0;

        public static RunSceneDirector ServerInstance { get; private set; }
        public static RunSceneDirector LocalInstance  { get; private set; }

        [SyncVar] private RunTransitionState _state         = RunTransitionState.Idle;
        [SyncVar] private RunSceneId         _currentScene  = RunSceneId.None;
        [SyncVar] private uint               _revisionValue = 0u;

        // 세션 내내 사는 객체는 씬마다 OnStartServer를 다시 받지 않는다.
        // 옛 씬의 참조를 붙든 채 돌지 않도록 바인딩 대상을 여기 모아 두고 전환마다 다시 건다
        private static readonly List<IRunSceneBinding> G_Bindings = new List<IRunSceneBinding>();

        // 이 세대에 도착을 알린 연결. 세대가 없으면 이전 전환의 늦은 통지가 다음 전환을 통과시킨다
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

        /// <summary> 지금 씬을 옮기는 중인가. 전멸 판정처럼 중간 상태를 오해하는 곳이 먼저 묻는다 </summary>
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

        // 씬 전환 중 Mirror 가 OnStopClient 를 부르면서 구독이 풀리고 다시 걸리지 않는다.
        // 그러면 도착 통지가 한 번도 안 가고 매 전환이 타임아웃까지 기다린다.
        // 이 오브젝트는 세션 내내 살아 있으므로 Mirror 수명이 아니라 자기 수명에 건다
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

        // 자기 로드가 끝난 것은 클라이언트 자신만 안다. 서버는 이 통지를 받아야 전원 도착을 판정한다
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

        /// <summary> 도메인 리로드를 끄고 재생할 때 이전 판의 목록이 남지 않게 한다 </summary>
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

            // 판정을 먼저 멈춘다. 담는 도중에 값이 움직이면 되부을 때 어긋난다
            _state = RunTransitionState.Freezing;
            run.ServerAddPauseReason(RunPauseReason.SceneTransition, PAUSE_DETAIL);
            yield return null;

            _state = RunTransitionState.Capturing;
            ServerCaptureParticipants();
            yield return null;

            // 떠나는 씬의 참조를 먼저 푼다. 파괴된 뒤에 풀면 이미 늦다
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

            // 전원 도착을 기다린다. 한 명이 느릴 때 혼자 보스 앞에 떨어지는 것을 여기서 막는다
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

            // 복구가 실패했으면 정지 사유를 풀지 않는다. 반쯤 복구된 상태로 런을 재개하지 않는다
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

                // 몸이 없으면 부을 곳이 없다. 여기서 조용히 넘어가면 담아 둔 무기와 캐릭터가
                // 영영 적용되지 않은 채 런이 재개되어, 도착한 사람만 직업 없는 몸이 된다
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

                // 반쯤 복구된 상태로 런을 재개하지 않는다. 사람이 봐야 한다
                _state = RunTransitionState.Failed;
                Debug.LogError($"[RunSceneDirector] 참가자 복구 실패, 연결 {connection.connectionId}");
            }
        }

        /// <summary>
        /// 연결의 참가자 식별을 구한다. 연결 번호가 아니라 플랫폼 식별을 쓴다 -
        /// 재접속하면 연결 번호가 바뀌므로 그것으로 담아 두면 돌아온 사람의 상태를 못 찾는다
        /// </summary>
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

        /// <summary>
        /// 전원이 새 씬에 도착했는가.
        ///
        /// Mirror가 씬을 옮길 때 모든 연결을 not ready로 내리고, 각자 로드를 마치면 다시 ready가 된다.
        /// 그것이 곧 도착 신호라 따로 만들지 않는다 - 직접 만든 Command 는 전환 중 not ready 구간에
        /// 걸려 한 번도 도달하지 못했다(실측: 도착 0으로 매번 타임아웃).
        /// CmdReportArrived 는 보조 신호로 남겨 두고, 둘 중 하나만 와도 도착으로 친다
        /// </summary>
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

                // 준비 신호만으로는 이르다. Mirror는 Ready와 AddPlayer를 따로 보내므로
                // isReady가 켜졌는데 플레이어 오브젝트는 아직 없는 구간이 있다.
                // 그 사이에 되붓기가 돌면 담아 둔 무기와 캐릭터를 부을 몸이 없어 조용히 건너뛴다
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

        /// <summary> 클라이언트가 도착을 알린다. 세대가 어긋난 늦은 통지는 버린다 </summary>
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

            // Loading 도 받는다. 서버가 자기 로드를 마치기 전에 끝낸 클라이언트가 있고,
            // 호스트 자신의 통지는 항상 그 시점에 온다. WaitingForParticipant 만 받으면
            // 매 전환이 도착 대기 타임아웃까지 간다
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
