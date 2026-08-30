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

            // 종류마다 다르게 잡는다, 기본값을 그대로 두면 잡귀 22마리가 플레이어와 같은 주기로 3연결에 나간다
            public const float PLAYER_SYNC_INTERVAL = 0.02f; // 조작 대상이라 촘촘하게
            public const float ENEMY_SYNC_INTERVAL  = 0.1f;  // 보간으로 메울 수 있어 성기게

            // 클라가 보낸 입력 시각은 신뢰하지 않는다
            public const float MAX_INPUT_DELTA_TIME  = 0.1f;  // 입력 하나로 진행시킬 수 있는 최대 시간(초)
            public const float MAX_INPUT_TIME_BEHIND = 0.5f;  // 이만큼 이상 과거인 입력 시각은 잘라낸다(초)
            public const float MAX_INPUT_TIME_AHEAD  = 0.05f; // 이보다 앞서면 서버 현재 시각으로 클램프(초)

            // syncInterval(상태 전송 주기)과 별개다, 이동 snapshot을 보내는 주기 — 프로토타입 시작값
            public const float PLAYER_INPUT_SEND_INTERVAL = 1f / 30f; // 이동 입력 발신 주기(초)
            public const float PLAYER_INPUT_EXPIRY        = 0.25f;   // 이 이상 새 입력이 없으면 0 입력으로 간주(초)
            public const float PLAYER_RECONCILE_THRESHOLD = 0.1f;    // 예측 위치와 서버 스냅샷의 오차 허용치(m)

            // 서버가 위치를 움직이지 않는 구간에도 스냅샷이 끊기면 소유 클라이언트의 재조정 ACK가
            // 같이 끊겨 예측 위치가 되돌아올 방법이 없고 미확인 입력도 비워지지 않는다
            public const float PLAYER_SNAPSHOT_IDLE_INTERVAL = 0.2f; // 정지 구간의 스냅샷 최소 송신 주기(초)
            public const int   PLAYER_MAX_PENDING_INPUTS     = 64;   // 재조정에 다시 적용할 미확인 입력 상한(개)

            // AuthorityTickScheduler 등록 순서, 숫자가 작을수록 먼저 실행된다
            public const int TICK_PRIORITY_MOVEMENT   = 0;
            public const int TICK_PRIORITY_AI         = 100;
            public const int TICK_PRIORITY_PROJECTILE = 200;
            public const int TICK_PRIORITY_ABILITY    = 300;
            public const int TICK_PRIORITY_STATUS     = 400; // 자원 회복, 그 틱의 소모가 모두 반영된 뒤에 돈다
        }
    }
}
