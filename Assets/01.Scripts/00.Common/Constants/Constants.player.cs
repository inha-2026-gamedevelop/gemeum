namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 플레이어 입력 · 판정 계약값. 이동 속도 같은 밸런스 수치는 CombatConfigSO에 있다 </summary>
        public static class Player
        {
            // InputSystem_Actions.inputactions의 액션 맵 · 액션 이름
            public const string ACTION_MAP      = "Player";
            public const string ACTION_MOVE     = "Move";
            public const string ACTION_LOOK     = "Look";
            public const string ACTION_ATTACK   = "Attack";
            public const string ACTION_PARRY    = "Parry";
            public const string ACTION_ROLL     = "Roll";
            public const string ACTION_SPRINT   = "Sprint";
            public const string ACTION_INTERACT = "Interact";

            public const float MOVE_INPUT_EPSILON = 0.01f; // 스틱 드리프트로 이동 입력이 계속 나가는 것을 막는다
            public const float GROUND_CHECK_EXTRA = 0.05f; // 접지 판정 레이 여유 길이(m)
            public const int   PARTY_SIZE         = 3;     // 런 고정 인원
        }
    }
}
