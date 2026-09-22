// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>몰이 강화 때 기준 스탯에서 새 값을 만들고 현재 HP 비율을 보존하는 순수 규칙</summary>
    public static class EnemyHerdingEnhancementRules
    {
        public static bool TryCalculate(float currentHealth, float currentMaxHealth,
            float baseMaxHealth, float baseAttackPower,
            float healthMultiplier, float attackMultiplier, float rewardMultiplier,
            out float nextHealth, out float nextMaxHealth,
            out float nextAttackPower, out float nextRewardMultiplier)
        {
            nextHealth           = 0f;
            nextMaxHealth        = 0f;
            nextAttackPower      = 0f;
            nextRewardMultiplier = 0f;

            if (!IsFiniteNonNegative(currentHealth) ||
                !IsFinitePositive(currentMaxHealth) ||
                !IsFinitePositive(baseMaxHealth) ||
                !IsFinitePositive(baseAttackPower) ||
                !IsFinitePositive(healthMultiplier) ||
                !IsFinitePositive(attackMultiplier) ||
                !IsFiniteNonNegative(rewardMultiplier))
            {
                return false;
            }

            nextMaxHealth   = baseMaxHealth * healthMultiplier;
            nextAttackPower = baseAttackPower * attackMultiplier;
            if (!IsFinitePositive(nextMaxHealth) ||
                !IsFinitePositive(nextAttackPower))
            {
                return false;
            }

            float healthRatio = Mathf.Clamp01(currentHealth / currentMaxHealth);
            nextHealth           = nextMaxHealth * healthRatio;
            nextRewardMultiplier = rewardMultiplier;
            return true;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value > 0f);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value >= 0f);
        }
    }
}
