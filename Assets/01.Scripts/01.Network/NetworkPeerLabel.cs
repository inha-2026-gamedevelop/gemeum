// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버 로그에 사람을 적는 방법 한 곳
    ///
    /// connectionId만 적으면 3인 테스트에서 1번이 누구였는지 매 판 다시 맞춰야 한다
    /// 이름을 알 수 있으면 이름을 앞에 두고, connectionId는 뒤에 남긴다 -
    /// 이름은 같을 수 있고 Mirror 로그와 맞춰 볼 때는 번호가 필요하다
    ///
    /// 접속, 인증 시점에는 아직 플레이어 오브젝트가 없어 이름을 알 수 없다
    /// 그때는 번호만 적힌다
    /// </summary>
    public static class NetworkPeerLabel
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 연결 하나를 사람이 읽는 한 토막으로 적는다 </summary>
        public static string Describe(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                return "연결 없음";
            }

            string name = ResolveName(conn);

            return string.IsNullOrWhiteSpace(name)
                ? $"연결 {conn.connectionId}"
                : $"{name}(연결 {conn.connectionId})";
        }

        /// <summary> 연결 객체 없이 번호만 들고 있는 자리에서 쓴다 </summary>
        public static string Describe(int connectionId)
        {
            if ((NetworkServer.connections != null)
                && (NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn)))
            {
                return Describe(conn);
            }

            return $"연결 {connectionId}";
        }

        private static string ResolveName(NetworkConnectionToClient conn)
        {
            NetworkIdentity identity = conn.identity;

            if ((identity != null)
                && (identity.TryGetComponent(out IDisplayNamed named))
                && (!string.IsNullOrWhiteSpace(named.DisplayName)))
            {
                return named.DisplayName;
            }

            // 아직 주 오브젝트가 없거나 이름이 안 올라왔으면 이 연결이 소유한 것 중에서 찾는다
            if (conn.owned == null)
            {
                return null;
            }

            foreach (NetworkIdentity owned in conn.owned)
            {
                if ((owned != null)
                    && (owned.TryGetComponent(out IDisplayNamed ownedName))
                    && (!string.IsNullOrWhiteSpace(ownedName.DisplayName)))
                {
                    return ownedName.DisplayName;
                }
            }

            return null;
        }
    }
}
