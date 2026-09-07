// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 원격 연결에서 플랫폼 ID를 얻는 방법 </summary>
    public interface IPlayerIdentitySource
    {
        /// <summary> 이 소스가 어느 트랜스포트와 짝인지 </summary>
        string Label { get; }

        /// <summary> 연결의 신원을 정한다 </summary>
        bool TryResolve(NetworkConnectionToClient conn, ulong claimedId, out PlayerPlatformId id, out string reason);
    }
}
