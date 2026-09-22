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
                    NodeCount = 3,
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
                _combat, _arrow, _tree, weapon, growth, allocation, 8u,
                out CombatStatsSnapshot stats));

            Assert.AreEqual(1.15f, stats.AttackSpeedMultiplier, 0.0001f);
            Assert.AreEqual(120f, stats.RangedAttackPower, 0.0001f,
                "장비 100 x 특성 피해 1.2. 공명진 계수는 이 snapshot에 들어오지 않는다");
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
                _combat, _arrow, _tree, weapon, growth, allocation, 1u,
                out CombatStatsSnapshot stats));
            Assert.AreEqual(1f, stats.AttackSpeedMultiplier);
            Assert.AreEqual(_combat.RangedStaminaCost * 0.88f,
                stats.ResolveStaminaCost(_combat.RangedStaminaCost), 0.0001f);
        }

        [Test]
        public void BellHealingAndFieldUseAcceptedCombinedValues()
        {
            BuildBellTree(TalentEffectKind.FieldTailSeconds, 0.5f);

            WeaponCombatSnapshot weapon = BellWeapon(25f);
            GrowthState growth = new GrowthState(4, 0, 127f, 2u, 5);
            TalentAllocationSnapshot allocation = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Bell, 5, new[] { 11, 12, 13, 14, 15 });

            Assert.IsTrue(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, weapon, growth, allocation, 3u,
                out CombatStatsSnapshot stats));

            Assert.AreEqual(_combat.HealAmount * 1.25f, stats.HealAmount, 0.0001f);
            Assert.AreEqual(_combat.HealSpiritCost * 0.9f,
                stats.ResolveSpiritCost(_combat.HealSpiritCost), 0.0001f);
            Assert.AreEqual(_combat.HealSpiritCost * 0.9f - 2f,
                stats.ResolveSpiritCost(_combat.HealSpiritCost, 2f), 0.0001f,
                "절대 감소는 비율 감소 뒤에 한 번만 뺀다");

            Assert.AreEqual(_combat.BellFieldRadius + 0.3f, stats.FieldRadius, 0.0001f);
            Assert.AreEqual(_combat.BellFieldDuration, stats.FieldDuration, 0.0001f,
                "끝노드를 사지 않으면 지속은 그대로다");
            Assert.AreEqual(_combat.BellFieldDamageMultiplier,
                stats.FieldDamageMultiplier, 0.0001f);
            Assert.AreEqual(2.375f, stats.FieldTailDuration, 0.0001f,
                "1.5 x (1 + 25%) + 특성 0.5");
            Assert.AreEqual(2f, stats.SkillSpiritCostReduction, 0.0001f);
        }

        [Test]
        public void EquipmentBuffDurationOnlyExtendsTailAndStopsAtCap()
        {
            // 「함께 걷는 울림」: 반경을 넓히는 대신 지속을 깎는다
            BuildBellTree(TalentEffectKind.FieldWideningRadiusBonus, 0.7f);

            WeaponCombatSnapshot weapon = BellWeapon(100f);
            GrowthState growth = new GrowthState(4, 0, 127f, 2u, 5);
            TalentAllocationSnapshot allocation = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Bell, 5, new[] { 11, 12, 13, 14, 15 });

            Assert.IsTrue(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, weapon, growth, allocation, 4u,
                out CombatStatsSnapshot stats));

            Assert.AreEqual(_combat.BellFieldTailMaximum, stats.FieldTailDuration, 0.0001f,
                "장비 401이 커져도 이탈 유지 상한을 넘지 않는다");
            Assert.AreEqual(_combat.BellFieldRadius + 0.3f + 0.7f, stats.FieldRadius, 0.0001f);
            Assert.AreEqual(_combat.BellFieldDuration - _combat.BellFieldWideningDurationPenalty,
                stats.FieldDuration, 0.0001f);
            Assert.AreEqual(_combat.BellFieldDamageMultiplier,
                stats.FieldDamageMultiplier, 0.0001f,
                "장비 401은 진 자체 수명과 공격력 계수에 관여하지 않는다");
        }

        [Test]
        public void RetiredBellEffectsAreRejected()
        {
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Bell, ColumnIndex = 0, NodeCount = 1,
                },
            });
            Set(_tree, "_nodes", new[]
            {
                BellNode(11, 0, TalentEffectKind.SupportBuffPercent, 5f),
            });

            GrowthState growth = new GrowthState(4, 0, 127f, 2u, 1);
            TalentAllocationSnapshot allocation = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Bell, 1, new[] { 11 });

            Assert.IsFalse(CombatStatCalculator.TryBuild(
                _combat, _arrow, _tree, BellWeapon(0f), growth, allocation, 5u, out _),
                "은퇴한 405 노드는 이름만 남기고 적용하지 않는다");
        }

        private void BuildBellTree(TalentEffectKind lastEffect, float lastValue)
        {
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Bell, ColumnIndex = 0, NodeCount = 5,
                },
            });
            Set(_tree, "_nodes", new[]
            {
                BellNode(11, 0, TalentEffectKind.HealAmountPercent, 10f),
                BellNode(12, 1, TalentEffectKind.SpiritCostPercent, -10f),
                BellNode(13, 2, TalentEffectKind.SkillSpiritCost, 2f),
                BellNode(14, 3, TalentEffectKind.FieldRadiusBonus, 0.3f),
                BellNode(15, 4, lastEffect, lastValue),
            });
        }

        private static WeaponCombatSnapshot BellWeapon(float buffDurationPercent)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId   = 2u,
                WeaponKind   = WeaponKind.Bell,
                Grade        = ItemGrade.Talisman,
                AttackPower  = 60f,
                Option0Kind  = EquipmentOptionKind.HealAmount,
                Option0Value = 15f,
                Option1Kind  = EquipmentOptionKind.BuffDuration,
                Option1Value = buffDurationPercent,
            };
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
