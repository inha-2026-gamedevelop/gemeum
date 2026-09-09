// System
using System;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    public class EquipmentGeneratorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SEED = 927;
        private const int SAMPLE_COUNT = 1000;
        private EquipmentSO _equipment;
        private DropTableSO _dropTable;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _equipment = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<EquipmentSO>("Assets/09.Data/Equipment/EquipmentSO.asset"));
            _dropTable = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<DropTableSO>("Assets/09.Data/DropTable/DropTableSO.asset"));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_equipment);
            UnityEngine.Object.DestroyImmediate(_dropTable);
        }

        [Test]
        public void SameSeedProducesSameSnapshots()
        {
            var first = CreateGenerator();
            var second = CreateGenerator();
            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                WeaponKind weapon = (WeaponKind) (i % Constants.Combat.WEAPON_KIND_COUNT);
                int field = i % Constants.Combat.FIELD_LEVEL_COUNT;
                Assert.AreEqual(first.Generate(weapon, field), second.Generate(weapon, field));
            }
        }

        [TestCase(WeaponKind.Sword)]
        [TestCase(WeaponKind.Bow)]
        [TestCase(WeaponKind.Bell)]
        public void ProductionDataStaysInRangesWithDistinctOptions(WeaponKind weapon)
        {
            var generator = CreateGenerator();
            _equipment.TryGetWeaponOptions(weapon, out var weaponOptions);
            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                var result = generator.Generate(weapon, i % Constants.Combat.FIELD_LEVEL_COUNT);
                Assert.AreEqual((uint) (i + 1), result.InstanceId);
                Assert.AreEqual(weapon, result.WeaponKind);
                Assert.IsTrue(_equipment.TryGetGrade(result.Grade, out var grade));
                Assert.That(result.AttackPower, Is.InRange(grade.AttackMin, grade.AttackMax));
                AssertOption(result.Option0Kind, result.Option0Value, weaponOptions);
                AssertOption(result.Option1Kind, result.Option1Value, weaponOptions);
                if (grade.OptionChance == 0f)
                {
                    Assert.AreEqual(EquipmentOptionKind.None, result.Option0Kind);
                    Assert.AreEqual(EquipmentOptionKind.None, result.Option1Kind);
                }

                if (grade.OptionCount < 2)
                {
                    Assert.AreEqual(EquipmentOptionKind.None, result.Option1Kind);
                }

                if (result.Grade == ItemGrade.Divine)
                {
                    Assert.AreNotEqual(EquipmentOptionKind.None, result.Option0Kind);
                    Assert.AreNotEqual(EquipmentOptionKind.None, result.Option1Kind);
                    Assert.AreNotEqual(result.Option0Kind, result.Option1Kind);
                }
            }
        }

        [TestCase(0d, ItemGrade.Common)]
        [TestCase(0.699999d, ItemGrade.Common)]
        [TestCase(0.7d, ItemGrade.Talisman)]
        [TestCase(0.95d, ItemGrade.Spiritual)]
        [TestCase(0.999999d, ItemGrade.Spiritual)]
        public void RouletteUsesHalfOpenIntervalsAndSkipsZeroWeight(double roll, ItemGrade expected)
        {
            var generator = new EquipmentGenerator(_equipment, _dropTable, new FixedRandom(roll));
            Assert.AreEqual(expected, generator.Generate(WeaponKind.Sword, 0).Grade);
        }

        [Test]
        public void InvalidRequestDoesNotConsumeIdOrRandom()
        {
            var generator = CreateGenerator();
            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate((WeaponKind) (-1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(WeaponKind.Sword, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate((WeaponKind) 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(WeaponKind.Sword, 4));
            Assert.AreEqual(0u, generator.LastInstanceId);
            Assert.AreEqual(CreateGenerator().Generate(WeaponKind.Sword, 0), generator.Generate(WeaponKind.Sword, 0));
        }

        [Test]
        public void InstanceIdExhaustionNeverWrapsToBasicWeapon()
        {
            var generator = new EquipmentGenerator(_equipment, _dropTable, new System.Random(SEED), uint.MaxValue - 1u);
            Assert.AreEqual(uint.MaxValue, generator.Generate(WeaponKind.Sword, 0).InstanceId);
            Assert.Throws<InvalidOperationException>(() => generator.Generate(WeaponKind.Sword, 0));
            Assert.AreEqual(uint.MaxValue, generator.LastInstanceId);
        }

        [Test]
        public void RerollPreservesIdentityGradeAndGeneratorSequence()
        {
            var generator = CreateGenerator();
            WeaponCombatSnapshot current = generator.Generate(WeaponKind.Sword, 0);
            uint lastInstanceId = generator.LastInstanceId;

            WeaponCombatSnapshot rerolled = generator.Reroll(current);

            Assert.AreEqual(current.InstanceId, rerolled.InstanceId);
            Assert.AreEqual(current.WeaponKind, rerolled.WeaponKind);
            Assert.AreEqual(current.Grade, rerolled.Grade);
            Assert.AreEqual(lastInstanceId, generator.LastInstanceId);
            Assert.IsTrue(InventoryRules.IsValidItem(rerolled));
        }

        [Test]
        public void UpgradePreservesIdentityAndAdvancesExactlyOneGrade()
        {
            var generator = CreateGenerator();
            var current = new WeaponCombatSnapshot
            {
                InstanceId  = 71u,
                WeaponKind  = WeaponKind.Bow,
                Grade       = ItemGrade.Common,
                AttackPower = 10f,
            };

            WeaponCombatSnapshot upgraded = generator.Upgrade(current);

            Assert.AreEqual(current.InstanceId, upgraded.InstanceId);
            Assert.AreEqual(current.WeaponKind, upgraded.WeaponKind);
            Assert.AreEqual(ItemGrade.Talisman, upgraded.Grade);
            Assert.AreEqual(0u, generator.LastInstanceId);
            Assert.IsTrue(InventoryRules.IsValidItem(upgraded));
        }

        [Test]
        public void ServiceRejectsBasicAndMaximumGradeWithoutAllocatingId()
        {
            var generator = CreateGenerator();
            var basic = new WeaponCombatSnapshot
            {
                InstanceId  = 0u,
                WeaponKind  = WeaponKind.Sword,
                Grade       = ItemGrade.Common,
                AttackPower = 10f,
            };
            WeaponCombatSnapshot maximum = basic;
            maximum.InstanceId = 72u;
            maximum.Grade = ItemGrade.Divine;

            Assert.Throws<ArgumentException>(() => generator.Reroll(basic));
            Assert.Throws<InvalidOperationException>(() => generator.Upgrade(maximum));
            Assert.AreEqual(0u, generator.LastInstanceId);
        }

        [Test]
        public void DataIsCapturedAtConstruction()
        {
            var generator = CreateGenerator();
            var expected = CreateGenerator().Generate(WeaponKind.Sword, 0);
            SetField(_equipment, "_grades", null);
            SetField(_equipment, "_commonOptions", null);
            SetField(_equipment, "_weaponOptions", null);
            SetField(_dropTable, "_entries", null);
            Assert.AreEqual(expected, generator.Generate(WeaponKind.Sword, 0));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidWeightRejectsInitialization(float weight)
        {
            var entries = GetField<DropTableSO.FieldLevelEntry[]>(_dropTable, "_entries");
            entries[0].WeightCommon = weight;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void ZeroWeightSumRejectsInitialization()
        {
            GetField<DropTableSO.FieldLevelEntry[]>(_dropTable, "_entries")[0] = default;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [TestCase("_grades")]
        [TestCase("_entries")]
        public void MissingDataRejectsInitialization(string field)
        {
            SetField(field == "_grades" ? (object) _equipment : _dropTable, field, null);
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void NullDependenciesRejectInitialization()
        {
            Assert.Throws<ArgumentNullException>(() => new EquipmentGenerator(null, _dropTable, new System.Random(SEED)));
            Assert.Throws<ArgumentNullException>(() => new EquipmentGenerator(_equipment, null, new System.Random(SEED)));
            Assert.Throws<ArgumentNullException>(() => new EquipmentGenerator(_equipment, _dropTable, null));
        }

        [Test]
        public void ReversedAttackRangeRejectsInitialization()
        {
            GetField<EquipmentSO.GradeEntry[]>(_equipment, "_grades")[0].AttackMin = float.MaxValue;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        public void InvalidOptionChanceRejectsInitialization(float chance)
        {
            GetField<EquipmentSO.GradeEntry[]>(_equipment, "_grades")[1].OptionChance = chance;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void TooManyOptionsRejectsInitialization()
        {
            GetField<EquipmentSO.GradeEntry[]>(_equipment, "_grades")[3].OptionCount = 3;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void DuplicateOptionRejectsInitialization()
        {
            _equipment.CommonOptions[1] = _equipment.CommonOptions[0];
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void ForeignWeaponOptionRejectsInitialization()
        {
            _equipment.TryGetWeaponOptions(WeaponKind.Sword, out var options);
            options[0].Kind = EquipmentOptionKind.ArrowSpeed;
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        [Test]
        public void InsufficientOptionsRejectInitialization()
        {
            SetField(_equipment, "_commonOptions", null);
            SetField(_equipment, "_weaponOptions", null);
            Assert.Throws<ArgumentException>(() => CreateGenerator());
        }

        private EquipmentGenerator CreateGenerator()
        {
            return new EquipmentGenerator(_equipment, _dropTable, new System.Random(SEED));
        }

        private void AssertOption(EquipmentOptionKind kind, float value, EquipmentSO.OptionEntry[] weaponOptions)
        {
            if (kind == EquipmentOptionKind.None)
            {
                Assert.AreEqual(0f, value);
                return;
            }

            foreach (var entry in _equipment.CommonOptions)
            {
                if (entry.Kind == kind)
                {
                    Assert.That(value, Is.InRange(entry.ValueMin, entry.ValueMax));
                    return;
                }
            }

            foreach (var entry in weaponOptions)
            {
                if (entry.Kind == kind)
                {
                    Assert.That(value, Is.InRange(entry.ValueMin, entry.ValueMax));
                    return;
                }
            }

            Assert.Fail($"다른 무기의 옵션이 생성되었습니다: {kind}");
        }

        private static T GetField<T>(object target, string name)
        {
            return (T) target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double _value;

            public FixedRandom(double value) { _value = value; }

            public override double NextDouble() => _value;
        }
    }
}
