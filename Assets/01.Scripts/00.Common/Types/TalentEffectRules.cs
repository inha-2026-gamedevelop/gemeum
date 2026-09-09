// System
using System;

namespace Geumeum.Common
{
    /// <summary> Data와 Systems가 함께 쓰는 특성 효과 호환성과 값 형식 규칙 </summary>
    public static class TalentEffectRules
    {
        public static bool IsCompatible(WeaponKind weapon, TalentEffectKind effect)
        {
            switch (effect)
            {
                case TalentEffectKind.CriticalChancePercent:
                case TalentEffectKind.AttackSpeedPercent:
                case TalentEffectKind.StaminaCostPercent:
                    return true;

                case TalentEffectKind.ParryGaugeGain:
                case TalentEffectKind.ParryWindowSeconds:
                case TalentEffectKind.CriticalMultiplierMin:
                case TalentEffectKind.ThirdComboDamagePercent:
                    return weapon == WeaponKind.Sword;

                case TalentEffectKind.ArrowSpeedPercent:
                case TalentEffectKind.GuidanceDurationSeconds:
                case TalentEffectKind.PierceCount:
                case TalentEffectKind.BurstShotCount:
                case TalentEffectKind.ArrowHitRadiusPercent:
                case TalentEffectKind.RangedDamagePercent:
                    return weapon == WeaponKind.Bow;

                case TalentEffectKind.HealAmountPercent:
                case TalentEffectKind.BuffDurationPercent:
                case TalentEffectKind.SpiritCostPercent:
                case TalentEffectKind.HealRadiusPercent:
                case TalentEffectKind.BellDamagePercent:
                case TalentEffectKind.SupportBuffPercent:
                    return weapon == WeaponKind.Bell;

                default:
                    return false;
            }
        }

        public static bool IsApplicableValue(TalentEffectKind effect, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            switch (effect)
            {
                case TalentEffectKind.StaminaCostPercent:
                case TalentEffectKind.SpiritCostPercent:
                    return (value > -100f) && (value < 0f);

                case TalentEffectKind.PierceCount:
                case TalentEffectKind.BurstShotCount:
                    return (value >= 1f) && (value <= 7f) &&
                        (value == (float)Math.Floor(value));

                case TalentEffectKind.CriticalMultiplierMin:
                    return value >= 1f;

                case TalentEffectKind.CriticalChancePercent:
                    return (value > 0f) && (value <= 100f);

                default:
                    return value > 0f;
            }
        }
    }
}
