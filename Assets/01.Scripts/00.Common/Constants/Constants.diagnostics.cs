namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 측정 도구 계약값. 게임 결과에 영향을 주지 않는다 </summary>
        public static class Diagnostics
        {
            public const int MAX_FRAME_SAMPLES = 500000;

            // 씬 로드 직후 몇 프레임은 수백 ms가 나와 평균을 통째로 망친다
            public const int WARMUP_FRAMES = 60;

            public const float AUTOSAVE_INTERVAL_SECONDS = 60f;   // 비정상 종료로 측정을 통째로 잃지 않도록
            public const float OVERLAY_REFRESH_SECONDS   = 0.25f; // 매 프레임 문자열을 만들면 그 자체가 부하다

            public const float PERCENTILE_MEDIAN = 0.50f;
            public const float PERCENTILE_HIGH   = 0.95f; // 체감 끊김은 평균이 아니라 여기서 드러난다
            public const float PERCENTILE_WORST  = 0.99f;

            public const string FRAME_LOG_PREFIX    = "FrameLog";
            public const float  FRAME_BUDGET_60_MS  = 1000f / 60f; // 예산 초과 프레임 비율 집계 기준
        }
    }
}
