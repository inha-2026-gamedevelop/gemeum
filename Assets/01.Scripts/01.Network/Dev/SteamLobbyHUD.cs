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
        private const int HEIGHT   = 420;
        private const int OFFSET_X = 10;
        private const int OFFSET_Y = 220;

        // 참가할 로비 ID 입력
        private string _joinLobbyId = string.Empty;

        // 초대할 친구의 SteamID64
        private string _inviteSteamId = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnGUI()
        {
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

#endif
    }
}
