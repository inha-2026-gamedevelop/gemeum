namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> Mirror 동기화 계약값과 서버가 클라 입력을 검증할 때 쓰는 허용 범위 </summary>
        public static class Network
        {
            public const int MAX_PLAYERS = 3; // 접속 정원

            public const float RESULT_TO_LOBBY_SECONDS = 5f; // Result 확정 후 Lobby 복귀까지의 시간(초)
            public const float BOSS_TRIAL_LOADOUT_TIMEOUT = 60f; // 전실에서 편성을 기다리는 상한(초), 지나면 빈 편성으로 연다

            public const float SERVER_TICK_INTERVAL   = 1f / 60f; // 서버 판정 틱 간격(초)
            public const float RUN_TIME_SYNC_INTERVAL = 0.5f;     // 경과 시간 동기화 주기(초), 클라는 이 사이를 보간한다

            public const float PLAYER_SYNC_INTERVAL = 0.02f; // 초
            public const float ENEMY_SYNC_INTERVAL  = 0.1f;  // 초

            public const float MAX_INPUT_DELTA_TIME  = 0.1f;  // 입력 하나로 진행시킬 수 있는 최대 시간(초)
            public const float PARRY_RESOLUTION_LEAD = 0.18f; // 패링 창이 닫히고 타격이 확정되기까지의 여유(초)

            public const float MAX_INPUT_TIME_BEHIND = 0.5f;  // 이만큼 이상 과거인 입력 시각은 잘라낸다(초)
            public const float MAX_INPUT_TIME_AHEAD  = 0.05f; // 이보다 앞서면 서버 현재 시각으로 클램프(초)

            public const float PARRY_MAX_INPUT_TIME_BEHIND = 0.25f; // rtt가 비정상으로 커도 넘지 못하는 절대 상한(초)
            public const float PARRY_INPUT_TIME_TOLERANCE  = 0.05f; // rtt 흔들림과 서버 틱 여유(초)
            public const float PARRY_MIN_REQUEST_INTERVAL  = 0.05f; // 거부 응답도 이 간격보다 자주 처리하지 않는다(초)

            public const float PLAYER_INPUT_SEND_INTERVAL = 1f / 30f; // 이동 입력 발신 주기(초)
            public const float PLAYER_INPUT_EXPIRY        = 0.25f;   // 이 이상 새 입력이 없으면 0 입력으로 간주(초)
            public const float PLAYER_RECONCILE_THRESHOLD   = 0.1f;  // 이 이하 오차는 보정하지 않는다(m)
            public const float PLAYER_RECONCILE_SNAP        = 2f;    // 이 이상 벌어지면 즉시 서버 위치로(m)
            public const float PLAYER_RECONCILE_SMOOTH_TIME = 0.08f; // 중간 구간의 수렴 시간 상수(초)

            public const float PLAYER_SNAPSHOT_IDLE_INTERVAL = 0.2f; // 정지 구간의 스냅샷 최소 송신 주기(초)
            public const int   PLAYER_MAX_PENDING_INPUTS     = 64;   // 재조정에 다시 적용할 미확인 입력 상한(개)

            public const int PLAYER_SERVER_INPUT_BUFFER_MAX = 8; // 서버 입력 큐 상한

            // AuthorityTickScheduler 등록 순서, 숫자가 작을수록 먼저 실행된다
            public const int TICK_PRIORITY_MOVEMENT   = 0;
            public const int TICK_PRIORITY_AI         = 100;
            public const int TICK_PRIORITY_PROJECTILE = 200;
            public const int TICK_PRIORITY_ABILITY    = 300;
            public const int TICK_PRIORITY_STATUS     = 400; // 자원 회복
        }
    }
}
