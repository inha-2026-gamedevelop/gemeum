// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary>
    /// 접속 요청. 이 형식은 2단계에서 확정하고 3단계(Steam)에서 바꾸지 않는다.
    /// Steam 경로에서도 같은 필드를 채우며, 신원 검증만 소스가 달라진다
    /// </summary>
    public struct AuthRequestMessage : NetworkMessage
    {
        /// <summary> 클라이언트가 주장하는 신원. 신뢰 여부는 IPlayerIdentitySource가 정한다 </summary>
        public ulong PlatformId;

        /// <summary> 빌드 식별자. 다르면 거부한다, 프로토콜이 어긋난 채로 붙으면 증상이 늦게 드러난다 </summary>
        public string Build;

        /// <summary> Steam 로비 소유자가 게시한 1회용 값. 개발 경로에서는 비어 있다 </summary>
        public string Nonce;

        /// <summary> 호스트 선출 회차. 오래된 후보 보고서를 걸러내는 데 쓴다, 개발 경로에서는 0이다 </summary>
        public uint Epoch;
    }

    /// <summary> 접속 응답. 거부 사유를 코드로 주어 클라이언트가 문구를 정하게 한다 </summary>
    public struct AuthResponseMessage : NetworkMessage
    {
        public AuthResultCode Code;
        public string         Reason;
    }

    /// <summary> 접속 거부 사유. 직렬화되므로 명시값을 유지하고 재배열하지 않는다 </summary>
    public enum AuthResultCode : byte
    {
        Accepted        = 0,
        BuildMismatch   = 1, // 빌드가 다르다
        InvalidIdentity = 2, // 신원을 정할 수 없다
        AdmissionLocked = 3, // 런이 진행 중이라 새 사람을 받지 않는다
        DuplicateId     = 4, // 같은 신원이 이미 붙어 있다
        LobbyFull       = 5, // 정원이 찼다
        NotLobbyMember  = 6, // Steam 로비 멤버가 아니다
        StaleSession    = 7, // 세션 nonce가 다르다, 이전 호스트의 접속 정보로 보인다
    }
}
