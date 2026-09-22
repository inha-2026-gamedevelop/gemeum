// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Dev;

namespace Geumeum.Network
{
    /// <summary> Steam 로비를 손으로 돌려 보기 위한 개발용 오버레이 </summary>
    public class SteamLobbyHUD : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "steam";
        private const string PANEL_TITLE = "Steam 로비";

        /****************************************
        *                Fields
        ****************************************/

        // PC 3대를 각자 조작해야 해서 마우스로 패널을 누르는 것보다 단축키가 빠르다
        //
        // F1~F12는 전부 차 있다. 코드 기본값이 KeyCode.None이고 씬과 프리팹에 값이 들어 있는
        // 키(GeumeumNetworkManager의 _devStartHostKey=F1, _devAbandonRunKey=F3)가 있어
        // 코드만 훑으면 비어 보인다. 새 키를 잡기 전에 PartyDebugOverlay의 G_KeyHelp와
        // 프리팹 직렬화 값을 함께 볼 것. 그래서 네 키 모두 F키 밖 이동키 무리로 모았다
        [Tooltip("방을 만든다. 호스트가 될 PC에서 누른다")]
        [SerializeField] private KeyCode _createLobbyKey = KeyCode.Home;

        [Tooltip("클립보드에 있는 로비 ID로 참가한다. 클라이언트 PC에서 누른다")]
        [SerializeField] private KeyCode _joinLobbyKey = KeyCode.PageDown;

        [Tooltip("3인이 모인 뒤 호스트를 연다. 로비 소유자만 통한다")]
        [SerializeField] private KeyCode _startHostKey = KeyCode.End;

        // 참가할 로비 ID 입력
        private string _joinLobbyId = string.Empty;

        // 초대할 친구의 SteamID64
        private string _inviteSteamId = string.Empty;


        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 이 도구의 단축키(Home / PageDown / End)가 치트 해제 차례와 겹친다
            // 잠긴 동안 켜져 있으면 해제 키를 치는 것만으로 로비가 만들어지고 호스트가 열린다
            DevAccess.GateComponent(this);
        }

        private void Start()
        {
            // Steam 전송이 아니어도 패널은 등록한다. 전송을 바꾸고 다시 켰을 때
            // 패널이 없으면 왜 없는지 알기 어렵다
            // 방 생성, 참가, 초대, 나가기는 정식 로비 UI에서 제공한다

            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return;
            }

            // 패널이 기본으로 꺼져 있어 화면에 안내가 없다. 로그로 한 번 남긴다
            Debug.Log($"[SteamLobbyHUD] Steam 단축키 — [{_createLobbyKey}] 방 만들기 · " +
                      $"[{_joinLobbyKey}] 클립보드 로비 ID로 참가 · [{_startHostKey}] 호스트 시작. " +
                      "나머지 조작은 브라우저 개발 패널에 있다", this);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void Update()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return;
            }

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            if (Input.GetKeyDown(_createLobbyKey))
            {
                Debug.Log($"[SteamLobbyHUD] [{_createLobbyKey}] 방 만들기", this);
                session.CreateLobby();
            }

            if (Input.GetKeyDown(_joinLobbyKey))
            {
                JoinFromClipboard(session);
            }

            if (Input.GetKeyDown(_startHostKey))
            {
                Debug.Log($"[SteamLobbyHUD] [{_startHostKey}] 호스트 시작 요청: " +
                          $"단계={session.State}, 가능={session.CanStartHost}", this);
                session.StartHost();
            }
        }

        /****************************************
        *                Panel
        ****************************************/

        /// <summary>
        /// 브라우저 개발 패널에 지금 상태와 조작을 적는다 -
        /// 예전에는 게임 화면 왼쪽에 고정 크기로 겹쳐 그려 로비 UI와 파티창이 서로를 덮었다
        /// </summary>
        private void Build(DevPanelBuilder builder)
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                builder.Label("지금은 Steam 전송이 아니다 (KCP)");
                return;
            }

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            SteamLobbyService       lobby   = SteamLobbyService.Instance;

            if ((session == null) || (lobby == null))
            {
                builder.Label("세션 구성이 씬에 없다");
                return;
            }

            builder.Label($"단계: {session.State}");

            if (!lobby.IsInLobby)
            {
                builder.Label("로비: 없음");
                builder.Button("create", $"방 만들기 ({_createLobbyKey})");
                builder.Text("joinid", "로비 ID로 참가", _joinLobbyId);
                builder.Button("join", "참가");
                return;
            }

            builder.Label($"로비: {lobby.LobbyId}");
            builder.Label($"소유자: {(lobby.IsOwner ? "나" : lobby.OwnerId.ToString())}");
            builder.Label($"인원: {lobby.Members.Count} / {SteamLobbyService.MAX_MEMBERS}");

            if (lobby.IsOverlayEnabled)
            {
                builder.Button("invite", "친구 초대 (오버레이)");
            }
            else
            {
                builder.Label("오버레이 없음");
                builder.Text("friendid", "SteamID로 초대", _inviteSteamId);
                builder.Button("sendinvite", "초대 보내기");
            }

            if (session.CanStartHost)
            {
                builder.Button("starthost", $"호스트 시작 ({_startHostKey})");
            }
            else if (session.State == SteamSessionState.Gathering)
            {
                builder.Label(lobby.IsOwner ? "인원이 덜 모였다" : "소유자만 시작할 수 있다");
            }

            builder.Button("leave", "방 나가기");
        }

        private void Handle(DevCommand command)
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            switch (command.Control)
            {
                case "create":
                    session.CreateLobby();
                    return;

                case "joinid":
                    _joinLobbyId = command.Value;
                    return;

                case "join":
                    JoinById(session);
                    return;

                case "invite":
                    session.OpenInviteOverlay();
                    return;

                case "friendid":
                    _inviteSteamId = command.Value;
                    return;

                case "sendinvite":
                    SendInvite(session);
                    return;

                case "starthost":
                    session.StartHost();
                    return;

                case "leave":
                    session.LeaveLobby();
                    return;
            }
        }

        /// <summary> 클립보드에 복사해 둔 로비 ID로 바로 참가한다. 3대를 오가며 손으로 옮겨 적지 않기 위한 것이다 </summary>
        private void JoinFromClipboard(SteamSessionCoordinator session)
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            string trimmed   = (clipboard == null) ? string.Empty : clipboard.Trim();

            if (!ulong.TryParse(trimmed, out ulong lobbyId))
            {
                Debug.LogWarning($"[SteamLobbyHUD] 클립보드에 로비 ID가 없다: {trimmed}. " +
                                 "호스트의 [로비 ID 복사]로 받은 18자리 값을 복사해 둘 것", this);
                return;
            }

            // 패널의 입력칸도 맞춘다. 무엇으로 들어갔는지 보이게 한다
            _joinLobbyId = trimmed;

            Debug.Log($"[SteamLobbyHUD] [{_joinLobbyKey}] 로비 {lobbyId}에 참가한다", this);
            session.JoinLobby(lobbyId);
        }

        private void JoinById(SteamSessionCoordinator session)
        {
            if (!ulong.TryParse(_joinLobbyId, out ulong lobbyId))
            {
                Debug.LogWarning($"[SteamLobbyHUD] 로비 ID가 숫자가 아니다: {_joinLobbyId}", this);
                return;
            }

            session.JoinLobby(lobbyId);
        }

        private void SendInvite(SteamSessionCoordinator session)
        {
            if (!ulong.TryParse(_inviteSteamId, out ulong friendId))
            {
                Debug.LogWarning($"[SteamLobbyHUD] SteamID가 숫자가 아니다: {_inviteSteamId}", this);
                return;
            }

            session.InviteUser(friendId);
        }

#endif
    }
}
