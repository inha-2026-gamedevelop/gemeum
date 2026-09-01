namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 측정 도구 계약값. 게임 결과에 영향을 주지 않는다 </summary>
        public static class Diagnostics
        {
            // 씬 로드 직후 몇 프레임은 수백 ms가 나와 평균을 통째로 망친다
            public const int WARMUP_FRAMES = 60;

            public const float AUTOSAVE_INTERVAL_SECONDS = 60f;   // 비정상 종료로 측정을 통째로 잃지 않도록
            public const float OVERLAY_REFRESH_SECONDS   = 0.25f; // 매 프레임 문자열을 만들면 그 자체가 부하다

            // 프레임 시간을 표본으로 쌓지 않고 히스토그램에 누적한다.
            // 저장 비용이 측정 시간에 비례해 커지면 측정기가 측정 대상을 흔든다
            public const float HISTOGRAM_BUCKET_MS = 0.05f;
            public const float HISTOGRAM_MAX_MS    = 200f;

            // 같은 PC에서 여러 인스턴스를 띄우면 상한 없는 렌더링이 서로 자원을 뺏어
            // 네트워크 비용과 렌더링 경쟁을 분리할 수 없다. 0이면 상한 없음
            public const int TARGET_FRAME_RATE = 60;

            public const float PERCENTILE_MEDIAN = 0.50f;
            public const float PERCENTILE_HIGH   = 0.95f; // 체감 끊김은 평균이 아니라 여기서 드러난다
            public const float PERCENTILE_WORST  = 0.99f;

            public const string FRAME_LOG_PREFIX    = "FrameLog";
            public const float  FRAME_BUDGET_60_MS  = 1000f / 60f; // 예산 초과 프레임 비율 집계 기준
        }
    }
}
