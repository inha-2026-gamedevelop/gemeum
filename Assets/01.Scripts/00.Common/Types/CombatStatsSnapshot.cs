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
        public float SupportBuffDamageMultiplier;
        public float SupportBuffDuration;

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
                    (SupportBuffDamageMultiplier.Equals(other.SupportBuffDamageMultiplier)) &&
                    (SupportBuffDuration.Equals(other.SupportBuffDuration)));
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
