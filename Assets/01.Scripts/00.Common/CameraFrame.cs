// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 카메라가 매 프레임 밀어 넣는 화면 기준 값 </summary>
    public static class CameraFrame
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 화면 기준이 되는 Y축 각도(도). 카메라 리그가 LateUpdate에서 민다 </summary>
        public static float Yaw { get; set; } = 0f;

        /// <summary> 카메라 회전 입력을 받아도 되는지. UI가 열릴 때 UI가 끈다 </summary>
        public static bool InputEnabled { get; set; } = true;

        /// <summary>
        /// 관리 창이 얼마나 열려 있는지(0 닫힘 ~ 1 열림). UI가 밀고 카메라가 읽는다
        /// 카메라는 이 값만큼 측면으로 비켜서 캐릭터를 창 반대쪽으로 보낸다
        /// </summary>
        public static float MenuRevealProgress { get; set; } = 0f;

        /// <summary> 락온이 걸려 있는지. 전투(LockOnController)가 밀고 카메라가 읽는다 </summary>
        public static bool LockOnActive { get; set; } = false;

        /// <summary> 락온 대상의 조준점(월드). LockOnActive가 true일 때만 유효하다 </summary>
        public static Vector3 LockOnPoint { get; set; } = Vector3.zero;

        /// <summary> 락온 중 쓸 붐 길이(m). 대상마다 다르다 </summary>
        public static float LockOnDistance { get; set; } = 0f;

        /// <summary>
        /// 화면이 보고 있는 평면 방향. x가 월드 X, y가 월드 Z다
        /// 마우스가 카메라를 돌리므로 이 값이 곧 플레이어가 조준하고 있는 방향이다
        /// </summary>
        public static Vector2 AimDirection => ToWorld(Vector2.up);

        /// <summary> 락온 상태(활성, 조준점, 거리)를 한 번에 밀어 넣는다 </summary>
        public static void SetLockOn(bool active, Vector3 point, float distance)
        {
            LockOnActive   = active;
            LockOnPoint    = point;
            LockOnDistance = distance;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 입력 축(x = 좌우, y = 앞뒤)을 화면 기준 월드 평면 방향으로 돌린다. 결과의 x는 월드 X, y는 월드 Z다 </summary>
        public static Vector2 ToWorld(Vector2 input)
        {
            float radians = Yaw * Mathf.Deg2Rad;
            float sin     = Mathf.Sin(radians);
            float cos     = Mathf.Cos(radians);

            return new Vector2(
                (input.x * cos) + (input.y * sin),
                (input.y * cos) - (input.x * sin));
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Yaw                = 0f;
            InputEnabled       = true;
            MenuRevealProgress = 0f;

            SetLockOn(false, Vector3.zero, 0f);
        }
    }
}
