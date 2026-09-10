// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> Steam 로비와 Mirror 연결을 아우르는 세션 단계 </summary>
    public class SteamSessionCoordinator : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ARG_CONNECT_LOBBY = "+connect_lobby";

        private SteamMirrorConnector _connector;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 씬에 하나만 둔다. 개발 HUD와 로비 UI가 읽는다 </summary>
        public static SteamSessionCoordinator Instance { get; private set; }

        /// <summary> 현재 세션 단계 </summary>
        public SteamSessionState State { get; private set; } = SteamSessionState.Offline;

        /// <summary>
        /// 호스트를 열 수 있는가. 소유자이고 필요한 인원이 다 모였을 때만.
        ///
        /// 인원을 낮출 수 있게 열어 두면 호스트 혼자 런이 시작되면서 입장이 잠겨
        /// 나머지 둘이 거부된다. 그래서 기본은 정원 고정이고,
        /// 개발 패널에서 사람이 직접 고른 인원만 예외로 받는다
        /// </summary>
        public bool CanStartHost
        {
            get
            {
                SteamLobbyService lobby = SteamLobbyService.Instance;
                return (State == SteamSessionState.Gathering)
                    && (lobby != null) && (lobby.IsOwner)
                    && (lobby.Members.Count >= ResolveRequiredMembers());
            }
        }

        /// <summary> 호스트를 열기 위해 로비에 모여 있어야 하는 인원 </summary>
        private static int ResolveRequiredMembers()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 테스트 실행 패널의 인원. 씬 값이나 실행 인자로는 여기까지 내려오지 않는다 -
            // 사고로 1인이 되면 나머지 둘이 입장을 거부당하므로 사람이 그 화면에서 누른 값만 받는다
            int chosen = LobbyController.DevRequiredPlayers;

            if ((chosen > 0) && (chosen <= SteamLobbyService.MAX_MEMBERS))
            {
                return chosen;
            }
#endif

            return SteamLobbyService.MAX_MEMBERS;
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

            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.JoinLobby(lobbyId);
        }

        /// <summary> 방을 만든다. 성공하면 Gathering으로 간다 </summary>
        public void CreateLobby()
        {
            if (!EnsureSteamPath())
            {
                return;
            }

            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.CreateLobby();
        }

        /// <summary> 초대 창을 연다 </summary>
        public void OpenInviteOverlay()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.OpenInviteOverlay();
        }

        /// <summary> 친구에게 초대를 직접 보낸다. 오버레이가 없을 때의 경로다 </summary>
        public void InviteUser(ulong friendSteamId)
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.InviteUser(friendSteamId);
        }

        /// <summary> 세션에서 빠진다 </summary>
        public void LeaveLobby()
        {
            StopNetworkSession();

            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.LeaveLobby();
        }

        /// <summary> 호스트를 연다 </summary>
        public void StartHost()
        {
            if (!CanStartHost)
            {
                Debug.LogWarning($"[SteamSessionCoordinator] 지금은 호스트를 열 수 없다: 단계={State}", this);
                return;
            }

            Commit(SteamSessionState.StartingHost);
            _connector.StartSteamHost();

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

        private static void StopNetworkSession()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                return;
            }

            if ((NetworkServer.active) && (NetworkClient.active))
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

    /// <summary> 세션 단계 </summary>
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
