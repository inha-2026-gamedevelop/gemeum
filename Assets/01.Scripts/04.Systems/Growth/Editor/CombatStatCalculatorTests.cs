// System
using System.Reflection;

// Unity
using UnityEngine;

// Test
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    public sealed class CombatStatCalculatorTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private CombatConfigSO _combat;
        private ArrowConfigSO _arrow;
        private TalentTreeSO _tree;

        [SetUp]
        public void SetUp()
        {
            _combat = ScriptableObject.CreateInstance<CombatConfigSO>();
            _arrow = ScriptableObject.CreateInstance<ArrowConfigSO>();
            _tree = ScriptableObject.CreateInstance<TalentTreeSO>();
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Bow,
                    ColumnIndex = 0,
                    DisplayName = "연사",
                },
            });
            Set(_tree, "_nodes", new[]
            {
                Node(1, 0, TalentEffectKind.AttackSpeedPercent, 10f),
                Node(2, 1, TalentEffectKind.BurstShotCount, 1f),
                Node(3, 2, TalentEffectKind.RangedDamagePercent, 20f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_tree);
            Object.DestroyImmediate(_arrow);
            Object.DestroyImmediate(_combat);
        }

        [Test]
        public void EquipmentAndTalentFamiliesAddOnceThenApplyCaps()
        {
            Set(_combat, "_maximumAttackSpeedMultiplier", 1.15f);
            Set(_combat, "_minimumResourceCost", 1f);
            Set(_combat, "_burstShotInterval", 0.12f);

            WeaponCombatSnapshot weapon = Weapon(100f,
                EquipmentOptionKind.AttackSpeed, 10f,
                EquipmentOptionKind.PierceCount, 1f);
            GrowthState growth = new GrowthState(4, 0, 127f, 2u, 3);
            TalentAllocationSnapshot allocation = new TalentAllocationSnapshot(
                4u, true, WeaponKind.Bow, 3, new[] { 1, 2, 3 });

            Assert.IsTrue(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, weapon, growth, allocation, 1.1f, 8u,
                out CombatStatsSnapshot stats));

            Assert.AreEqual(1.15f, stats.AttackSpeedMultiplier, 0.0001f);
            Assert.AreEqual(132f, stats.RangedAttackPower, 0.0001f,
                "장비 100 x 특성 피해 1.2 x 지원 버프 1.1");
            Assert.AreEqual(2, stats.PierceCount);
            Assert.AreEqual(2, stats.BurstShotCount);
            Assert.AreEqual(0.12f / 1.15f, stats.BurstShotInterval, 0.0001f);
            Assert.AreEqual(127f, stats.MaxHealth);
        }

        [Test]
        public void ZeroCapsKeepLegacyBaseCostAndSpeedUsable()
        {
            Set(_combat, "_maximumAttackSpeedMultiplier", 0f);
            Set(_combat, "_minimumResourceCost", 0f);
            Set(_tree, "_nodes", new TalentTreeSO.NodeEntry[0]);

            WeaponCombatSnapshot weapon = Weapon(80f,
                EquipmentOptionKind.StaminaCostReduction, -12f,
                EquipmentOptionKind.None, 0f);
            GrowthState growth = new GrowthState(1, 0, 100f, 1u, 0);
            TalentAllocationSnapshot allocation = TalentAllocationSnapshot.Empty(
                WeaponKind.Bow);

            Assert.IsTrue(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, weapon, growth, allocation, 1f, 1u,
                out CombatStatsSnapshot stats));
            Assert.AreEqual(1f, stats.AttackSpeedMultiplier);
            Assert.AreEqual(_combat.RangedStaminaCost * 0.88f,
                stats.ResolveStaminaCost(_combat.RangedStaminaCost), 0.0001f);
        }

        [Test]
        public void BellHealingCostAndSupportUseAcceptedCombinedValues()
        {
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Bell,
                    ColumnIndex = 0,
                },
            });
            Set(_tree, "_nodes", new[]
            {
                BellNode(11, 0, TalentEffectKind.HealAmountPercent, 10f),
                BellNode(12, 1, TalentEffectKind.SpiritCostPercent, -10f),
                BellNode(13, 2, TalentEffectKind.SupportBuffPercent, 5f),
            });

            WeaponCombatSnapshot weapon = new WeaponCombatSnapshot
            {
                InstanceId   = 2u,
                WeaponKind   = WeaponKind.Bell,
                Grade        = ItemGrade.Talisman,
                AttackPower  = 60f,
                Option0Kind  = EquipmentOptionKind.HealAmount,
                Option0Value = 15f,
                Option1Kind  = EquipmentOptionKind.BuffDuration,
                Option1Value = 25f,
            };
            GrowthState growth = new GrowthState(4, 0, 127f, 2u, 3);
            TalentAllocationSnapshot allocation = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Bell, 3, new[] { 11, 12, 13 });

            Assert.IsTrue(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, weapon, growth, allocation, 1f, 3u,
                out CombatStatsSnapshot stats));
            Assert.AreEqual(_combat.HealAmount * 1.25f, stats.HealAmount, 0.0001f);
            Assert.AreEqual(_combat.HealSpiritCost * 0.9f,
                stats.ResolveSpiritCost(_combat.HealSpiritCost), 0.0001f);
            Assert.AreEqual(1.05f, stats.SupportBuffDamageMultiplier, 0.0001f);
            Assert.AreEqual(_combat.SupportBuffDuration * 1.25f,
                stats.SupportBuffDuration, 0.0001f);
        }

        private static TalentTreeSO.NodeEntry Node(int id, int row,
            TalentEffectKind effect, float value)
        {
            return new TalentTreeSO.NodeEntry
            {
                NodeId      = id,
                WeaponKind  = WeaponKind.Bow,
                ColumnIndex = 0,
                RowIndex    = row,
                EffectKind  = effect,
                PointCost   = 1,
                Value       = value,
            };
        }

        private static WeaponCombatSnapshot Weapon(float power,
            EquipmentOptionKind firstKind, float firstValue,
            EquipmentOptionKind secondKind, float secondValue)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId   = 1u,
                WeaponKind   = WeaponKind.Bow,
                Grade        = ItemGrade.Talisman,
                AttackPower  = power,
                Option0Kind  = firstKind,
                Option0Value = firstValue,
                Option1Kind  = secondKind,
                Option1Value = secondValue,
            };
        }

        private static TalentTreeSO.NodeEntry BellNode(int id, int row,
            TalentEffectKind effect, float value)
        {
            return new TalentTreeSO.NodeEntry
            {
                NodeId      = id,
                WeaponKind  = WeaponKind.Bell,
                ColumnIndex = 0,
                RowIndex    = row,
                EffectKind  = effect,
                PointCost   = 1,
                Value       = value,
            };
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, PRIVATE_INSTANCE).SetValue(target, value);
        }
    }
}
