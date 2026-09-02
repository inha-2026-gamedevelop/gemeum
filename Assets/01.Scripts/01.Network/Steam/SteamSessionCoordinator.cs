// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary>
    /// Steam 로비와 Mirror 연결을 아우르는 세션 단계. 상태 변경은 여기 한 곳에서만 커밋한다,
    /// Steam 콜백과 Mirror 콜백이 각자 UI를 열고 닫으면 어느 쪽이 최신인지 알 수 없게 된다.
    /// 설계안 5장의 11개 상태 중 3단계에 필요한 6개만 둔다 — 측정과 선출은 4단계다
    /// </summary>
    public class SteamSessionCoordinator : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ARG_CONNECT_LOBBY = "+connect_lobby";

        [Tooltip("이 인원이 모이면 호스트를 열 수 있다. 실전은 3명이고 2대로 시험할 때만 낮춘다")]
        [SerializeField] private int _devRequiredMembers = SteamLobbyService.MAX_MEMBERS;

        private SteamMirrorConnector _connector;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 씬에 하나만 둔다. 개발 HUD와 로비 UI가 읽는다 </summary>
        public static SteamSessionCoordinator Instance { get; private set; }

        /// <summary> 현재 세션 단계 </summary>
        public SteamSessionState State { get; private set; } = SteamSessionState.Offline;

        /// <summary> 호스트를 열 수 있는가. 소유자이고 인원이 찼을 때만 </summary>
        public bool CanStartHost
        {
            get
            {
                SteamLobbyService lobby = SteamLobbyService.Instance;
                return (State == SteamSessionState.Gathering)
                    && (lobby != null) && (lobby.IsOwner)
                    && (lobby.Members.Count >= _devRequiredMembers);
            }
        }

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 단계가 바뀌었다. UI는 이것만 보고 그린다 </summary>
        public event Action<SteamSessionState> StateChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Instance   = this;
            _connector = GetComponent<SteamMirrorConnector>();

            if (_connector == null)
            {
                Debug.LogError("[SteamSessionCoordinator] 같은 오브젝트에 SteamMirrorConnector가 없다", this);
            }
        }

        private void OnEnable()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby != null)
            {
                lobby.LobbyEntered   += OnLobbyEntered;
                lobby.LobbyLeft      += OnLobbyLeft;
                lobby.MembersChanged += OnMembersChanged;
                lobby.GameServerSet  += OnGameServerSet;
            }

            // Mirror의 NetworkClient.OnConnectedEvent는 NetworkManager가 대입으로 덮어쓴다.
            // 거기 직접 구독하면 클라이언트를 시작하는 순간 지워지므로 매니저가 다시 낸 이벤트를 쓴다
            GeumeumNetworkManager.ClientConnected    += OnClientConnected;
            GeumeumNetworkManager.ClientDisconnected += OnClientDisconnected;
        }

        private void OnDisable()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby != null)
            {
                lobby.LobbyEntered   -= OnLobbyEntered;
                lobby.LobbyLeft      -= OnLobbyLeft;
                lobby.MembersChanged -= OnMembersChanged;
                lobby.GameServerSet  -= OnGameServerSet;
            }

            GeumeumNetworkManager.ClientConnected    -= OnClientConnected;
            GeumeumNetworkManager.ClientDisconnected -= OnClientDisconnected;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Steam 초기화는 SteamManager가 몇 프레임 걸려 끝내므로 Awake에서 단정할 수 없다
        private void Start()
        {
            RefreshSteamReady();
            TryJoinFromCommandLine();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 로비 ID로 직접 참가한다. 오버레이 초대를 못 쓸 때의 우회 경로다 </summary>
        public void JoinLobby(ulong lobbyId)
        {
            if (!EnsureSteamPath())
            {
                return;
            }

            SteamLobbyService.Instance?.JoinLobby(lobbyId);
        }

        /// <summary> 방을 만든다. 성공하면 Gathering으로 간다 </summary>
        public void CreateLobby()
        {
            if (!EnsureSteamPath())
            {
                return;
            }

            SteamLobbyService.Instance?.CreateLobby();
        }

        /// <summary> 초대 창을 연다 </summary>
        public void OpenInviteOverlay()
        {
            SteamLobbyService.Instance?.OpenInviteOverlay();
        }

        /// <summary> 친구에게 초대를 직접 보낸다. 오버레이가 없을 때의 경로다 </summary>
        public void InviteUser(ulong friendSteamId)
        {
            SteamLobbyService.Instance?.InviteUser(friendSteamId);
        }

        /// <summary>
        /// 세션에서 빠진다. 로비만 나가면 Mirror 연결이 그대로 남아
        /// 화면상 아무것도 달라지지 않는다 — 쓰는 사람에게는 나가지지 않는 것으로 보인다
        /// </summary>
        public void LeaveLobby()
        {
            StopNetworkSession();
            SteamLobbyService.Instance?.LeaveLobby();
        }

        /// <summary>
        /// 호스트를 연다. 3단계에서는 선출 없이 로비 소유자가 그대로 호스트가 된다,
        /// 후보 정렬은 4단계에서 이 앞에 끼워 넣는다
        /// </summary>
        public void StartHost()
        {
            if (!CanStartHost)
            {
                Debug.LogWarning($"[SteamSessionCoordinator] 지금은 호스트를 열 수 없다: 단계={State}", this);
                return;
            }

            Commit(SteamSessionState.StartingHost);
            _connector.StartSteamHost();

            // 커넥터가 게시까지 끝냈으면 서버가 떠 있다. 실패했으면 다시 모으는 단계로 돌린다
            Commit(NetworkServer.active ? SteamSessionState.Connecting : SteamSessionState.Gathering);
        }

        /****************************************
        *              Callbacks
        ****************************************/

        private void OnLobbyEntered(ulong lobbyId)
        {
            Commit(SteamSessionState.Gathering);
        }

        private void OnLobbyLeft(string reason)
        {
            RefreshSteamReady();
        }

        private void OnMembersChanged()
        {
            // 단계는 그대로지만 인원이 바뀌었다. UI가 명단과 시작 가능 여부를 다시 그려야 한다
            if ((State == SteamSessionState.Gathering) || (State == SteamSessionState.Connecting))
            {
                StateChanged?.Invoke(State);
            }
        }

        private void OnGameServerSet(ulong hostSteamId)
        {
            if (State == SteamSessionState.Gathering)
            {
                Commit(SteamSessionState.Connecting);
            }
        }

        private void OnClientConnected()
        {
            Commit(SteamSessionState.Ready);
        }

        private void OnClientDisconnected()
        {
            if (State == SteamSessionState.Ready)
            {
                Commit(SteamSessionState.Gathering);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshSteamReady()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby != null) && (lobby.IsInLobby))
            {
                Commit(SteamSessionState.Gathering);
                return;
            }

            Commit(SteamLocalUser.TryGetSteamId(out ulong _)
                ? SteamSessionState.SteamReady
                : SteamSessionState.Offline);
        }

        private void Commit(SteamSessionState next)
        {
            if (State == next)
            {
                return;
            }

            Debug.Log($"[SteamSessionCoordinator] 단계 전환: {State} -> {next}");
            State = next;

            PublishState(next);
            StateChanged?.Invoke(next);
        }

        // 공용 데이터는 소유자만 쓸 수 있다. 소유자가 아니면 조용히 지나간다
        private void PublishState(SteamSessionState next)
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby) || (!lobby.IsOwner))
            {
                return;
            }

            string value = ToLobbyValue(next);
            if (!string.IsNullOrEmpty(value))
            {
                lobby.SetData(SteamLobbyKeys.STATE, value);
            }
        }

        private static string ToLobbyValue(SteamSessionState state)
        {
            switch (state)
            {
                case SteamSessionState.Gathering:  return SteamLobbyKeys.State.GATHERING;
                case SteamSessionState.Connecting: return SteamLobbyKeys.State.CONNECTING;
                case SteamSessionState.Ready:      return SteamLobbyKeys.State.READY;
                default:                           return null;
            }
        }

        // Steam은 게임이 꺼진 상태에서 초대를 수락하면 +connect_lobby <id>로 실행한다.
        // 스크립트로 로비 ID를 직접 줄 때도 같은 인자를 쓴다
        private void TryJoinFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (!string.Equals(args[i], ARG_CONNECT_LOBBY, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ulong.TryParse(args[i + 1], out ulong lobbyId))
                {
                    Debug.LogWarning($"[SteamSessionCoordinator] {ARG_CONNECT_LOBBY} 값이 숫자가 아니다: {args[i + 1]}", this);
                    return;
                }

                Debug.Log($"[SteamSessionCoordinator] 실행 인자로 로비 {lobbyId}에 참가한다");
                JoinLobby(lobbyId);
                return;
            }
        }

        // 호스트면 서버와 자기 클라이언트를 함께 닫는다. 남은 두 명은 호스트 소실로 처리된다
        private static void StopNetworkSession()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                return;
            }

            if (NetworkServer.active && NetworkClient.active)
            {
                manager.StopHost();
                return;
            }

            if (NetworkClient.active)
            {
                manager.StopClient();
                return;
            }

            if (NetworkServer.active)
            {
                manager.StopServer();
            }
        }

        private bool EnsureSteamPath()
        {
            if (SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return true;
            }

            Debug.LogError("[SteamSessionCoordinator] Steam 경로가 아니다, -steam 인자로 실행할 것", this);
            return false;
        }
    }

    /// <summary>
    /// 세션 단계. 설계안 5장의 부분집합이고, 측정·선출·런 관련 상태는 여기 없다.
    /// 로비 데이터 문자열과 짝이므로 이름을 임의로 바꾸지 않는다
    /// </summary>
    public enum SteamSessionState
    {
        Offline      = 0, // Steam 세션 없음
        SteamReady   = 1, // Steam 초기화 성공, 방을 만들거나 초대를 받을 수 있다
        Gathering    = 2, // 로비에 들어가 인원을 모으는 중
        StartingHost = 3, // 호스트 기동과 세션 게시 진행 중
        Connecting   = 4, // 나머지 멤버가 붙는 중
        Ready        = 5, // 연결 완료, Mirror 로비가 준비 상태를 관리한다
    }
}
