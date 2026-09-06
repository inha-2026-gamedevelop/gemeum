// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 카메라가 매 프레임 밀어 넣는 화면 기준 값. 오브젝트가 아니라 값 하나만 건넨다.
    /// 이동을 카메라 기준으로 바꾸려면 02.Combat이 카메라의 yaw를 알아야 하는데,
    /// 02.Combat이 05.Art를 참조하는 것은 의존 방향 위반이다.
    /// 로케이터를 하나 더 만들지 않은 이유는 카메라 각도가 갈아 끼울 구현체가 아니라 그냥 숫자여서다 -
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
        /// UI가 끄고 카메라가 읽는 방향이라 05.Art가 06.UI를 참조하지 않는다
        /// </summary>
        public static bool InputEnabled { get; set; } = true;

        /// <summary>
        /// 락온이 걸려 있는지. 전투(LockOnController)가 밀고 카메라가 읽는다 -
        /// Yaw와 반대 방향이지만 같은 원칙이다. 카메라가 전투를 참조하지 않으므로
        /// 락온이 없는 씬(캐릭터 미리보기 등)에서도 리그가 그대로 돈다
        /// </summary>
        public static bool LockOnActive { get; set; } = false;

        /// <summary> 락온 대상의 조준점(월드). LockOnActive가 true일 때만 유효하다 </summary>
        public static Vector3 LockOnPoint { get; set; } = Vector3.zero;

        /// <summary>
        /// 락온 중 쓸 붐 길이(m). 대상마다 다르다 - 잡귀는 기본 거리, 보스 몸통은 꼬리 펼침이
        /// 프레임에 들어와야 해서 더 멀다. 안 보이면 싸울 수 없으므로 연출이 아니라 조작이다
        /// </summary>
        public static float LockOnDistance { get; set; } = 0f;

        /// <summary>
        /// 락온 상태를 한 번에 밀어 넣는다. 세 값을 따로 쓰면 한 프레임 어긋난 조합이 나온다 -
        /// 대상은 바뀌었는데 거리가 이전 값이면 카메라가 한 프레임 엉뚱한 길이로 선다
        /// </summary>
        public static void SetLockOn(bool active, Vector3 point, float distance)
        {
            LockOnActive   = active;
            LockOnPoint    = point;
            LockOnDistance = distance;
        }

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

            SetLockOn(false, Vector3.zero, 0f);
        }
    }
}
