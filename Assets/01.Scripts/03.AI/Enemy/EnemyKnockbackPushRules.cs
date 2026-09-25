// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 넉백 밀기의 방향, 감속 곡선 순수 규칙. 서버 EnemyController가 쓰고 EditMode 테스트가 검증한다
    /// 거리, 시간 수치는 EnemySO(KnockbackDistance, KnockbackPushSeconds)에서 받는다 - 여기에는 곡선 모양만 둔다
    /// </summary>
    public static class EnemyKnockbackPushRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR   = 0.0001f; // 이보다 짧은 수평 벡터는 방향으로 믿지 않는다
        private const float BLOCKED_MOVE_RATIO  = 0.5f;    // 요청한 거리의 이 비율도 못 나갔으면 막힌 것으로 본다
        private const float MIN_BLOCK_CHECK_STEP = 0.01f;  // 곡선 끝의 아주 작은 걸음은 오차가 커서 막힘 판정에 쓰지 않는다(m)

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 밀어낼 수평 방향. 가해자 → 잡귀 방향을 수평으로 정규화한다
        /// 가해자가 없거나(환경 피해, 가해자 소멸) 같은 자리에 겹쳐 있으면 잡귀의 뒤쪽(-forward)으로,
        /// 그마저 수평 성분이 없으면 월드 뒤쪽으로 민다 - 어떤 입력에도 길이 1의 수평 벡터를 돌려준다
        /// </summary>
        public static Vector3 ResolveDirection(Vector3 selfPosition, Vector3 selfForward,
            bool hasSource, Vector3 sourcePosition)
        {
            if (hasSource)
            {
                Vector3 away = selfPosition - sourcePosition;
                away.y       = 0f;
                if (away.sqrMagnitude > MIN_DIRECTION_SQR)
                {
                    return away.normalized;
                }
            }

            Vector3 back = -selfForward;
            back.y       = 0f;
            return (back.sqrMagnitude > MIN_DIRECTION_SQR) ? back.normalized : Vector3.back;
        }

        /// <summary> 밀기 시간은 넉백 상태 시간을 넘지 않는다, 둘 중 하나라도 유효하지 않으면 0(밀지 않음) </summary>
        public static float ClampPushSeconds(float pushSeconds, float knockbackDuration)
        {
            if (!IsFinitePositive(pushSeconds) || !IsFinitePositive(knockbackDuration))
            {
                return 0f;
            }

            return Mathf.Min(pushSeconds, knockbackDuration);
        }

        /// <summary> 거리와 (Clamp한) 시간이 모두 양수여야 민다 </summary>
        public static bool CanPush(float distance, float pushSeconds)
        {
            return IsFinitePositive(distance) && IsFinitePositive(pushSeconds);
        }

        /// <summary>
        /// 경과 시간에 대한 누적 이동 비율(0~1). ease-out 2차 곡선 1-(1-t)^2 -
        /// 처음에 가장 빠르고(초기 속도 = 2D/T) 끝에서 0으로 잦아든다
        /// </summary>
        public static float EvaluateProgress(float elapsed, float pushSeconds)
        {
            if (!IsFinitePositive(pushSeconds))
            {
                return 1f;
            }

            float t         = Mathf.Clamp01(elapsed / pushSeconds);
            float remaining = 1f - t;
            return 1f - (remaining * remaining);
        }

        /// <summary>
        /// 이번 틱에 밀 거리. 누적 곡선의 차이로 구하므로 틱 간격이 들쭉날쭉해도
        /// 경과 시간이 pushSeconds에 닿으면 합계가 정확히 distance가 된다
        /// </summary>
        public static float StepDistance(float distance, float pushSeconds, float previousElapsed, float elapsed)
        {
            if (!CanPush(distance, pushSeconds))
            {
                return 0f;
            }

            return distance * (EvaluateProgress(elapsed, pushSeconds) - EvaluateProgress(previousElapsed, pushSeconds));
        }

        /// <summary> 요청한 걸음 대비 실제로 밀린 거리(밀기 방향 성분)가 너무 짧으면 벽, NavMesh 경계에 막힌 것이다 </summary>
        public static bool IsBlocked(float requestedStep, float movedAlongDirection)
        {
            if (requestedStep < MIN_BLOCK_CHECK_STEP)
            {
                return false;
            }

            return movedAlongDirection < (requestedStep * BLOCKED_MOVE_RATIO);
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value > 0f);
        }
    }
}
