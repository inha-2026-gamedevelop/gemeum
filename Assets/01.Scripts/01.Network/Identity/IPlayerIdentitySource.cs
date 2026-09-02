// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 원격 연결에서 플랫폼 ID를 얻는 방법. 트랜스포트마다 신원의 출처가 달라 여기서 갈린다.
    /// 갈라지는 곳은 트랜스포트와 이 계약 둘뿐이고, 로스터와 인증 검증 순서는 같은 코드가 돈다
    /// </summary>
    public interface IPlayerIdentitySource
    {
        /// <summary> 이 소스가 어느 트랜스포트와 짝인지. 부트 게이트가 짝 검사에 쓴다 </summary>
        string Label { get; }

        /// <summary>
        /// 연결의 신원을 정한다. claimedId는 클라이언트가 인증 메시지에 실어 보낸 값으로
        /// 신뢰 여부는 구현이 정한다. Steam은 무시하고 conn.address를 쓰고, 개발 소스만 그대로 믿는다
        /// </summary>
        bool TryResolve(NetworkConnectionToClient conn, ulong claimedId, out PlayerPlatformId id, out string reason);
    }
}
