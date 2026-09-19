// System
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    public sealed class BossTrialRulesTests
    {
        private BossTrialLoadoutSO _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BossTrialLoadoutSO>();
            SetPrivateField(_config, "_loadoutPoints", 100);
            SetPrivateField(_config, "_equipmentTierCosts", new[] { 10, 25, 45, -1 });
            SetPrivateField(_config, "_potionCost", 10);
            SetPrivateField(_config, "_maxPotions", 3);
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void BudgetCountsEquipmentAndPotions()
        {
            var loadout = new BossTrialLoadout { EquipmentTier = 2, PotionCount = 3 };

            Assert.AreEqual(BossTrialLoadoutResult.Success,
                BossTrialRules.Validate(_config, in loadout, out int cost));
            Assert.AreEqual(75, cost);
        }

        [Test]
        public void EmptyLoadoutCostsNothing()
        {
            BossTrialLoadout loadout = BossTrialRules.CreateEmpty();

            Assert.AreEqual(BossTrialLoadoutResult.Success,
                BossTrialRules.Validate(_config, in loadout, out int cost));
            Assert.AreEqual(0, cost);
        }

        [Test]
        public void OverBudgetIsRejected()
        {
            SetPrivateField(_config, "_loadoutPoints", 40);
            var loadout = new BossTrialLoadout { EquipmentTier = 2, PotionCount = 1 };

            Assert.AreEqual(BossTrialLoadoutResult.OverBudget,
                BossTrialRules.Validate(_config, in loadout, out _));
        }

        [Test]
        public void CatalogOutsideTheListIsRejected()
        {
            // 비용이 음수인 등급과 목록 밖 값은 고를 수 없다
            var blocked = new BossTrialLoadout { EquipmentTier = 3, PotionCount = 0 };
            var unknown = new BossTrialLoadout { EquipmentTier = 9, PotionCount = 0 };

            Assert.AreEqual(BossTrialLoadoutResult.TierOutOfRange,
                BossTrialRules.Validate(_config, in blocked, out _));
            Assert.AreEqual(BossTrialLoadoutResult.TierOutOfRange,
                BossTrialRules.Validate(_config, in unknown, out _));
        }

        [Test]
        public void PotionLimitIsEnforcedInBothDirections()
        {
            var tooMany  = new BossTrialLoadout { EquipmentTier = -1, PotionCount = 4 };
            var negative = new BossTrialLoadout { EquipmentTier = -1, PotionCount = -1 };

            Assert.AreEqual(BossTrialLoadoutResult.PotionLimit,
                BossTrialRules.Validate(_config, in tooMany, out _));
            Assert.AreEqual(BossTrialLoadoutResult.PotionLimit,
                BossTrialRules.Validate(_config, in negative, out _));
        }

        [Test]
        public void MissingConfigIsRejectedInsteadOfGivingEverything()
        {
            var loadout = new BossTrialLoadout { EquipmentTier = 0, PotionCount = 0 };

            Assert.AreEqual(BossTrialLoadoutResult.DataMissing,
                BossTrialRules.Validate(null, in loadout, out _));
        }

        [Test]
        public void DefinitionRejectsEmptyCatalogAndNegativeBudget()
        {
            Assert.IsTrue(_config.ValidateDefinition(out string failure), failure);

            SetPrivateField(_config, "_equipmentTierCosts", new int[0]);
            Assert.IsFalse(_config.ValidateDefinition(out _));

            SetPrivateField(_config, "_equipmentTierCosts", new[] { 10 });
            SetPrivateField(_config, "_loadoutPoints", -1);
            Assert.IsFalse(_config.ValidateDefinition(out _));
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
