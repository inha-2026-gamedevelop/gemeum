// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 장비와 특성 원본에서 행동 수락 시 쓸 전투 수치를 만든다 </summary>
    public static class CombatStatCalculator
    {
        /****************************************
        *                Types
        ****************************************/

        private struct EffectTotals
        {
            public float CriticalChancePercent;
            public float AttackSpeedPercent;
            public float StaminaCostPercent;
            public float SpiritCostPercent;
            public float ParryGaugeGain;
            public float ParryWindowSeconds;
            public float CriticalMultiplierMin;
            public float ThirdComboDamagePercent;
            public float ArrowSpeedPercent;
            public float ArrowHitRadiusPercent;
            public float RangedDamagePercent;
            public float GuidanceDurationSeconds;
            public int   PierceCount;
            public int   BurstShotCount;
            public float HealAmountPercent;
            public float HealRadiusPercent;
            public float BuffDurationPercent;
            public float BellDamagePercent;
            public float SupportBuffPercent;
        }

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryBuild(CombatConfigSO combat, ArrowConfigSO arrow,
            TalentTreeSO tree, in WeaponCombatSnapshot weapon, in GrowthState growth,
            in TalentAllocationSnapshot allocation, float activeDamageMultiplier,
            uint revision,
            out CombatStatsSnapshot snapshot)
        {
            snapshot = default;
            if ((combat == null) || (arrow == null) || (tree == null) ||
                (!growth.IsValid) || (revision == 0u) || (!IsValidWeapon(weapon)) ||
                (!IsFinite(activeDamageMultiplier)) || (activeDamageMultiplier < 1f) ||
                (!allocation.IsValid) || (!allocation.HasWeapon) ||
                (allocation.Weapon != weapon.WeaponKind) ||
                (!TalentRules.TryValidateAllocation(tree, allocation, growth.TalentPoints,
                    out _)))
            {
                return false;
            }

            EffectTotals totals = default;
            if ((!TryApplyEquipmentOption(weapon.WeaponKind, weapon.Option0Kind,
                    weapon.Option0Value, ref totals)) ||
                (!TryApplyEquipmentOption(weapon.WeaponKind, weapon.Option1Kind,
                    weapon.Option1Value, ref totals)) ||
                (!TryApplyTalents(tree, allocation, ref totals)))
            {
                return false;
            }

            float meleePower  = combat.MeleeBasePower;
            float rangedPower = combat.RangedBasePower;
            switch (weapon.WeaponKind.ToReach())
            {
                case AttackReach.Melee:
                    meleePower = weapon.AttackPower;
                    break;
                case AttackReach.Ranged:
                    rangedPower = weapon.AttackPower *
                        PercentMultiplier(totals.RangedDamagePercent);
                    break;
                case AttackReach.Area:
                    meleePower = weapon.AttackPower * PercentMultiplier(totals.BellDamagePercent);
                    break;
                default:
                    return false;
            }

            float attackSpeed = PercentMultiplier(totals.AttackSpeedPercent);
            if (combat.MaximumAttackSpeedMultiplier > 0f)
            {
                attackSpeed = Mathf.Min(attackSpeed, combat.MaximumAttackSpeedMultiplier);
            }

            float staminaCost = Mathf.Max(0f, PercentMultiplier(totals.StaminaCostPercent));
            float spiritCost  = Mathf.Max(0f, PercentMultiplier(totals.SpiritCostPercent));
            float criticalMin = Mathf.Max(combat.CriticalMultiplierMin,
                totals.CriticalMultiplierMin);
            criticalMin = Mathf.Min(criticalMin, combat.CriticalMultiplierMax);

            int pierceCount = arrow.BasePierceCount + totals.PierceCount;
            int burstCount  = 1 + totals.BurstShotCount;

            meleePower  *= activeDamageMultiplier;
            rangedPower *= activeDamageMultiplier;

            snapshot = new CombatStatsSnapshot
            {
                Revision                  = revision,
                MaxHealth                 = growth.MaxHealth,
                MeleeAttackPower          = meleePower,
                RangedAttackPower         = rangedPower,
                CriticalChance            = Mathf.Clamp01(totals.CriticalChancePercent * 0.01f),
                AttackSpeedMultiplier      = attackSpeed,
                StaminaCostMultiplier      = staminaCost,
                SpiritCostMultiplier       = spiritCost,
                MinimumResourceCost        = Mathf.Max(0f, combat.MinimumResourceCost),
                ParryGaugeGain              = combat.ParryGaugeGainPerSuccess + totals.ParryGaugeGain,
                ParryWindowBonus            = totals.ParryWindowSeconds,
                CriticalMultiplierMin       = criticalMin,
                CriticalMultiplierMax       = combat.CriticalMultiplierMax,
                ThirdComboDamageMultiplier  = PercentMultiplier(totals.ThirdComboDamagePercent),
                ArrowSpeed                  = arrow.ArrowSpeed * PercentMultiplier(totals.ArrowSpeedPercent),
                ArrowHitRadius              = arrow.UnlockedHitRadius *
                    PercentMultiplier(totals.ArrowHitRadiusPercent),
                GuidanceDuration            = arrow.GuidanceDuration + totals.GuidanceDurationSeconds,
                PierceCount                 = pierceCount,
                BurstShotCount              = burstCount,
                BurstShotInterval           = (burstCount > 1)
                    ? combat.BurstShotInterval / attackSpeed
                    : 0f,
                HealAmount                  = combat.HealAmount * PercentMultiplier(totals.HealAmountPercent),
                HealRadius                  = combat.HealRadius * PercentMultiplier(totals.HealRadiusPercent),
                SupportBuffDamageMultiplier = PercentMultiplier(totals.SupportBuffPercent),
                SupportBuffDuration         = combat.SupportBuffDuration *
                    PercentMultiplier(totals.BuffDurationPercent),
            };

            return IsValidSnapshot(snapshot);
        }

        private static bool TryApplyEquipmentOption(WeaponKind weapon,
            EquipmentOptionKind kind, float value, ref EffectTotals totals)
        {
            if ((!Enum.IsDefined(typeof(EquipmentOptionKind), kind)) || (!IsFinite(value)))
            {
                return false;
            }

            if (kind == EquipmentOptionKind.None)
            {
                return value == 0f;
            }

            switch (kind)
            {
                case EquipmentOptionKind.CriticalChance:
                    totals.CriticalChancePercent += value;
                    return true;
                case EquipmentOptionKind.AttackSpeed:
                    totals.AttackSpeedPercent += value;
                    return true;
                case EquipmentOptionKind.StaminaCostReduction:
                    totals.StaminaCostPercent += value;
                    return true;
                case EquipmentOptionKind.ParryGaugeGain:
                    if (weapon != WeaponKind.Sword)
                    {
                        return false;
                    }

                    totals.ParryGaugeGain += value;
                    return true;
                case EquipmentOptionKind.ParryWindow:
                    if (weapon != WeaponKind.Sword)
                    {
                        return false;
                    }

                    totals.ParryWindowSeconds += value;
                    return true;
                case EquipmentOptionKind.CriticalMultiplierMin:
                    if (weapon != WeaponKind.Sword)
                    {
                        return false;
                    }

                    totals.CriticalMultiplierMin = Mathf.Max(totals.CriticalMultiplierMin, value);
                    return true;
                case EquipmentOptionKind.ArrowSpeed:
                    if (weapon != WeaponKind.Bow)
                    {
                        return false;
                    }

                    totals.ArrowSpeedPercent += value;
                    return true;
                case EquipmentOptionKind.GuidanceDuration:
                    if (weapon != WeaponKind.Bow)
                    {
                        return false;
                    }

                    totals.GuidanceDurationSeconds += value;
                    return true;
                case EquipmentOptionKind.PierceCount:
                    if ((weapon != WeaponKind.Bow) || (!TryAddWholeNumber(value,
                            ref totals.PierceCount)))
                    {
                        return false;
                    }

                    return true;
                case EquipmentOptionKind.HealAmount:
                    if (weapon != WeaponKind.Bell)
                    {
                        return false;
                    }

                    totals.HealAmountPercent += value;
                    return true;
                case EquipmentOptionKind.BuffDuration:
                    if (weapon != WeaponKind.Bell)
                    {
                        return false;
                    }

                    totals.BuffDurationPercent += value;
                    return true;
                case EquipmentOptionKind.SpiritCostReduction:
                    if (weapon != WeaponKind.Bell)
                    {
                        return false;
                    }

                    totals.SpiritCostPercent += value;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryApplyTalents(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, ref EffectTotals totals)
        {
            for (int i = 0; i < allocation.NodeIds.Length; ++i)
            {
                if (!tree.TryGetNode(allocation.NodeIds[i], out TalentTreeSO.NodeEntry node))
                {
                    return false;
                }

                switch (node.EffectKind)
                {
                    case TalentEffectKind.CriticalChancePercent:
                        totals.CriticalChancePercent += node.Value;
                        break;
                    case TalentEffectKind.AttackSpeedPercent:
                        totals.AttackSpeedPercent += node.Value;
                        break;
                    case TalentEffectKind.StaminaCostPercent:
                        totals.StaminaCostPercent += node.Value;
                        break;
                    case TalentEffectKind.ParryGaugeGain:
                        totals.ParryGaugeGain += node.Value;
                        break;
                    case TalentEffectKind.ParryWindowSeconds:
                        totals.ParryWindowSeconds += node.Value;
                        break;
                    case TalentEffectKind.CriticalMultiplierMin:
                        totals.CriticalMultiplierMin = Mathf.Max(
                            totals.CriticalMultiplierMin, node.Value);
                        break;
                    case TalentEffectKind.ThirdComboDamagePercent:
                        totals.ThirdComboDamagePercent += node.Value;
                        break;
                    case TalentEffectKind.ArrowSpeedPercent:
                        totals.ArrowSpeedPercent += node.Value;
                        break;
                    case TalentEffectKind.ArrowHitRadiusPercent:
                        totals.ArrowHitRadiusPercent += node.Value;
                        break;
                    case TalentEffectKind.RangedDamagePercent:
                        totals.RangedDamagePercent += node.Value;
                        break;
                    case TalentEffectKind.GuidanceDurationSeconds:
                        totals.GuidanceDurationSeconds += node.Value;
                        break;
                    case TalentEffectKind.PierceCount:
                        if (!TryAddWholeNumber(node.Value, ref totals.PierceCount))
                        {
                            return false;
                        }

                        break;
                    case TalentEffectKind.BurstShotCount:
                        if (!TryAddWholeNumber(node.Value, ref totals.BurstShotCount))
                        {
                            return false;
                        }

                        break;
                    case TalentEffectKind.HealAmountPercent:
                        totals.HealAmountPercent += node.Value;
                        break;
                    case TalentEffectKind.BuffDurationPercent:
                        totals.BuffDurationPercent += node.Value;
                        break;
                    case TalentEffectKind.SpiritCostPercent:
                        totals.SpiritCostPercent += node.Value;
                        break;
                    case TalentEffectKind.HealRadiusPercent:
                        totals.HealRadiusPercent += node.Value;
                        break;
                    case TalentEffectKind.BellDamagePercent:
                        totals.BellDamagePercent += node.Value;
                        break;
                    case TalentEffectKind.SupportBuffPercent:
                        totals.SupportBuffPercent += node.Value;
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool TryAddWholeNumber(float value, ref int total)
        {
            if ((!IsFinite(value)) || (value < 0f) || (value > int.MaxValue) ||
                (value != Mathf.Floor(value)))
            {
                return false;
            }

            int amount = (int)value;
            if (total > (int.MaxValue - amount))
            {
                return false;
            }

            total += amount;
            return true;
        }

        private static bool IsValidWeapon(in WeaponCombatSnapshot weapon)
        {
            return Enum.IsDefined(typeof(WeaponKind), weapon.WeaponKind) &&
                IsFinite(weapon.AttackPower) && (weapon.AttackPower > 0f);
        }

        private static bool IsValidSnapshot(in CombatStatsSnapshot snapshot)
        {
            return snapshot.IsValid && IsFinite(snapshot.MaxHealth) &&
                (snapshot.MaxHealth > 0f) && IsFinite(snapshot.MeleeAttackPower) &&
                (snapshot.MeleeAttackPower > 0f) && IsFinite(snapshot.RangedAttackPower) &&
                (snapshot.RangedAttackPower > 0f) && IsFinite(snapshot.CriticalChance) &&
                IsFinite(snapshot.AttackSpeedMultiplier) &&
                (snapshot.AttackSpeedMultiplier > 0f) &&
                IsFinite(snapshot.StaminaCostMultiplier) &&
                (snapshot.StaminaCostMultiplier >= 0f) &&
                IsFinite(snapshot.SpiritCostMultiplier) &&
                (snapshot.SpiritCostMultiplier >= 0f) &&
                IsFinite(snapshot.ParryGaugeGain) && (snapshot.ParryGaugeGain > 0f) &&
                IsFinite(snapshot.CriticalMultiplierMin) &&
                IsFinite(snapshot.CriticalMultiplierMax) &&
                (snapshot.CriticalMultiplierMin > 0f) &&
                (snapshot.CriticalMultiplierMin <= snapshot.CriticalMultiplierMax) &&
                IsFinite(snapshot.ArrowSpeed) && (snapshot.ArrowSpeed > 0f) &&
                IsFinite(snapshot.ArrowHitRadius) && (snapshot.ArrowHitRadius > 0f) &&
                IsFinite(snapshot.GuidanceDuration) && (snapshot.GuidanceDuration >= 0f) &&
                (snapshot.PierceCount > 0) && (snapshot.BurstShotCount > 0) &&
                IsFinite(snapshot.BurstShotInterval) &&
                ((snapshot.BurstShotCount == 1) || (snapshot.BurstShotInterval > 0f)) &&
                IsFinite(snapshot.HealAmount) && (snapshot.HealAmount > 0f) &&
                IsFinite(snapshot.HealRadius) && (snapshot.HealRadius > 0f) &&
                IsFinite(snapshot.SupportBuffDamageMultiplier) &&
                (snapshot.SupportBuffDamageMultiplier >= 1f) &&
                IsFinite(snapshot.SupportBuffDuration) &&
                (snapshot.SupportBuffDuration >= 0f) &&
                ((snapshot.SupportBuffDamageMultiplier <= 1f) ||
                    (snapshot.SupportBuffDuration > 0f));
        }

        private static float PercentMultiplier(float percent)
        {
            return 1f + (percent * 0.01f);
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
