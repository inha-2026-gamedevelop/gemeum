// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary>
    /// 장비와 특성 원본에서 행동 수락 시 쓸 전투 수치를 만든다
    /// 공명진 계수는 여기 들어오지 않는다 - 그 계수는 행동의 자원 비용이 확정되는
    /// 시점에 한 번만 곱해야 해서, 수명이 다른 이 snapshot에 섞으면 두 번 걸린다
    /// </summary>
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

            // 장비 옵션 401. 새 트리에서는 공명진의 이탈 후 유지 시간에만 쓴다
            public float BuffDurationPercent;

            public float BellFirstHitSpiritGain;
            public float EchoRecallSpiritCost;
            public float EchoDurationSeconds;
            public float EchoFieldDamagePercent;
            public float FieldEchoExtendSeconds;
            public float HealSpiritCost;
            public float FieldHealEchoDiscount;
            public float FieldHealAfterglowPercent;
            public float SkillSpiritCost;
            public float FieldRadiusBonus;
            public float FieldTailSeconds;
            public float FieldWideningRadiusBonus;
        }

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryBuild(CombatConfigSO combat, ArrowConfigSO arrow,
            TalentTreeSO tree, in WeaponCombatSnapshot weapon, in GrowthState growth,
            in TalentAllocationSnapshot allocation, uint revision,
            out CombatStatsSnapshot snapshot)
        {
            snapshot = default;
            if ((combat == null) || (arrow == null) || (tree == null) ||
                (!growth.IsValid) || (revision == 0u) || (!IsValidWeapon(weapon)) ||
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
                    meleePower = weapon.AttackPower;
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

            int pierceCount = Mathf.Min(arrow.MaxPierceCount, arrow.BasePierceCount + totals.PierceCount + (int)Positive(totals.PrecisionPierceBonus));
            int burstCount  = 1 + totals.BurstShotCount;

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
                HealRadius                  = combat.HealRadius,
                HealSpiritCostReduction     = Positive(totals.HealSpiritCost),

                FieldRadius                 = ResonanceFieldRules.ResolveRadius(
                    combat.BellFieldRadius, totals.FieldRadiusBonus,
                    totals.FieldWideningRadiusBonus),
                FieldDuration               = ResonanceFieldRules.ResolveDuration(
                    combat.BellFieldDuration, totals.FieldWideningRadiusBonus,
                    combat.BellFieldWideningDurationPenalty),
                FieldTailDuration           = ResonanceFieldRules.ResolveTailDuration(
                    combat.BellFieldTailDuration, totals.BuffDurationPercent,
                    totals.FieldTailSeconds, combat.BellFieldTailMaximum),
                FieldDamageMultiplier       = combat.BellFieldDamageMultiplier,
                SkillSpiritCostReduction    = Positive(totals.SkillSpiritCost),
                FieldEchoExtendSeconds      = Positive(totals.FieldEchoExtendSeconds),
                FieldHealEchoDiscount       = Positive(totals.FieldHealEchoDiscount),
                FieldHealAfterglowRatio     = Mathf.Max(0f,
                    totals.FieldHealAfterglowPercent * 0.01f),

                EchoSpiritCostReduction     = Positive(totals.EchoRecallSpiritCost),
                EchoDurationBonus           = Positive(totals.EchoDurationSeconds),
                EchoFieldDamageMultiplier   = PercentMultiplier(totals.EchoFieldDamagePercent),
                BellFirstHitSpiritGain      = Positive(totals.BellFirstHitSpiritGain),
                BasicStaminaReduction = Positive(totals.BasicStaminaReduction),
                CounterDurationBonus = Positive(totals.CounterDurationBonus),
                ParryStaminaBonus = Positive(totals.ParryStaminaBonus),
                CounterRecoveryReduction = Positive(totals.CounterRecoveryReduction),
                CounterThirdCombo = Positive(totals.CounterThirdCombo),
                SecondComboDamagePercent = Positive(totals.SecondComboDamagePercent),
                ComboWindowBonus = Positive(totals.ComboWindowBonus),
                ThirdHitSkillCooldown = Positive(totals.ThirdHitSkillCooldown),
                SkillSecondCombo = Positive(totals.SkillSecondCombo),
                RollBasicStaminaReduction = Positive(totals.RollBasicStaminaReduction),
                MissSkillRecoveryReduction = Positive(totals.MissSkillRecoveryReduction),
                RollSkillDamagePercent = Positive(totals.RollSkillDamagePercent),
                SideStepSkill = Positive(totals.SideStepSkill),
                BowBasicStaminaReduction = Positive(totals.BowBasicStaminaReduction),
                BowSkillStaminaReduction = Positive(totals.BowSkillStaminaReduction),
                WedgeDurationBonus = Positive(totals.WedgeDurationBonus),
                WedgeFirstHitCooldown = Positive(totals.WedgeFirstHitCooldown),
                WedgeNormalBurstPercent = Positive(totals.WedgeNormalBurstPercent),
                PrecisionStaminaReduction = Positive(totals.PrecisionStaminaReduction),
                WedgePrecisionBurstPercent = Positive(totals.WedgePrecisionBurstPercent),
                PrecisionPierceBonus = Positive(totals.PrecisionPierceBonus),
                PrecisionFollowThrough = Positive(totals.PrecisionFollowThrough),
                BowRollBasicReduction = Positive(totals.BowRollBasicReduction),
                BowComboWindowBonus = Positive(totals.BowComboWindowBonus),
                WedgeRollReduction = Positive(totals.WedgeRollReduction),
                RollShotRecoveryReduction = Positive(totals.RollShotRecoveryReduction),

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
                case EquipmentOptionKind.CounterDuration:
                    if (weapon != WeaponKind.Sword || value < 0f || value > .3f)
                    {
                        return false;
                    }

                    totals.CounterDurationBonus += value;
                    return true;
                case EquipmentOptionKind.ParryGaugeGain:
                    if (weapon != WeaponKind.Sword)
                    {
                        return false;
                    }

                    return false; // 구 200 장비는 새 빌드에 반입하지 않는다
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
                    case TalentEffectKind.SpiritCostPercent:
                        totals.SpiritCostPercent += node.Value;
                        break;

                    case TalentEffectKind.BasicStaminaReduction: totals.BasicStaminaReduction += node.Value; break;
                    case TalentEffectKind.CounterDurationBonus: totals.CounterDurationBonus += node.Value; break;
                    case TalentEffectKind.ParryStaminaBonus: totals.ParryStaminaBonus += node.Value; break;
                    case TalentEffectKind.CounterRecoveryReduction: totals.CounterRecoveryReduction += node.Value; break;
                    case TalentEffectKind.CounterThirdCombo: totals.CounterThirdCombo += node.Value; break;
                    case TalentEffectKind.SecondComboDamagePercent: totals.SecondComboDamagePercent += node.Value; break;
                    case TalentEffectKind.ComboWindowBonus: totals.ComboWindowBonus += node.Value; break;
                    case TalentEffectKind.ThirdHitSkillCooldown: totals.ThirdHitSkillCooldown += node.Value; break;
                    case TalentEffectKind.SkillSecondCombo: totals.SkillSecondCombo += node.Value; break;
                    case TalentEffectKind.RollBasicStaminaReduction: totals.RollBasicStaminaReduction += node.Value; break;
                    case TalentEffectKind.MissSkillRecoveryReduction: totals.MissSkillRecoveryReduction += node.Value; break;
                    case TalentEffectKind.RollSkillDamagePercent: totals.RollSkillDamagePercent += node.Value; break;
                    case TalentEffectKind.SideStepSkill: totals.SideStepSkill += node.Value; break;
                    case TalentEffectKind.BowBasicStaminaReduction: totals.BowBasicStaminaReduction += node.Value; break;
                    case TalentEffectKind.BowSkillStaminaReduction: totals.BowSkillStaminaReduction += node.Value; break;
                    case TalentEffectKind.WedgeDurationBonus: totals.WedgeDurationBonus += node.Value; break;
                    case TalentEffectKind.WedgeFirstHitCooldown: totals.WedgeFirstHitCooldown += node.Value; break;
                    case TalentEffectKind.WedgeNormalBurstPercent: totals.WedgeNormalBurstPercent += node.Value; break;
                    case TalentEffectKind.PrecisionStaminaReduction: totals.PrecisionStaminaReduction += node.Value; break;
                    case TalentEffectKind.WedgePrecisionBurstPercent: totals.WedgePrecisionBurstPercent += node.Value; break;
                    case TalentEffectKind.PrecisionPierceBonus: totals.PrecisionPierceBonus += node.Value; break;
                    case TalentEffectKind.PrecisionFollowThrough: totals.PrecisionFollowThrough += node.Value; break;
                    case TalentEffectKind.BowRollBasicReduction: totals.BowRollBasicReduction += node.Value; break;
                    case TalentEffectKind.BowComboWindowBonus: totals.BowComboWindowBonus += node.Value; break;
                    case TalentEffectKind.WedgeRollReduction: totals.WedgeRollReduction += node.Value; break;
                    case TalentEffectKind.RollShotRecoveryReduction: totals.RollShotRecoveryReduction += node.Value; break;
                    case TalentEffectKind.BellFirstHitSpiritGain:
                        totals.BellFirstHitSpiritGain += node.Value;
                        break;
                    case TalentEffectKind.EchoRecallSpiritCost:
                        totals.EchoRecallSpiritCost += node.Value;
                        break;
                    case TalentEffectKind.EchoDurationSeconds:
                        totals.EchoDurationSeconds += node.Value;
                        break;
                    case TalentEffectKind.EchoFieldDamagePercent:
                        totals.EchoFieldDamagePercent += node.Value;
                        break;
                    case TalentEffectKind.FieldEchoExtendSeconds:
                        totals.FieldEchoExtendSeconds += node.Value;
                        break;
                    case TalentEffectKind.HealSpiritCost:
                        totals.HealSpiritCost += node.Value;
                        break;
                    case TalentEffectKind.FieldHealEchoDiscount:
                        totals.FieldHealEchoDiscount += node.Value;
                        break;
                    case TalentEffectKind.FieldHealAfterglowPercent:
                        totals.FieldHealAfterglowPercent += node.Value;
                        break;
                    case TalentEffectKind.SkillSpiritCost:
                        totals.SkillSpiritCost += node.Value;
                        break;
                    case TalentEffectKind.FieldRadiusBonus:
                        totals.FieldRadiusBonus += node.Value;
                        break;
                    case TalentEffectKind.FieldTailSeconds:
                        totals.FieldTailSeconds += node.Value;
                        break;
                    case TalentEffectKind.FieldWideningRadiusBonus:
                        totals.FieldWideningRadiusBonus += node.Value;
                        break;

                    // 401/403/404/405는 은퇴한 구 방울 노드 효과다. 트리 검증이 먼저
                    // 막지만, 옛 asset이 남아 들어와도 조용히 적용하지 않는다
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
                IsFinite(snapshot.HealSpiritCostReduction) &&
                (snapshot.HealSpiritCostReduction >= 0f) &&
                IsValidFieldSnapshot(snapshot);
        }

        private static bool IsValidFieldSnapshot(in CombatStatsSnapshot snapshot)
        {
            return IsFinite(snapshot.FieldRadius) && (snapshot.FieldRadius > 0f) &&
                IsFinite(snapshot.FieldDuration) && (snapshot.FieldDuration > 0f) &&
                IsFinite(snapshot.FieldTailDuration) &&
                (snapshot.FieldTailDuration >= 0f) &&
                IsFinite(snapshot.FieldDamageMultiplier) &&
                (snapshot.FieldDamageMultiplier >= 1f) &&
                IsFinite(snapshot.SkillSpiritCostReduction) &&
                (snapshot.SkillSpiritCostReduction >= 0f) &&
                IsFinite(snapshot.FieldEchoExtendSeconds) &&
                (snapshot.FieldEchoExtendSeconds >= 0f) &&
                IsFinite(snapshot.FieldHealEchoDiscount) &&
                (snapshot.FieldHealEchoDiscount >= 0f) &&
                IsFinite(snapshot.FieldHealAfterglowRatio) &&
                (snapshot.FieldHealAfterglowRatio >= 0f) &&
                IsFinite(snapshot.EchoSpiritCostReduction) &&
                (snapshot.EchoSpiritCostReduction >= 0f) &&
                IsFinite(snapshot.EchoDurationBonus) &&
                (snapshot.EchoDurationBonus >= 0f) &&
                IsFinite(snapshot.EchoFieldDamageMultiplier) &&
                (snapshot.EchoFieldDamageMultiplier >= 1f) &&
                IsFinite(snapshot.BellFirstHitSpiritGain) &&
                (snapshot.BellFirstHitSpiritGain >= 0f);
        }

        private static float PercentMultiplier(float percent)
        {
            return 1f + (percent * 0.01f);
        }

        private static float Positive(float value)
        {
            return ((IsFinite(value)) && (value > 0f)) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
