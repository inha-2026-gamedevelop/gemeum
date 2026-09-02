// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 개발 KCP 경로의 신원 소스. 클라이언트가 보낸 값을 그대로 믿는다.
    /// 검증이 없으므로 에디터와 개발 빌드 밖에는 존재하지 않는다
    /// </summary>
    public class DevIdentitySource : MonoBehaviour, IPlayerIdentitySource
    {
        /****************************************
        *              Constants
        ****************************************/

        // 호스트는 connectionId가 항상 0이므로 이 값이 재접속에도 유지된다
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
                // 호스트의 로컬 연결은 connectionId가 항상 0이라 이 값이 재접속에도 유지된다
                if (conn.connectionId == NetworkConnection.LocalConnectionId)
                {
                    id     = new PlayerPlatformId(HOST_FALLBACK_ID);
                    reason = null;
                    return true;
                }

                // 원격은 다르다. connectionId가 재접속마다 새로 발급되어 신원으로 쓸 수 없다.
                // 붙는 것 자체는 막지 않지만 자리와 역할 복구가 동작하지 않으므로 크게 알린다
                Debug.LogWarning(
                    $"[DevIdentitySource] connectionId={conn.connectionId}에 -platformid가 없다. "
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
