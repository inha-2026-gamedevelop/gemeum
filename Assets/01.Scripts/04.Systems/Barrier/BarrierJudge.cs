// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Systems
{
    /// <summary>
    /// 결계 중심, 반경 보간과 XZ 외부 판정. 순수 C#이라 씬, 네트워크 없이 단위 테스트로 검증한다.
    /// 서버와 클라이언트가 같은 BarrierTransitionState를 넣으면 같은 결과가 나와야 한다
    /// </summary>
    public static class BarrierJudge
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 위치가 결계 밖인지. 높이는 보지 않고 XZ 평면 거리로만 판정한다 </summary>
        public static bool IsOutside(Vector3 worldPosition, in BarrierTransitionState state, double serverTime)
        {
            Vector3 center = GetCenter(state, serverTime);
            float   radius = GetRadius(state, serverTime);

            float dx = worldPosition.x - center.x;
            float dz = worldPosition.z - center.z;
            float sqrDistance = (dx * dx) + (dz * dz);

            return sqrDistance > (radius * radius);
        }

        /// <summary> 보간 중인 현재 중심. 전환 구간 동안 호출마다 값이 다르다 </summary>
        public static Vector3 GetCenter(in BarrierTransitionState state, double serverTime)
        {
            float k = GetInterpolant(state, serverTime);
            return Vector3.Lerp(state.FromCenter, state.TargetCenter, k);
        }

        /// <summary> 보간 중인 현재 반경(m). 전환 구간 동안 호출마다 값이 다르다 </summary>
        public static float GetRadius(in BarrierTransitionState state, double serverTime)
        {
            float k = GetInterpolant(state, serverTime);
            return Mathf.Lerp(state.FromRadius, state.TargetRadius, k);
        }

        /// <summary>
        /// 전환 구간 진행률(0~1)을 SmoothStep으로 완만하게 가공한다.
        /// 중심, 반경이 반드시 같은 k를 쓰도록 이 메서드 하나로 모은다
        /// </summary>
        private static float GetInterpolant(in BarrierTransitionState state, double serverTime)
        {
            double span = state.EndServerTime - state.StartServerTime;

            if (span <= 0.0)
            {
                return 1f;
            }

            double t = (serverTime - state.StartServerTime) / span;
            t = Math.Clamp(t, 0.0, 1.0);

            float tf = (float)t;
            return tf * tf * (3f - (2f * tf)); // SmoothStep01
        }
    }
}
