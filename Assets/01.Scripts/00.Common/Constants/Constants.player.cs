// Unity
using UnityEngine;

namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 플레이어 입력, 판정 계약값. 이동 속도 같은 밸런스 수치는 CombatConfigSO에 있다 </summary>
        public static class Player
        {
            // ProjectSettings/InputManager.asset의 축 이름
            public const string AXIS_HORIZONTAL = "Horizontal";
            public const string AXIS_VERTICAL   = "Vertical";
            public const string AXIS_MOUSE_X     = "Mouse X";
            public const string AXIS_MOUSE_Y     = "Mouse Y";
            public const string AXIS_MOUSE_WHEEL = "Mouse ScrollWheel";

            // 키 바인딩, 리바인딩 UI가 생기면 이 값을 기본값으로 쓴다
            public const KeyCode KEY_SPRINT    = KeyCode.LeftShift; // 기본 달리기면 누르는 동안 걷고, 끄면 누르는 동안 달린다
            public const KeyCode KEY_ROLL      = KeyCode.C;
            public const KeyCode KEY_INTERACT  = KeyCode.E;
            public const KeyCode KEY_FREE_LOOK = KeyCode.LeftAlt; // -nocursorlock 개발 모드에서 누르는 동안만 카메라가 돈다
            public const KeyCode KEY_TOGGLE_CURSOR = KeyCode.Escape;  // 누를 때마다 마우스 커서와 카메라 회전을 번갈아 켠다

            public const int MOUSE_BUTTON_ATTACK = 0;
            public const int MOUSE_BUTTON_PARRY  = 1;

            public const int MOUSE_BUTTON_LOCK_ON = 2; // 중클릭

            public const float MOUSE_AXIS_SENSITIVITY = 0.1f; // InputManager.asset의 sensitivity

            public const float MOVE_INPUT_EPSILON = 0.01f; // 스틱 드리프트로 이동 입력이 계속 나가는 것을 막는다
            public const float GROUND_CHECK_EXTRA = 0.05f; // 접지 판정 레이 여유 길이(m)
            public const int   PARTY_SIZE         = 3;     // 런 고정 인원

            public const float REVIVE_PROBE_UP       = 3f;    // 후보 지점 위 이만큼에서 아래로 쏜다(m)
            public const float REVIVE_PROBE_DISTANCE = 12f;   // 아래로 훑는 최대 거리(m)
            public const float REVIVE_GROUND_OFFSET  = 0.05f; // 지면에 박히지 않게 띄우는 높이(m)
        }
    }
}
