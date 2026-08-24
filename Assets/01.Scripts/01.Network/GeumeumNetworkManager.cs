// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 호스트 생성 · 접속 · 해제를 관리하는 Mirror NetworkManager. 최대 3인, 플레이어 프리팹 등록과 연결 수명만 담당 </summary>
    public class GeumeumNetworkManager : NetworkManager
    {
        /****************************************
        *              Unity Event
        ****************************************/

        public override void Awake()
        {
            maxConnections = Constants.Network.MAX_PLAYERS; // NetworkServer.Listen이 이 값으로 접속 정원을 강제한다
            base.Awake();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[GeumeumNetworkManager] 접속: connectionId={conn.connectionId}, 현재 인원={numPlayers}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[GeumeumNetworkManager] 해제: connectionId={conn.connectionId}");
            base.OnServerDisconnect(conn);
        }
    }
}
