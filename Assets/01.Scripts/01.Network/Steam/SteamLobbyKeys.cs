namespace Geumeum.Network
{
    /// <summary>
    /// Steam 로비 데이터 키. 키 이름이 여러 곳에 흩어지면 한쪽만 고쳤을 때
    /// 오타가 컴파일에 걸리지 않고 런타임에 빈 문자열로만 드러나므로 여기 하나로 모은다
    /// </summary>
    public static class SteamLobbyKeys
    {
        /****************************************
        *              Public Data
        ****************************************/

        /// <summary> AppID 480을 공유하는 다른 개발자의 로비를 걸러내는 표식 </summary>
        public const string GAME = "game";

        /// <summary> 로비 데이터 계약 버전. 다르면 서로 다른 형식을 읽게 된다 </summary>
        public const string SCHEMA = "schema";

        /// <summary> 빌드 식별자. 다른 빌드끼리 붙으면 증상이 늦게 드러난다 </summary>
        public const string BUILD = "build";

        /// <summary> 세션 상태. UI와 참가 정책이 읽는다 </summary>
        public const string STATE = "state";

        /// <summary> 선출 회차. 오래된 메시지를 폐기하는 데 쓴다 </summary>
        public const string EPOCH = "epoch";

        /// <summary> 선택된 Mirror 호스트의 SteamID64 문자열 </summary>
        public const string HOST = "host";

        /// <summary> 무작위 세션 값. 이전 호스트의 접속 정보 재사용을 막는다 </summary>
        public const string NONCE = "nonce";

        /// <summary> 성능 측정 계약 버전 </summary>
        public const string PROBE = "probe";

        /****************************************
        *              Member Data
        ****************************************/

        public const string MEMBER_PROBE_STATE   = "probe_state";
        public const string MEMBER_PROBE_EPOCH   = "probe_epoch";
        public const string MEMBER_AUTHORITY_P95 = "authority_p95";
        public const string MEMBER_FRAME_P95     = "frame_p95";
        public const string MEMBER_MEMORY_MB     = "memory_mb";
        public const string MEMBER_VRAM_MB       = "vram_mb";
        public const string MEMBER_PING_LOC      = "ping_loc";

        /****************************************
        *                Values
        ****************************************/

        /// <summary> GAME 키에 넣는 값. 이 값이 아니면 우리 로비가 아니다 </summary>
        public const string GAME_VALUE = "geumeum";

        /// <summary> SCHEMA 키에 넣는 값. 로비 데이터 형식을 바꿀 때만 올린다 </summary>
        public const string SCHEMA_VALUE = "1";

        /// <summary> STATE 키에 들어가는 값. 5장 상태 머신의 이름을 그대로 쓴다 </summary>
        public static class State
        {
            public const string GATHERING  = "gathering";
            public const string MEASURING  = "measuring";
            public const string ELECTING   = "electing";
            public const string CONNECTING = "connecting";
            public const string READY      = "ready";
            public const string IN_GAME    = "in_game";
            public const string RECOVERING = "recovering";
        }
    }
}
