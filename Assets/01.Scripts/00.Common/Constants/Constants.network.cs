namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> Mirror 동기화 계약값과 서버가 클라 입력을 검증할 때 쓰는 허용 범위 </summary>
        public static class Network
        {
            public const int MAX_PLAYERS = 3; // 접속 정원

            public const float SERVER_TICK_INTERVAL   = 1f / 60f; // 서버 판정 틱 간격(초)
            public const float RUN_TIME_SYNC_INTERVAL = 0.5f;     // 경과 시간 동기화 주기(초), 클라는 이 사이를 보간한다

            // 클라가 보낸 입력 시각은 신뢰하지 않는다
            public const float MAX_INPUT_DELTA_TIME  = 0.1f;  // 입력 하나로 진행시킬 수 있는 최대 시간(초)
            public const float MAX_INPUT_TIME_BEHIND = 0.5f;  // 이만큼 이상 과거인 입력 시각은 잘라낸다(초)
            public const float MAX_INPUT_TIME_AHEAD  = 0.05f; // 이보다 앞서면 서버 현재 시각으로 클램프(초)
        }
    }
}
