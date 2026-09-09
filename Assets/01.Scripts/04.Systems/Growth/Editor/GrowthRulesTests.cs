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
    /// <summary> 레벨 곡선과 공용 XP 계산의 경계 규칙을 확인한다 </summary>
    public class GrowthRulesTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private LevelCurveSO _curve;

        [SetUp]
        public void SetUp()
        {
            _curve = ScriptableObject.CreateInstance<LevelCurveSO>();
            Set(_curve, "_talentPointPerLevel", 1);
            Set(_curve, "_levels", new[]
            {
                Entry(1, 100f, 100),
                Entry(2, 109f, 123),
                Entry(3, 118f, 151),
                Entry(4, 127f, 186),
                Entry(5, 136f, 229),
                Entry(6, 145f, 282),
                Entry(7, 154f, 346),
                Entry(8, 163f, 426),
                Entry(9, 172f, 524),
                Entry(10, 180f, 0),
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_curve);
        }

        [Test]
        public void OneRewardCanProcessSeveralLevels()
        {
            Assert.IsTrue(GrowthRules.TryCreateStartingState(_curve, out GrowthState start));

            bool applied = GrowthRules.TryApplyExperience(_curve, start, 223,
                out GrowthState next, out GrowthApplyResult result);

            Assert.IsTrue(applied);
            Assert.AreEqual(3, next.Level);
            Assert.AreEqual(223, next.Experience);
            Assert.AreEqual(2, result.LevelsGained);
            Assert.AreEqual(18f, result.MaxHealthIncrease);
            Assert.AreEqual(2, next.TalentPoints);
        }

        [Test]
        public void ExactCumulativeThresholdLevelsUp()
        {
            Assert.IsTrue(GrowthRules.TryGetCumulativeExperience(_curve, 3, out int required));
            Assert.AreEqual(223, required);

            GrowthState start = new GrowthState(2, 222, 109f, 1u, 1);
            Assert.IsTrue(GrowthRules.TryApplyExperience(_curve, start, 1,
                out GrowthState next, out GrowthApplyResult result));
            Assert.AreEqual(3, next.Level);
            Assert.AreEqual(223, next.Experience);
            Assert.AreEqual(1, result.LevelsGained);
        }

        [Test]
        public void LevelAndExperienceStopAtTheCurveCap()
        {
            Assert.IsTrue(GrowthRules.TryCreateStartingState(_curve, out GrowthState start));
            Assert.IsTrue(GrowthRules.TryApplyExperience(_curve, start, int.MaxValue,
                out GrowthState maxed, out GrowthApplyResult _unused));

            Assert.AreEqual(Constants.Combat.MAX_LEVEL, maxed.Level);
            Assert.AreEqual(2367, maxed.Experience);
            Assert.IsFalse(GrowthRules.TryApplyExperience(_curve, maxed, 1,
                out GrowthState _unusedNext, out GrowthApplyResult _unusedResult));
        }

        [Test]
        public void RewardMultiplierUsesFlooredPositiveExperience()
        {
            Assert.AreEqual(55, GrowthRules.ResolveRewardExperience(37, 1.5f));
            Assert.AreEqual(0, GrowthRules.ResolveRewardExperience(0, 1.5f));
            Assert.AreEqual(0, GrowthRules.ResolveRewardExperience(37, 0f));
        }

        [Test]
        public void UsableStateMustMatchExperienceIntervalAndCurveStats()
        {
            Assert.IsTrue(GrowthRules.IsUsableState(_curve,
                new GrowthState(3, 223, 118f, 7u, 2)));
            Assert.IsTrue(GrowthRules.IsUsableState(_curve,
                new GrowthState(10, 2367, 180f, 7u, 9)));

            Assert.IsFalse(GrowthRules.IsUsableState(_curve,
                new GrowthState(3, 222, 118f, 7u, 2)));
            Assert.IsFalse(GrowthRules.IsUsableState(_curve,
                new GrowthState(3, 374, 118f, 7u, 2)));
            Assert.IsFalse(GrowthRules.IsUsableState(_curve,
                new GrowthState(3, 223, 127f, 7u, 2)));
            Assert.IsFalse(GrowthRules.IsUsableState(_curve,
                new GrowthState(3, 223, 118f, 7u, 1)));
        }

        [Test]
        public void ExperienceApplicationRejectsTalentPointOverflow()
        {
            Set(_curve, "_talentPointPerLevel", int.MaxValue);
            Assert.IsTrue(GrowthRules.TryCreateStartingState(_curve, out GrowthState start));

            Assert.IsFalse(GrowthRules.TryApplyExperience(_curve, start, 223,
                out GrowthState _unusedNext, out GrowthApplyResult _unusedResult));
        }

        private static LevelCurveSO.LevelEntry Entry(int level, float maxHealth, int requiredExperience)
        {
            return new LevelCurveSO.LevelEntry
            {
                Level              = level,
                MaxHealth          = maxHealth,
                RequiredExperience = requiredExperience,
            };
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }
    }
}
