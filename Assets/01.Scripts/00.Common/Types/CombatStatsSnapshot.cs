// System
using System;

namespace Geumeum.Common
{
    /// <summary> 레벨, 장비, 특성을 모두 반영한 유효 전투 수치. 공격을 수락하는 순간의 값을 끝까지 쓴다 </summary>
    [Serializable]
    public struct CombatStatsSnapshot : IEquatable<CombatStatsSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 레벨, 장비, 특성 중 하나라도 바뀌면 올라간다. 0은 아직 만들지 않은 값이다 </summary>
        public uint Revision;

        public float MaxHealth;
        public float MeleeAttackPower;
        public float RangedAttackPower;

        public float CriticalChance;
        public float AttackSpeedMultiplier;
        public float StaminaCostMultiplier;
        public float SpiritCostMultiplier;
        public float MinimumResourceCost;

        public float BasicStaminaReduction;
        public float CounterDurationBonus;
        public float ParryStaminaBonus;
        public float CounterRecoveryReduction;
        public float CounterThirdCombo;
        public float SecondComboDamagePercent;
        public float ComboWindowBonus;
        public float ThirdHitSkillCooldown;
        public float SkillSecondCombo;
        public float RollBasicStaminaReduction;
        public float MissSkillRecoveryReduction;
        public float RollSkillDamagePercent;
        public float SideStepSkill;
        public float BowBasicStaminaReduction;
        public float BowSkillStaminaReduction;
        public float WedgeDurationBonus;
        public float WedgeFirstHitCooldown;
        public float WedgeNormalBurstPercent;
        public float PrecisionStaminaReduction;
        public float WedgePrecisionBurstPercent;
        public float PrecisionPierceBonus;
        public float PrecisionFollowThrough;
        public float BowRollBasicReduction;
        public float BowComboWindowBonus;
        public float WedgeRollReduction;
        public float RollShotRecoveryReduction;

        public float ParryGaugeGain;
        public float ParryWindowBonus;
        public float CriticalMultiplierMin;
        public float CriticalMultiplierMax;
        public float ThirdComboDamageMultiplier;

        public float ArrowSpeed;
        public float ArrowHitRadius;
        public float GuidanceDuration;
        public int   PierceCount;
        public int   BurstShotCount;
        public float BurstShotInterval;

        public float HealAmount;
        public float HealRadius;
        public float HealSpiritCostReduction;

        // 공명진(R). 진을 만드는 순간의 시전자 값으로 고정하고 수혜자 장비로 다시 늘리지 않는다
        public float FieldRadius;
        public float FieldDuration;
        public float FieldTailDuration;
        public float FieldDamageMultiplier;
        public float SkillSpiritCostReduction;
        public float FieldEchoExtendSeconds;
        public float FieldHealEchoDiscount;
        public float FieldHealAfterglowRatio;

        // 잔향(Q)
        public float EchoSpiritCostReduction;
        public float EchoDurationBonus;
        public float EchoFieldDamageMultiplier;
        public float BellFirstHitSpiritGain;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Revision != 0u);

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(CombatStatsSnapshot other)
        {
            return ((Revision == other.Revision) &&
                    (BasicStaminaReduction.Equals(other.BasicStaminaReduction)) &&
                    (CounterDurationBonus.Equals(other.CounterDurationBonus)) &&
                    (ParryStaminaBonus.Equals(other.ParryStaminaBonus)) &&
                    (CounterRecoveryReduction.Equals(other.CounterRecoveryReduction)) &&
                    (CounterThirdCombo.Equals(other.CounterThirdCombo)) &&
                    (SecondComboDamagePercent.Equals(other.SecondComboDamagePercent)) &&
                    (ComboWindowBonus.Equals(other.ComboWindowBonus)) &&
                    (ThirdHitSkillCooldown.Equals(other.ThirdHitSkillCooldown)) &&
                    (SkillSecondCombo.Equals(other.SkillSecondCombo)) &&
                    (RollBasicStaminaReduction.Equals(other.RollBasicStaminaReduction)) &&
                    (MissSkillRecoveryReduction.Equals(other.MissSkillRecoveryReduction)) &&
                    (RollSkillDamagePercent.Equals(other.RollSkillDamagePercent)) &&
                    (SideStepSkill.Equals(other.SideStepSkill)) &&
                    (BowBasicStaminaReduction.Equals(other.BowBasicStaminaReduction)) &&
                    (BowSkillStaminaReduction.Equals(other.BowSkillStaminaReduction)) &&
                    (WedgeDurationBonus.Equals(other.WedgeDurationBonus)) &&
                    (WedgeFirstHitCooldown.Equals(other.WedgeFirstHitCooldown)) &&
                    (WedgeNormalBurstPercent.Equals(other.WedgeNormalBurstPercent)) &&
                    (PrecisionStaminaReduction.Equals(other.PrecisionStaminaReduction)) &&
                    (WedgePrecisionBurstPercent.Equals(other.WedgePrecisionBurstPercent)) &&
                    (PrecisionPierceBonus.Equals(other.PrecisionPierceBonus)) &&
                    (PrecisionFollowThrough.Equals(other.PrecisionFollowThrough)) &&
                    (BowRollBasicReduction.Equals(other.BowRollBasicReduction)) &&
                    (BowComboWindowBonus.Equals(other.BowComboWindowBonus)) &&
                    (WedgeRollReduction.Equals(other.WedgeRollReduction)) &&
                    (RollShotRecoveryReduction.Equals(other.RollShotRecoveryReduction)) &&

                    (MaxHealth.Equals(other.MaxHealth)) &&
                    (MeleeAttackPower.Equals(other.MeleeAttackPower)) &&
                    (RangedAttackPower.Equals(other.RangedAttackPower)) &&
                    (CriticalChance.Equals(other.CriticalChance)) &&
                    (AttackSpeedMultiplier.Equals(other.AttackSpeedMultiplier)) &&
                    (StaminaCostMultiplier.Equals(other.StaminaCostMultiplier)) &&
                    (SpiritCostMultiplier.Equals(other.SpiritCostMultiplier)) &&
                    (MinimumResourceCost.Equals(other.MinimumResourceCost)) &&
                    (ParryGaugeGain.Equals(other.ParryGaugeGain)) &&
                    (ParryWindowBonus.Equals(other.ParryWindowBonus)) &&
                    (CriticalMultiplierMin.Equals(other.CriticalMultiplierMin)) &&
                    (CriticalMultiplierMax.Equals(other.CriticalMultiplierMax)) &&
                    (ThirdComboDamageMultiplier.Equals(other.ThirdComboDamageMultiplier)) &&
                    (ArrowSpeed.Equals(other.ArrowSpeed)) &&
                    (ArrowHitRadius.Equals(other.ArrowHitRadius)) &&
                    (GuidanceDuration.Equals(other.GuidanceDuration)) &&
                    (PierceCount == other.PierceCount) &&
                    (BurstShotCount == other.BurstShotCount) &&
                    (BurstShotInterval.Equals(other.BurstShotInterval)) &&
                    (HealAmount.Equals(other.HealAmount)) &&
                    (HealRadius.Equals(other.HealRadius)) &&
                    (HealSpiritCostReduction.Equals(other.HealSpiritCostReduction)) &&
                    (FieldRadius.Equals(other.FieldRadius)) &&
                    (FieldDuration.Equals(other.FieldDuration)) &&
                    (FieldTailDuration.Equals(other.FieldTailDuration)) &&
                    (FieldDamageMultiplier.Equals(other.FieldDamageMultiplier)) &&
                    (SkillSpiritCostReduction.Equals(other.SkillSpiritCostReduction)) &&
                    (FieldEchoExtendSeconds.Equals(other.FieldEchoExtendSeconds)) &&
                    (FieldHealEchoDiscount.Equals(other.FieldHealEchoDiscount)) &&
                    (FieldHealAfterglowRatio.Equals(other.FieldHealAfterglowRatio)) &&
                    (EchoSpiritCostReduction.Equals(other.EchoSpiritCostReduction)) &&
                    (EchoDurationBonus.Equals(other.EchoDurationBonus)) &&
                    (EchoFieldDamageMultiplier.Equals(other.EchoFieldDamageMultiplier)) &&
                    (BellFirstHitSpiritGain.Equals(other.BellFirstHitSpiritGain)));
        }

        public override bool Equals(object obj) => ((obj is CombatStatsSnapshot other) && (Equals(other)));

        public override int GetHashCode() => Revision.GetHashCode();

        /// <summary> 공격 속도 배율을 선딜과 후딜 시간으로 바꾼다 </summary>
        public float ResolveAttackDuration(float baseDuration)
        {
            if ((baseDuration <= 0f) || (AttackSpeedMultiplier <= 0f))
            {
                return 0f;
            }

            return baseDuration / AttackSpeedMultiplier;
        }

        /// <summary> 스태미나 감소율과 SO 비용 하한을 한 번 적용한다 </summary>
        public float ResolveStaminaCost(float baseCost)
        {
            return ResolveResourceCost(baseCost, StaminaCostMultiplier);
        }

        /// <summary> 신력 감소율과 SO 비용 하한을 한 번 적용한다 </summary>
        public float ResolveSpiritCost(float baseCost)
        {
            return ResolveResourceCost(baseCost, SpiritCostMultiplier);
        }

        /// <summary>
        /// 비율 감소를 먼저 적용한 뒤 노드가 주는 절대 감소를 뺀다
        /// 비용은 0 아래로 내려가지 않고, 절대 감소가 붙으면 SO 하한도 그만큼 밀린다
        /// </summary>
        public float ResolveSpiritCost(float baseCost, float flatReduction)
        {
            float cost = ResolveSpiritCost(baseCost);
            if ((flatReduction <= 0f) || float.IsNaN(flatReduction) ||
                float.IsInfinity(flatReduction))
            {
                return cost;
            }

            return Math.Max(0f, cost - flatReduction);
        }

        private float ResolveResourceCost(float baseCost, float multiplier)
        {
            if ((baseCost <= 0f) || (multiplier < 0f))
            {
                return 0f;
            }

            float cost = baseCost * multiplier;
            return (MinimumResourceCost > 0f) ? Math.Max(MinimumResourceCost, cost) : cost;
        }
    }
}
