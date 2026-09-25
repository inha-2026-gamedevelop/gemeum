// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 잡귀 머리 위 체력 바의 순수 계산 - 비율, 기준 최대 체력, 표시 불투명도, 거리 배율
    /// 표시(EnemyHealthBarLayer)는 이 결과만 화면에 옮긴다
    /// </summary>
    public static class EnemyHealthBarRules
    {
        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 비율을 0~1로 자른다. NaN·무한대는 0으로 본다 </summary>
        public static float ClampRatio(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                return 0f;
            }

            return Mathf.Clamp01(ratio);
        }

        /// <summary> 남은 체력 비율. 최대 체력을 모르면(0 이하·비정상) 0 </summary>
        public static float Ratio(float health, float maxHealth)
        {
            if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || (maxHealth <= 0f))
            {
                return 0f;
            }

            return ClampRatio(health / maxHealth);
        }

        /// <summary>
        /// 비율의 분모. 서버 값(syncedMax)을 알면 그것을, 모르면 이 화면이 본 가장 높은 체력(observedPeak)을 쓴다
        /// 잡귀 최대 체력은 서버에서만 확정되는 값이라 원격 클라이언트에서는 0이다
        /// </summary>
        public static float ReferenceMax(float syncedMax, float observedPeak)
        {
            if ((!float.IsNaN(syncedMax)) && (!float.IsInfinity(syncedMax)) && (syncedMax > 0f))
            {
                return syncedMax;
            }

            if (float.IsNaN(observedPeak) || float.IsInfinity(observedPeak) || (observedPeak <= 0f))
            {
                return 0f;
            }

            return observedPeak;
        }

        /// <summary> 마지막 피격 뒤 경과에 따른 불투명도. visibleSeconds까지 1, 이후 fadeSeconds 동안 0으로 </summary>
        public static float Alpha(float now, float lastHitTime, float visibleSeconds, float fadeSeconds)
        {
            float elapsed = now - lastHitTime;

            if (elapsed <= visibleSeconds)
            {
                return 1f;
            }

            if (fadeSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - ((elapsed - visibleSeconds) / fadeSeconds));
        }

        /// <summary> 카메라 깊이에 따른 배율. 기준 거리 안이면 1, 밖이면 거리에 반비례하되 minScale 아래로는 줄이지 않는다 </summary>
        public static float DistanceScale(float depth, float referenceDistance, float minScale)
        {
            if ((referenceDistance <= 0f) || (depth <= referenceDistance))
            {
                return 1f;
            }

            float scale = referenceDistance / depth;
            float floor = Mathf.Clamp01(minScale);

            return (scale > floor) ? scale : floor;
        }
    }
}
