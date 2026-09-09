// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> Steam 로비를 손으로 돌려 보기 위한 개발용 오버레이 </summary>
    public class SteamLobbyHUD : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *              Constants
        ****************************************/

        private const int WIDTH    = 260;
        private const int HEIGHT   = 460;
        private const int OFFSET_X = 10;
        private const int OFFSET_Y = 220;

        /****************************************
        *                Fields
        ****************************************/

        // PC 3대를 각자 조작해야 해서 마우스로 패널을 누르는 것보다 단축키가 빠르다.
        //
        // F1~F12는 전부 차 있다. 코드 기본값이 KeyCode.None이고 씬과 프리팹에 값이 들어 있는
        // 키(GeumeumNetworkManager의 _devStartHostKey=F1, _devAbandonRunKey=F3)가 있어
        // 코드만 훑으면 비어 보인다. 새 키를 잡기 전에 PartyDebugOverlay의 G_KeyHelp와
        // 프리팹 직렬화 값을 함께 볼 것. 그래서 네 키 모두 F키 밖 이동키 무리로 모았다
        [Tooltip("로비 패널을 껐다 켠다. 기본은 꺼짐이라 평소 화면을 가리지 않는다")]
        [SerializeField] private KeyCode _togglePanelKey = KeyCode.PageUp;

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

        // 기본은 꺼짐. 플레이 화면을 가리지 않게 두고 필요할 때만 연다
        private bool _isPanelVisible = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return;
            }

            // 패널이 기본으로 꺼져 있어 화면에 안내가 없다. 로그로 한 번 남긴다
            Debug.Log($"[SteamLobbyHUD] Steam 단축키 — [{_togglePanelKey}] 패널 · " +
                      $"[{_createLobbyKey}] 방 만들기 · [{_joinLobbyKey}] 클립보드 로비 ID로 참가 · " +
                      $"[{_startHostKey}] 호스트 시작", this);
        }

        private void Update()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return;
            }

            if (Input.GetKeyDown(_togglePanelKey))
            {
                _isPanelVisible = !_isPanelVisible;
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

        private void OnGUI()
        {
            if ((!_isPanelVisible) ||
                (!SteamTransportSelector.IsSteamTransport(Transport.active)))
            {
                return;
            }

            GUILayout.BeginArea(new Rect(OFFSET_X, OFFSET_Y, WIDTH, HEIGHT));
            GUILayout.Label("Steam 로비 (개발용)");
            GUILayout.Label($"[{_togglePanelKey}] 패널  [{_createLobbyKey}] 방 만들기");
            GUILayout.Label($"[{_joinLobbyKey}] 참가  [{_startHostKey}] 호스트 시작");

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            SteamLobbyService       lobby   = SteamLobbyService.Instance;

            if ((session == null) || (lobby == null))
            {
                GUILayout.Label("세션 구성이 씬에 없다");
                GUILayout.EndArea();
                return;
            }

            DrawStatus(session, lobby);
            DrawButtons(session, lobby);

            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void DrawStatus(SteamSessionCoordinator session, SteamLobbyService lobby)
        {
            GUILayout.Label($"단계: {session.State}");

            if (!lobby.IsInLobby)
            {
                GUILayout.Label("로비: 없음");
                return;
            }

            GUILayout.Label($"로비: {lobby.LobbyId}");
            GUILayout.Label($"소유자: {(lobby.IsOwner ? "나" : lobby.OwnerId.ToString())}");
            GUILayout.Label($"인원: {lobby.Members.Count} / {SteamLobbyService.MAX_MEMBERS}");
        }

        private void DrawButtons(SteamSessionCoordinator session, SteamLobbyService lobby)
        {
            if (!lobby.IsInLobby)
            {
                if (GUILayout.Button("방 만들기"))
                {
                    session.CreateLobby();
                }

                DrawJoinById(session);
                return;
            }

            if (lobby.IsOverlayEnabled)
            {
                if (GUILayout.Button("친구 초대"))
                {
                    session.OpenInviteOverlay();
                }
            }
            else
            {
                GUILayout.Label("오버레이 없음");

                if (GUILayout.Button("로비 ID 복사"))
                {
                    GUIUtility.systemCopyBuffer = lobby.LobbyId.ToString();
                }

                DrawDirectInvite(session);
            }

            GUI.enabled = session.CanStartHost;
            if (GUILayout.Button("호스트 시작"))
            {
                session.StartHost();
            }

            GUI.enabled = true;

            if ((!session.CanStartHost) && (session.State == SteamSessionState.Gathering))
            {
                GUILayout.Label(lobby.IsOwner ? "인원이 덜 모였다" : "소유자만 시작할 수 있다");
            }

            if (GUILayout.Button("방 나가기"))
            {
                session.LeaveLobby();
            }
        }

        private void DrawDirectInvite(SteamSessionCoordinator session)
        {
            GUILayout.Label("SteamID로 초대");
            _inviteSteamId = GUILayout.TextField(_inviteSteamId);

            GUI.enabled = ulong.TryParse(_inviteSteamId, out ulong friendId);
            if (GUILayout.Button("초대 보내기"))
            {
                session.InviteUser(friendId);
            }

            GUI.enabled = true;
        }

        private void DrawJoinById(SteamSessionCoordinator session)
        {
            GUILayout.Label("로비 ID로 참가");
            _joinLobbyId = GUILayout.TextField(_joinLobbyId);

            GUI.enabled = ulong.TryParse(_joinLobbyId, out ulong lobbyId);
            if (GUILayout.Button("참가"))
            {
                session.JoinLobby(lobbyId);
            }

            GUI.enabled = true;

            if (GUILayout.Button("붙여넣기"))
            {
                _joinLobbyId = GUIUtility.systemCopyBuffer;
            }
        }

        /// <summary>
        /// 클립보드에 복사해 둔 로비 ID로 바로 참가한다.
        /// 호스트가 [로비 ID 복사]로 넘긴 값을 클라이언트가 붙여넣기 없이 쓰는 경로다
        /// </summary>
        private void JoinFromClipboard(SteamSessionCoordinator session)
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            string trimmed   = (clipboard == null) ? string.Empty : clipboard.Trim();

            if (!ulong.TryParse(trimmed, out ulong lobbyId))
            {
                Debug.LogWarning($"[SteamLobbyHUD] 클립보드에 로비 ID가 없다: \"{trimmed}\". " +
                                 "호스트의 [로비 ID 복사]로 받은 18자리 값을 복사해 둘 것", this);
                return;
            }

            // 패널을 열었을 때 무엇으로 들어갔는지 보이도록 입력칸도 맞춘다
            _joinLobbyId = trimmed;

            Debug.Log($"[SteamLobbyHUD] [{_joinLobbyKey}] 로비 {lobbyId}에 참가한다", this);
            session.JoinLobby(lobbyId);
        }

#endif
    }
}
