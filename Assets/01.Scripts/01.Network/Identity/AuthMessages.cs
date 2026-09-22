// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> 접속 요청 </summary>
    public struct AuthRequestMessage : NetworkMessage
    {
        /// <summary> 클라이언트가 주장하는 신원 </summary>
        public ulong PlatformId;

        /// <summary> 빌드 식별자 </summary>
        public string Build;

        /// <summary> Steam 로비 소유자가 게시한 1회용 값 </summary>
        public string Nonce;

        /// <summary> 호스트 선출 회차 </summary>
        public uint Epoch;
    }

    /// <summary> 접속 응답 </summary>
    public struct AuthResponseMessage : NetworkMessage
    {
        public AuthResultCode Code;
        public string         Reason;

        /// <summary>인증 직후 서버가 현재 network scene을 보낼 예정이면 true</summary>
        public bool SceneSyncRequired;
    }

    /// <summary> 접속 거부 사유 </summary>
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
