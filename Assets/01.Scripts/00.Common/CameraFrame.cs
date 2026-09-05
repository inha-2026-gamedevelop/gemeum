// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 카메라가 매 프레임 밀어 넣는 화면 기준 값. 오브젝트가 아니라 값 하나만 건넨다.
    /// 이동을 카메라 기준으로 바꾸려면 02.Combat이 카메라의 yaw를 알아야 하는데,
    /// 02.Combat이 05.Art를 참조하는 것은 의존 방향(04-architecture 8.1) 위반이다.
    /// 로케이터를 하나 더 만들지 않은 이유는 <b>카메라 각도가 갈아 끼울 구현체가 아니라 그냥 숫자</b>여서다 -
    /// ICombatFeedback, IBarrierState의 로케이터는 구현이 통째로 갈릴 수 있는 것에 쓰는 장치다.
    /// 카메라가 없으면 Yaw는 0이라 입력 축이 그대로 월드 방향이 되어 지금과 똑같이 동작하고,
    /// 카메라를 붙이면 저절로 살아난다
    /// </summary>
    public static class CameraFrame
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 화면 기준이 되는 Y축 각도(도). 카메라 리그가 LateUpdate에서 민다 </summary>
        public static float Yaw { get; set; } = 0f;

        /// <summary>
        /// 카메라 회전 입력을 받아도 되는지. 마우스를 쓰는 UI가 열릴 때 UI가 끈다.
        /// <b>UI가 끄고 카메라가 읽는 방향이라</b> 05.Art가 06.UI를 참조하지 않는다
        /// </summary>
        public static bool InputEnabled { get; set; } = true;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 입력 축(x = 좌우, y = 앞뒤)을 화면 기준 월드 평면 방향으로 돌린다.
        /// 결과의 x는 월드 X, y는 월드 Z다 - 파트 전체가 평면 방향을 이 규약으로 쓴다.
        /// 회전은 크기를 보존하므로 아날로그 입력의 세기가 그대로 남고,
        /// 서버의 ClampMagnitude 검증도 지금과 같은 값을 본다
        /// </summary>
        public static Vector2 ToWorld(Vector2 input)
        {
            // Yaw가 0이면 sin 0, cos 1이라 입력이 그대로 나온다. 카메라가 없는 씬의 동작이 여기서 보장된다
            float radians = Yaw * Mathf.Deg2Rad;
            float sin     = Mathf.Sin(radians);
            float cos     = Mathf.Cos(radians);

            // Quaternion.Euler(0, Yaw, 0) * (x, 0, y)를 평면 성분만 편 것이다
            return new Vector2(
                (input.x * cos) + (input.y * sin),
                (input.y * cos) - (input.x * sin));
        }

        /// <summary>
        /// 도메인 리로드를 끈 상태에서는 정적 값이 플레이 세션 사이에 남는다.
        /// 지난 판의 카메라 각도가 남으면 카메라가 붙기 전 첫 프레임의 이동이 엉뚱한 쪽으로 나간다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Yaw          = 0f;
            InputEnabled = true;
        }
    }
}
