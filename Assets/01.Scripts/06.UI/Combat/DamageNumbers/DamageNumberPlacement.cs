// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 데미지 숫자의 화면 배치 계산(COMBAT_STYLE_DESIGN S10 위치·가독성). 좌표는 숫자 층의 로컬(1080p 기준 단위)이다
    /// 원점은 게임 화면 중앙, 곧 조준점이다
    /// </summary>
    public static class DamageNumberPlacement
    {
        /****************************************
        *              Constants
        ****************************************/

        // 중앙에 정확히 떨어진 숫자는 밀어낼 방향이 없어 위로 보낸다
        private const float CENTER_EPSILON = 0.0001f;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 조준점 보호 반경 안의 점을 반경 가장자리로 밀어낸다. 밖이면 그대로 </summary>
        public static Vector2 PushOutOfCenter(Vector2 point, float radius)
        {
            if (radius <= 0f)
            {
                return point;
            }

            float distance = point.magnitude;

            if (distance >= radius)
            {
                return point;
            }

            if (distance <= CENTER_EPSILON)
            {
                return new Vector2(0f, radius);
            }

            return point * (radius / distance);
        }

        /// <summary> 수명 비율(0~1)에 따른 상승 거리. 처음에 빠르고 끝에서 느려진다 </summary>
        public static float Rise(float normalizedAge, float risePixels)
        {
            float t = Mathf.Clamp01(normalizedAge);
            float easeOut = 1f - ((1f - t) * (1f - t));

            return risePixels * easeOut;
        }

        /// <summary> 수명 비율에 따른 불투명도. fadeStart 전까지 1, 이후 0으로 </summary>
        public static float Alpha(float normalizedAge, float fadeStart)
        {
            float t     = Mathf.Clamp01(normalizedAge);
            float start = Mathf.Clamp01(fadeStart);

            if ((t <= start) || (start >= 1f))
            {
                return (t >= 1f) ? 0f : 1f;
            }

            return 1f - ((t - start) / (1f - start));
        }
    }
}
