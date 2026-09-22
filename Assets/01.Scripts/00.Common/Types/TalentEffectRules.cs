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

                case TalentEffectKind.BasicStaminaReduction:
                case TalentEffectKind.CounterDurationBonus:
                case TalentEffectKind.ParryStaminaBonus:
                case TalentEffectKind.CounterRecoveryReduction:
                case TalentEffectKind.CounterThirdCombo:
                case TalentEffectKind.SecondComboDamagePercent:
                case TalentEffectKind.ComboWindowBonus:
                case TalentEffectKind.ThirdHitSkillCooldown:
                case TalentEffectKind.SkillSecondCombo:
                case TalentEffectKind.RollBasicStaminaReduction:
                case TalentEffectKind.MissSkillRecoveryReduction:
                case TalentEffectKind.RollSkillDamagePercent:
                case TalentEffectKind.SideStepSkill:
                    return weapon == WeaponKind.Sword;
                case TalentEffectKind.BowBasicStaminaReduction:
                case TalentEffectKind.BowSkillStaminaReduction:
                case TalentEffectKind.WedgeDurationBonus:
                case TalentEffectKind.WedgeFirstHitCooldown:
                case TalentEffectKind.WedgeNormalBurstPercent:
                case TalentEffectKind.PrecisionStaminaReduction:
                case TalentEffectKind.WedgePrecisionBurstPercent:
                case TalentEffectKind.PrecisionPierceBonus:
                case TalentEffectKind.PrecisionFollowThrough:
                case TalentEffectKind.BowRollBasicReduction:
                case TalentEffectKind.BowComboWindowBonus:
                case TalentEffectKind.WedgeRollReduction:
                case TalentEffectKind.RollShotRecoveryReduction:
                    return weapon == WeaponKind.Bow;

                case TalentEffectKind.HealAmountPercent:
                case TalentEffectKind.SpiritCostPercent:
                case TalentEffectKind.BellFirstHitSpiritGain:
                case TalentEffectKind.EchoRecallSpiritCost:
                case TalentEffectKind.EchoDurationSeconds:
                case TalentEffectKind.EchoFieldDamagePercent:
                case TalentEffectKind.FieldEchoExtendSeconds:
                case TalentEffectKind.HealSpiritCost:
                case TalentEffectKind.FieldHealEchoDiscount:
                case TalentEffectKind.FieldHealAfterglowPercent:
                case TalentEffectKind.SkillSpiritCost:
                case TalentEffectKind.FieldRadiusBonus:
                case TalentEffectKind.FieldTailSeconds:
                case TalentEffectKind.FieldWideningRadiusBonus:
                    return weapon == WeaponKind.Bell;

                // 401/403/404/405는 은퇴한 구 방울 노드의 효과다. 직렬화 값은 남기되
                // 새 노드가 이름만 바꿔 다시 쓰지 못하도록 어떤 무기에도 붙이지 않는다
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
                case TalentEffectKind.EchoFieldDamagePercent:
                case TalentEffectKind.FieldHealAfterglowPercent:
                    return (value > 0f) && (value <= 100f);

                case TalentEffectKind.BellFirstHitSpiritGain:
                    return (value >= 1f) && (value <= 5f) &&
                        (value == (float)Math.Floor(value));

                // 반경과 시간 노드는 부호만이 아니라 상한도 본다. 오타 한 자리가
                // 진 하나로 전장을 덮는 값이 되면 서버 부트에서 잡을 방법이 없다
                case TalentEffectKind.FieldRadiusBonus:
                case TalentEffectKind.FieldWideningRadiusBonus:
                    return (value > 0f) && (value <= 3f);

                case TalentEffectKind.EchoDurationSeconds:
                case TalentEffectKind.FieldEchoExtendSeconds:
                case TalentEffectKind.FieldTailSeconds:
                    return (value > 0f) && (value <= 5f);

                default:
                    return value > 0f;
            }
        }
    }
}
