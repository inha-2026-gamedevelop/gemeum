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
            public const KeyCode KEY_SPRINT    = KeyCode.LeftShift;
            public const KeyCode KEY_ROLL      = KeyCode.C;
            public const KeyCode KEY_INTERACT  = KeyCode.E;
            public const KeyCode KEY_FREE_LOOK = KeyCode.LeftAlt; // -nocursorlock 개발 모드에서 누르는 동안만 카메라가 돈다

            public const int MOUSE_BUTTON_ATTACK = 0;
            public const int MOUSE_BUTTON_PARRY  = 1;

            // 중클릭이 락온인 이유는 우클릭을 세 직업이 이미 나눠 쓰고 있고
            // 인게임 무기 교체가 없어 휠이 비어 있기 때문이다. 소울즈 관습 그대로다
            public const int MOUSE_BUTTON_LOCK_ON = 2;

            // Input.GetAxisRaw("Mouse X")는 픽셀 이동량이 아니라 InputManager.asset의 sensitivity(0.1)가
            // 이미 곱해진 값이다. 카메라 감도를 사양대로 도/픽셀로 적어 두려면 그 배수를 되돌려야 한다.
            // 이 값을 InputManager에서 바꾸면 카메라 회전 속도가 조용히 같이 바뀐다
            public const float MOUSE_AXIS_SENSITIVITY = 0.1f;

            public const float MOVE_INPUT_EPSILON = 0.01f; // 스틱 드리프트로 이동 입력이 계속 나가는 것을 막는다
            public const float GROUND_CHECK_EXTRA = 0.05f; // 접지 판정 레이 여유 길이(m)
            public const int   PARTY_SIZE         = 3;     // 런 고정 인원

            // 부활 지점을 지면에 앉힐 때 쓰는 탐침. 밸런스가 아니라 판정 여유값이다
            public const float REVIVE_PROBE_UP       = 3f;    // 후보 지점 위 이만큼에서 아래로 쏜다(m)
            public const float REVIVE_PROBE_DISTANCE = 12f;   // 아래로 훑는 최대 거리(m)
            public const float REVIVE_GROUND_OFFSET  = 0.05f; // 지면에 박히지 않게 띄우는 높이(m)
        }
    }
}
