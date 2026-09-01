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
            // 패링 창이 닫히고 타격이 확정되기까지의 여유(초). 밸런스가 아니라 네트워크 계약값으로,
            // 150ms 지연과 60Hz 서버 틱에서도 판정이 타격보다 먼저 끝나도록 잡은 값이다.
            // 조정할 때는 docs/unity/PARRY_SYSTEM_DESIGN.md 3.1절을 함께 고친다
            public const float PARRY_RESOLUTION_LEAD = 0.18f;

            public const float MAX_INPUT_TIME_BEHIND = 0.5f;  // 이만큼 이상 과거인 입력 시각은 잘라낸다(초)
            public const float MAX_INPUT_TIME_AHEAD  = 0.05f; // 이보다 앞서면 서버 현재 시각으로 클램프(초)

            // 패링 window 전체가 impactTime - 0.50 ~ impactTime - 0.18 구간이라, 이동용 0.5초를 그대로 쓰면
            // clamp 범위가 window를 통째로 덮어 아무것도 제한하지 못한다. 패링은 전용 하한을 쓴다.
            // 조정할 때는 docs/unity/PARRY_SYSTEM_DESIGN.md 3.2절을 함께 고친다
            public const float PARRY_MAX_INPUT_TIME_BEHIND = 0.25f; // rtt가 비정상으로 커도 넘지 못하는 절대 상한(초)
            public const float PARRY_INPUT_TIME_TOLERANCE  = 0.05f; // rtt 흔들림과 서버 틱 여유(초)
            public const float PARRY_MIN_REQUEST_INTERVAL  = 0.05f; // 거부 응답도 이 간격보다 자주 처리하지 않는다(초)

            // syncInterval(상태 전송 주기)과 별개다, 이동 snapshot을 보내는 주기 - 프로토타입 시작값
            public const float PLAYER_INPUT_SEND_INTERVAL = 1f / 30f; // 이동 입력 발신 주기(초)
            public const float PLAYER_INPUT_EXPIRY        = 0.25f;   // 이 이상 새 입력이 없으면 0 입력으로 간주(초)
            // 재조정 3단계. 이 아래는 클라이언트 값을 그대로 보여 주고, 중간 구간은 서서히 서버 쪽으로
            // 수렴시키며, 상한을 넘으면 즉시 서버 위치로 바꾼다. 매 스냅샷마다 순간이동시키면 튄다
            public const float PLAYER_RECONCILE_THRESHOLD   = 0.1f;  // 이 이하 오차는 보정하지 않는다(m)
            public const float PLAYER_RECONCILE_SNAP        = 2f;    // 이 이상 벌어지면 즉시 서버 위치로(m)
            public const float PLAYER_RECONCILE_SMOOTH_TIME = 0.08f; // 중간 구간의 수렴 시간 상수(초)

            // 서버가 위치를 움직이지 않는 구간에도 스냅샷이 끊기면 소유 클라이언트의 재조정 ACK가
            // 같이 끊겨 예측 위치가 되돌아올 방법이 없고 미확인 입력도 비워지지 않는다
            public const float PLAYER_SNAPSHOT_IDLE_INTERVAL = 0.2f; // 정지 구간의 스냅샷 최소 송신 주기(초)
            public const int   PLAYER_MAX_PENDING_INPUTS     = 64;   // 재조정에 다시 적용할 미확인 입력 상한(개)

            // 30Hz 발신, 60Hz 드레인이면 정상적으로는 한두 개만 쌓인다. 조작된 클라가 sequence만
            // 올려가며 폭주 전송해도 서버 큐가 무한히 자라지 않도록 상한을 둔다
            public const int PLAYER_SERVER_INPUT_BUFFER_MAX = 8;

            // AuthorityTickScheduler 등록 순서, 숫자가 작을수록 먼저 실행된다
            public const int TICK_PRIORITY_MOVEMENT   = 0;
            public const int TICK_PRIORITY_AI         = 100;
            public const int TICK_PRIORITY_PROJECTILE = 200;
            public const int TICK_PRIORITY_ABILITY    = 300;
            public const int TICK_PRIORITY_STATUS     = 400; // 자원 회복, 그 틱의 소모가 모두 반영된 뒤에 돈다
        }
    }
}
