// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> Steam 경로의 신원 소스. conn.address의 SteamID64를 쓴다 </summary>
    public class SteamIdentitySource : MonoBehaviour, IPlayerIdentitySource
    {
        /****************************************
        *              Properties
        ****************************************/

        public string Label => "Steam";

        /****************************************
        *              Public API
        ****************************************/

        public bool TryResolve(NetworkConnectionToClient conn, ulong claimedId, out PlayerPlatformId id, out string reason)
        {
            id     = PlayerPlatformId.None;
            reason = null;

            if (conn == null)
            {
                reason = "연결이 없다";
                return false;
            }

            if (conn.connectionId == NetworkConnection.LocalConnectionId)
            {
                id = new PlayerPlatformId(SteamLocalUser.TryGetSteamId(out ulong localId) ? localId : 1UL);
                return true;
            }

            if (!PlayerPlatformId.TryParse(conn.address, out id))
            {
                reason = $"conn.address가 SteamID64가 아니다: {conn.address}";
                return false;
            }

            if (claimedId == 0UL)
            {
                id     = PlayerPlatformId.None;
                reason = "클라이언트가 자기 SteamID를 보내지 않았다, Steam 초기화 실패로 보인다";
                return false;
            }

            if (claimedId != id.Value)
            {
                id     = PlayerPlatformId.None;
                reason = $"인증 메시지의 SteamID가 접속 주소와 다르다: 주장={claimedId}, 주소={conn.address}";
                return false;
            }

            return true;
        }
    }
}
