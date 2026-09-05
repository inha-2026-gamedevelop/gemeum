// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary>
    /// Steam 로비를 손으로 돌려 보기 위한 개발용 오버레이. 출시 로비 UI가 아니다.
    /// 3PC 검증에 필요한 조작만 두고, 씬 동선이 정해지면 06.UI의 로비 화면이 이 자리를 대신한다
    /// </summary>
    public class SteamLobbyHUD : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *              Constants
        ****************************************/

        private const int WIDTH    = 260;
        private const int HEIGHT   = 420;
        private const int OFFSET_X = 10;
        private const int OFFSET_Y = 220; // NetworkManagerHUD 아래

        // 참가할 로비 ID 입력. 오버레이를 못 쓸 때 호스트가 불러 주는 값을 받는다
        private string _joinLobbyId = string.Empty;

        // 초대할 친구의 SteamID64. 오버레이 친구 창을 못 여니 직접 받는다
        private string _inviteSteamId = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnGUI()
        {
            // KCP 경로에서는 로비가 없다. 빈 패널을 띄우면 개발 화면만 어지럽다
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return;
            }

            GUILayout.BeginArea(new Rect(OFFSET_X, OFFSET_Y, WIDTH, HEIGHT));
            GUILayout.Label("Steam 로비 (개발용)");

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

            // 오버레이는 Steam이 실행한 프로세스에만 주입된다. 개발 빌드에서는 없는 경우가 많아
            // 왜 눌러도 반응이 없는지 여기서 미리 말해 준다
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

            // 소유자가 아니거나 인원이 덜 찼으면 누를 수 없다. 왜 못 누르는지 이유를 같이 보여 준다
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

        // 오버레이 친구 창 없이 초대를 보낸다. 받는 쪽은 Steam 클라이언트 알림으로 받는다
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

#endif
    }
}
