// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 개발 KCP 경로의 신원 소스. 클라이언트가 보낸 값을 그대로 믿는다 </summary>
    public class DevIdentitySource : MonoBehaviour, IPlayerIdentitySource
    {
        /****************************************
        *              Constants
        ****************************************/

        // 호스트 연결의 대체 ID
        private const ulong HOST_FALLBACK_ID = 1UL;

        /****************************************
        *              Properties
        ****************************************/

        public string Label => "Dev";

        /****************************************
        *              Public API
        ****************************************/

        public bool TryResolve(NetworkConnectionToClient conn, ulong claimedId, out PlayerPlatformId id, out string reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (claimedId == 0UL)
            {
                if (conn.connectionId == NetworkConnection.LocalConnectionId)
                {
                    id     = new PlayerPlatformId(HOST_FALLBACK_ID);
                    reason = null;
                    return true;
                }

                Debug.LogWarning(
                    $"[DevIdentitySource] {NetworkPeerLabel.Describe(conn)}에 -platformid가 없다. "
                    + "재접속하면 다른 사람으로 취급되어 자리와 역할이 복구되지 않는다. "
                    + "실행 인자에 -platformid <숫자>를 줄 것", this);

                id     = new PlayerPlatformId((ulong) (conn.connectionId + 1));
                reason = null;
                return true;
            }

            id     = new PlayerPlatformId(claimedId);
            reason = null;
            return true;
#else
            id     = PlayerPlatformId.None;
            reason = "개발 신원 소스는 출시 빌드에서 쓸 수 없다";
            return false;
#endif
        }
    }
}
