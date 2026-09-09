// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary> SO 실행값을 한 번 snapshot하고 기존 에셋의 미직렬화 필드만 이전값으로 보완한다 </summary>
    internal readonly struct BossPatternExecutionSpec
    {
        public const int MAX_REPEAT_COUNT = 8;

        public BossPatternSO.PatternTemplate  Template      { get; }
        public BossPatternSO.PatternTargeting Targeting     { get; }
        public float Range                                { get; }
        public float AngleDegrees                         { get; }
        public float MoveSpeed                            { get; }
        public float MoveDistance                         { get; }
        public float ImpactRadius                         { get; }
        public int   RepeatCount                          { get; }
        public int   ParryableRepeatIndex                 { get; }

        public BossPatternExecutionSpec(in BossPatternSO.PatternEntry pattern)
        {
            ResolveLegacyShape(pattern.PatternId, out BossPatternSO.PatternTemplate template,
                out float range, out float angle, out float moveSpeed, out float moveDistance,
                out float impactRadius);

            Template      = (pattern.Template == BossPatternSO.PatternTemplate.Unknown)
                ? template : pattern.Template;
            Targeting     = pattern.Targeting;
            Range         = PositiveOr(pattern.Range, range);
            AngleDegrees  = PositiveOr(pattern.AngleDegrees, angle);
            MoveSpeed     = PositiveOr(pattern.MoveSpeed, moveSpeed);
            MoveDistance  = PositiveOr(pattern.MoveDistance, moveDistance);
            ImpactRadius  = PositiveOr(pattern.ImpactRadius, impactRadius);
            RepeatCount   = Mathf.Clamp(pattern.RepeatCount, 1, MAX_REPEAT_COUNT);
            ParryableRepeatIndex = pattern.ParryableRepeatIndex;
        }

        public float GetImpactOffset(in BossPatternSO.PatternEntry pattern, int repeatIndex)
        {
            float fallbackStep = Mathf.Max(0.1f, pattern.TelegraphDuration * 0.35f);
            float offset = pattern.GetImpactOffset(repeatIndex);
            if ((!IsFinite(offset)) || (offset < 0f))
            {
                offset = pattern.TelegraphDuration + (fallbackStep * repeatIndex);
            }

            if (repeatIndex <= 0)
            {
                return Mathf.Max(0f, offset);
            }

            float previous = pattern.GetImpactOffset(repeatIndex - 1);
            if ((!IsFinite(previous)) || (offset <= previous))
            {
                offset = Mathf.Max(0f, previous) + fallbackStep;
            }

            return offset;
        }

        public bool IsRepeatParryable(in BossPatternSO.PatternEntry pattern, int repeatIndex)
        {
            return pattern.IsParryable &&
                ((ParryableRepeatIndex < 0) || (ParryableRepeatIndex == repeatIndex));
        }

        /// <summary>첫 예고 시각은 유지하고 뒤 impact 사이 간격만 서버 난수로 흔든다.</summary>
        public void BuildImpactSchedule(in BossPatternSO.PatternEntry pattern,
            System.Random random, float[] destination)
        {
            if ((destination == null) || (destination.Length == 0))
            {
                return;
            }

            int count = Mathf.Min(RepeatCount, destination.Length);
            destination[0] = GetImpactOffset(pattern, 0);
            float jitterMin = FiniteOrZero(pattern.RepeatTimingJitterMin);
            float jitterMax = FiniteOrZero(pattern.RepeatTimingJitterMax);
            if (jitterMax < jitterMin)
            {
                float swap = jitterMin;
                jitterMin = jitterMax;
                jitterMax = swap;
            }

            for (int i = 1; i < count; ++i)
            {
                float baseInterval = GetImpactOffset(pattern, i) -
                    GetImpactOffset(pattern, i - 1);
                float jitter = (random == null) || Mathf.Approximately(jitterMin, jitterMax)
                    ? jitterMin
                    : Mathf.Lerp(jitterMin, jitterMax, (float) random.NextDouble());
                destination[i] = destination[i - 1] +
                    Mathf.Max(0.25f, baseInterval + jitter);
            }
        }

        private static float PositiveOr(float value, float fallback)
        {
            return (IsFinite(value) && (value > 0f)) ? value : fallback;
        }

        private static float FiniteOrZero(float value)
        {
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static void ResolveLegacyShape(int patternId,
            out BossPatternSO.PatternTemplate template, out float range, out float angle,
            out float moveSpeed, out float moveDistance, out float impactRadius)
        {
            bool phase2 = patternId >= 6;
            template = BossPatternSO.PatternTemplate.MeleeArc;
            range = phase2 ? 5.13f : 3.19f;
            angle = phase2 ? 180f : 100f;
            moveSpeed = phase2 ? 12f : 10f;
            moveDistance = phase2 ? 15.96f : 9.12f;
            impactRadius = 1.37f;

            switch (patternId)
            {
                case 3:
                case 7:
                    template = BossPatternSO.PatternTemplate.TargetedDash;
                    break;
                case 4:
                case 8:
                    template = BossPatternSO.PatternTemplate.MarkedCircle;
                    range = phase2 ? 6.27f : 3.99f;
                    angle = 360f;
                    impactRadius = range;
                    break;
                case 5:
                case 9:
                case 10:
                    template = BossPatternSO.PatternTemplate.ConeBurst;
                    range = 10.26f;
                    angle = 60f;
                    break;
            }
        }
    }
}
