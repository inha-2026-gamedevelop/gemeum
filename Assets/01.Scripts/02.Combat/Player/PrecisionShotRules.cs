// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 정밀 사격의 홀드와 조준 입력을 계산하는 순수 규칙 </summary>
    public static class PrecisionShotRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NORMALIZED_DIRECTION_TOLERANCE = 0.01f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 현재 홀드와 세대가 해제 요청을 어떻게 처리할지 정한다 </summary>
        public static PrecisionReleaseDecision ResolveRelease(
            PrecisionShotState state,
            uint activeGeneration,
            uint requestedGeneration,
            double holdStartGameTime,
            double currentGameTime,
            float holdDuration)
        {
            if (((state != PrecisionShotState.Holding) && (state != PrecisionShotState.Ready))
                || (activeGeneration == 0u) || (requestedGeneration != activeGeneration))
            {
                return PrecisionReleaseDecision.Reject;
            }

            if ((!IsFinite(holdStartGameTime)) || (!IsFinite(currentGameTime))
                || (!IsFinite(holdDuration)) || (holdDuration < 0f)
                || (currentGameTime < holdStartGameTime))
            {
                return PrecisionReleaseDecision.Reject;
            }

            return ((currentGameTime - holdStartGameTime) < holdDuration)
                ? PrecisionReleaseDecision.Cancel
                : PrecisionReleaseDecision.Fire;
        }

        /// <summary> 카메라 조준 ray가 유한하고 정규화됐으며 허용 원점 안에 있는지 검사한다 </summary>
        public static bool IsValidAimRay(
            in PrecisionAimRay aim,
            Vector3 playerPosition,
            float maxOriginDistance)
        {
            if ((!IsFinite(aim.Origin)) || (!IsFinite(aim.Direction))
                || (!IsFinite(playerPosition)) || (!IsFinite(maxOriginDistance))
                || (maxOriginDistance <= 0f))
            {
                return false;
            }

            float directionSqr = aim.Direction.sqrMagnitude;
            if (Mathf.Abs(directionSqr - 1f) > NORMALIZED_DIRECTION_TOLERANCE)
            {
                return false;
            }

            float maxDistanceSqr = maxOriginDistance * maxOriginDistance;
            return ((aim.Origin - playerPosition).sqrMagnitude <= maxDistanceSqr);
        }

        /// <summary> 요청이 지금 런과 씬에서 만들어졌는지 검사한다 </summary>
        public static bool IsCurrentRequestContext(
            uint requestedRunIdValue,
            uint requestedSceneRevisionValue,
            uint currentRunIdValue,
            uint currentSceneRevisionValue)
        {
            return (requestedRunIdValue != 0u)
                && (requestedRunIdValue == currentRunIdValue)
                && (requestedSceneRevisionValue == currentSceneRevisionValue);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
