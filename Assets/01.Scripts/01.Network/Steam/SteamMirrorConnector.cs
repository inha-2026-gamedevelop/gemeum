// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> Steam 로비와 Mirror 연결을 잇는다 </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SteamMirrorConnector : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private NetworkManager _manager;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.GameServerSet += OnGameServerSet;
            lobby.JoinRequested += OnJoinRequested;
        }

        private void OnDisable()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                return;
            }

            lobby.GameServerSet -= OnGameServerSet;
            lobby.JoinRequested -= OnJoinRequested;
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 로비에 세션 값을 게시하고 호스트를 시작한 뒤 접속 신호를 보낸다 </summary>
        public void StartSteamHost()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                Debug.LogError("[SteamMirrorConnector] Steam 경로가 아니다, -steam 인자로 실행할 것", this);
                return;
            }

            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby))
            {
                Debug.LogError("[SteamMirrorConnector] 로비에 들어가 있지 않아 호스트를 열 수 없다", this);
                return;
            }

            if (!lobby.IsOwner)
            {
                Debug.LogError("[SteamMirrorConnector] 로비 소유자만 호스트를 열 수 있다, 공용 데이터를 쓰지 못한다", this);
                return;
            }

            if (!SteamLocalUser.TryGetSteamId(out ulong hostId))
            {
                Debug.LogError("[SteamMirrorConnector] 자기 SteamID를 읽지 못해 호스트를 열 수 없다", this);
                return;
            }

            string nonce = PublishSession(lobby, hostId);

            _manager.StartHost();

            if (!NetworkServer.active)
            {
                Debug.LogError("[SteamMirrorConnector] 호스트 시작에 실패했다, 부트 게이트 로그를 확인할 것", this);
                return;
            }

            lobby.SetGameServer(hostId);
            Debug.Log($"[SteamMirrorConnector] 접속 신호 발송: host={hostId}, nonce={nonce}");
        }

        /// <summary> 로비에 참가한다 </summary>
        public void JoinLobby(ulong lobbyId)
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if (lobby == null)
            {
                Debug.LogError("[SteamMirrorConnector] 로비 서비스가 씬에 없다", this);
                return;
            }

            lobby.JoinLobby(lobbyId);
        }

        /// <summary> 로비 데이터의 host를 보고 접속한다 </summary>
        public void ConnectToPublishedHost()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby))
            {
                return;
            }

            if (!ulong.TryParse(lobby.GetData(SteamLobbyKeys.HOST), out ulong hostId))
            {
                Debug.Log("[SteamMirrorConnector] 아직 게시된 호스트가 없다");
                return;
            }

            ConnectTo(hostId);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnGameServerSet(ulong hostSteamId)
        {
            if (NetworkServer.active)
            {
                return;
            }

            ConnectTo(hostSteamId);
        }

        private void OnJoinRequested(ulong lobbyId)
        {
            if ((NetworkServer.active) || (NetworkClient.active))
            {
                Debug.LogWarning($"[SteamMirrorConnector] 세션 중이라 로비 {lobbyId} 초대를 자동 수락하지 않는다. "
                    + "방 나가기 뒤에 다시 수락할 것", this);
                return;
            }

            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby != null) && (lobby.IsInLobby) && (lobby.LobbyId == lobbyId))
            {
                return;
            }

            JoinLobby(lobbyId);
        }

        private void ConnectTo(ulong hostSteamId)
        {
            if (hostSteamId == 0UL)
            {
                Debug.LogError("[SteamMirrorConnector] 호스트 SteamID가 0이다", this);
                return;
            }

            if (NetworkClient.active)
            {
                Debug.Log("[SteamMirrorConnector] 이미 접속 중이라 접속 신호를 무시한다");
                return;
            }

            _manager.networkAddress = hostSteamId.ToString();
            Debug.Log($"[SteamMirrorConnector] 호스트 {hostSteamId}에 접속한다");
            _manager.StartClient();
        }

        private string PublishSession(SteamLobbyService lobby, ulong hostId)
        {
            string nonce = Guid.NewGuid().ToString("N");

            lobby.SetData(SteamLobbyKeys.HOST,  hostId.ToString());
            lobby.SetData(SteamLobbyKeys.NONCE, nonce);
            lobby.SetData(SteamLobbyKeys.STATE, SteamLobbyKeys.State.CONNECTING);

            Debug.Log($"[SteamMirrorConnector] 세션 게시: host={hostId}, nonce={nonce}");
            return nonce;
        }
    }
}
