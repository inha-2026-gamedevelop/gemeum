// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary> 서버가 거리, 쿨다운, 직전 패턴을 적용해 보스 패턴을 고른다 </summary>
    public static class BossPatternSelector
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 선택기에 전달할 한 패턴의 서버 상태 </summary>
        public struct Candidate
        {
            public BossPatternSO.PatternEntry Pattern;
            public float                      CooldownRemaining;
            public bool                       IsDistanceEligible;
            public bool                       IsConditional;
            public bool                       IsSealed;

            public Candidate(
                BossPatternSO.PatternEntry pattern,
                float cooldownRemaining,
                bool isDistanceEligible,
                bool isConditional)
                : this(pattern, cooldownRemaining, isDistanceEligible, isConditional, false)
            {
            }

            public Candidate(
                BossPatternSO.PatternEntry pattern,
                float cooldownRemaining,
                bool isDistanceEligible,
                bool isConditional,
                bool isSealed)
            {
                Pattern              = pattern;
                CooldownRemaining    = cooldownRemaining;
                IsDistanceEligible   = isDistanceEligible;
                IsConditional        = isConditional;
                IsSealed             = isSealed;
            }
        }

        /****************************************
        *                Constants
        ****************************************/

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 유효 후보를 필터링하고 남은 후보에서 균등하게 하나를 고른다
        /// </summary>
        public static bool TrySelect(
            Candidate[] candidates,
            int candidateCount,
            int previousPatternId,
            System.Random random,
            out Candidate selected)
        {
            selected = default;

            if (candidates == null)
            {
                return false;
            }

            if ((candidateCount <= 0) || (candidateCount > candidates.Length))
            {
                return false;
            }

            if (random == null)
            {
                return false;
            }

            float totalWeight = SumEligibleWeight(candidates, candidateCount, previousPatternId);
            if (totalWeight <= 0f)
            {
                return false;
            }

            double selectedWeight = random.NextDouble() * totalWeight;

            for (int i = 0; i < candidateCount; ++i)
            {
                Candidate candidate = candidates[i];
                if (!IsEligible(candidate, previousPatternId))
                {
                    continue;
                }

                selectedWeight -= ResolveWeight(candidate.Pattern.SelectionWeight);
                if (selectedWeight <= 0d)
                {
                    selected = candidate;
                    return true;
                }

            }

            selected = default;
            return false;
        }

        private static float SumEligibleWeight(
            Candidate[] candidates,
            int candidateCount,
            int previousPatternId)
        {
            float totalWeight = 0f;

            for (int i = 0; i < candidateCount; ++i)
            {
                if (IsEligible(candidates[i], previousPatternId))
                {
                    totalWeight += ResolveWeight(candidates[i].Pattern.SelectionWeight);
                }
            }

            return totalWeight;
        }

        private static bool IsEligible(Candidate candidate, int previousPatternId)
        {
            if (candidate.Pattern.PatternId <= 0)
            {
                return false;
            }

            if (candidate.Pattern.PatternId == previousPatternId)
            {
                return false;
            }

            if ((!candidate.IsDistanceEligible) || (candidate.IsConditional) || (candidate.IsSealed))
            {
                return false;
            }

            if ((!IsFinite(candidate.CooldownRemaining))
                || (candidate.CooldownRemaining > 0f))
            {
                return false;
            }

            return true;
        }

        private static float ResolveWeight(float value)
        {
            // 새 필드가 없는 기존 에셋의 0은 D14의 1차 이관값 1.0으로 취급한다
            return (IsFinite(value) && (value > 0f)) ? value : 1f;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
