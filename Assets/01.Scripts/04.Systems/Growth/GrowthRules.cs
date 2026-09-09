// System
using System;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 성장 곡선에 대한 순수 계산. 네트워크나 플레이어 객체를 만지지 않는다 </summary>
    public static class GrowthRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 런의 공용 성장 상태를 만든다 </summary>
        public static bool TryCreateStartingState(LevelCurveSO curve, out GrowthState state)
        {
            state = default;

            if ((curve == null) ||
                (!curve.TryGetLevel(Constants.Combat.START_LEVEL, out LevelCurveSO.LevelEntry entry)) ||
                (!IsFinite(entry.MaxHealth)) || (entry.MaxHealth <= 0f) ||
                (curve.TalentPointPerLevel < 0))
            {
                return false;
            }

            state = new GrowthState(Constants.Combat.START_LEVEL, 0, entry.MaxHealth, 1u, 0);
            return true;
        }

        /// <summary> 누적 경험치를 적용하고 가능한 레벨업을 모두 처리한다 </summary>
        public static bool TryApplyExperience(LevelCurveSO curve, GrowthState current, int amount,
            out GrowthState next, out GrowthApplyResult result)
        {
            next   = current;
            result = default;

            if ((curve == null) || (amount <= 0) || (!IsUsableState(curve, current)))
            {
                return false;
            }

            if (current.Revision == uint.MaxValue)
            {
                return false;
            }

            if (!TryGetCumulativeExperience(curve, curve.MaxLevel, out int maxExperience))
            {
                return false;
            }

            long totalExperience = (long)current.Experience + amount;
            int experience = (totalExperience >= maxExperience) ? maxExperience : (int)totalExperience;
            int level = current.Level;
            int levelsGained = 0;

            while (level < curve.MaxLevel)
            {
                if (!TryGetCumulativeExperience(curve, level + 1, out int requiredExperience))
                {
                    return false;
                }

                if (experience < requiredExperience)
                {
                    break;
                }

                ++level;
                ++levelsGained;
            }

            if (!curve.TryGetLevel(level, out LevelCurveSO.LevelEntry levelEntry) ||
                (!IsFinite(levelEntry.MaxHealth)) || (levelEntry.MaxHealth <= 0f))
            {
                return false;
            }

            if ((experience == current.Experience) && (levelsGained == 0))
            {
                return false;
            }

            long talentPointsValue = (long)(level - Constants.Combat.START_LEVEL) *
                curve.TalentPointPerLevel;
            if ((talentPointsValue < 0L) || (talentPointsValue > int.MaxValue))
            {
                return false;
            }

            int talentPoints = (int)talentPointsValue;
            next = new GrowthState(level, experience, levelEntry.MaxHealth, current.Revision + 1u,
                talentPoints);
            result = new GrowthApplyResult(current, next, experience - current.Experience, levelsGained,
                levelEntry.MaxHealth - current.MaxHealth);
            return true;
        }

        /// <summary> 보상 배수를 적용한 경험치. 화폐 보상과 같은 내림 뒤 int 범위로 고정한다 </summary>
        public static int ResolveRewardExperience(int baseExperience, float rewardMultiplier)
        {
            if ((baseExperience <= 0) || (!IsFinite(rewardMultiplier)) || (rewardMultiplier <= 0f))
            {
                return 0;
            }

            double scaled = Math.Floor(baseExperience * (double)rewardMultiplier);
            if (scaled >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (scaled <= 0d) ? 0 : (int)scaled;
        }

        /// <summary> 특정 레벨에 도달하는 데 필요한 누적 경험치 </summary>
        public static bool TryGetCumulativeExperience(LevelCurveSO curve, int targetLevel,
            out int experience)
        {
            experience = 0;

            if ((curve == null) || (targetLevel < Constants.Combat.START_LEVEL) ||
                (targetLevel > curve.MaxLevel))
            {
                return false;
            }

            long total = 0L;
            for (int level = Constants.Combat.START_LEVEL; level < targetLevel; ++level)
            {
                if (!curve.TryGetLevel(level, out LevelCurveSO.LevelEntry entry) ||
                    (entry.RequiredExperience <= 0))
                {
                    return false;
                }

                total += entry.RequiredExperience;
                if (total > int.MaxValue)
                {
                    return false;
                }
            }

            experience = (int)total;
            return true;
        }

        /// <summary> 상태와 곡선이 함께 사용할 수 있는지 검사한다 </summary>
        public static bool IsUsableState(LevelCurveSO curve, GrowthState state)
        {
            if ((curve == null) || (!state.IsValid) ||
                (state.Level < Constants.Combat.START_LEVEL) || (state.Level > curve.MaxLevel))
            {
                return false;
            }

            if (!curve.TryGetLevel(state.Level, out LevelCurveSO.LevelEntry entry) ||
                (!IsFinite(entry.MaxHealth)) || (entry.MaxHealth <= 0f))
            {
                return false;
            }

            if ((state.MaxHealth != entry.MaxHealth) || (curve.TalentPointPerLevel < 0))
            {
                return false;
            }

            long expectedTalentPoints = (long)(state.Level - Constants.Combat.START_LEVEL) *
                curve.TalentPointPerLevel;
            if ((expectedTalentPoints < 0L) || (expectedTalentPoints > int.MaxValue) ||
                (state.TalentPoints != (int)expectedTalentPoints))
            {
                return false;
            }

            if (!TryGetCumulativeExperience(curve, state.Level, out int levelExperience) ||
                (state.Experience < levelExperience))
            {
                return false;
            }

            if (state.Level < curve.MaxLevel)
            {
                return (TryGetCumulativeExperience(curve, state.Level + 1,
                            out int nextLevelExperience) &&
                        (state.Experience < nextLevelExperience));
            }

            return (TryGetCumulativeExperience(curve, curve.MaxLevel, out int maxExperience) &&
                    (state.Experience <= maxExperience));
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
