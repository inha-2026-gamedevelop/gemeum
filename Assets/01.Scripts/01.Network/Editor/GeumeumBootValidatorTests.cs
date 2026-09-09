// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.Network.Tests
{
    public sealed class GeumeumBootValidatorTests
    {
        private readonly List<Object> _createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; ++i)
            {
                if (_createdObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void BootSceneUsesMainProfile()
        {
            Assert.AreEqual(
                GeumeumBootValidator.SceneProfile.Main,
                GeumeumBootValidator.ResolveSceneProfile("Assets/00.Scenes/Release/Boot.unity"));
            Assert.AreEqual(
                GeumeumBootValidator.SceneProfile.Main,
                GeumeumBootValidator.ResolveSceneProfile(Constants.Scene.BOOT));
            Assert.AreEqual(
                GeumeumBootValidator.SceneProfile.DevScene,
                GeumeumBootValidator.ResolveSceneProfile(Constants.Scene.DEV_COMBAT));
        }

        [Test]
        public void ProductionProgressionAssetsProvideCompleteTalentAndScoreData()
        {
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(
                "Assets/09.Data/Combat/CombatConfigSO.asset");
            var talent = AssetDatabase.LoadAssetAtPath<TalentTreeSO>(
                "Assets/09.Data/Talent/TalentTreeSO.asset");
            var score = AssetDatabase.LoadAssetAtPath<ScoreRuleSO>(
                "Assets/09.Data/Score/ScoreRuleSO.asset");
            Assert.IsNotNull(combat);
            Assert.IsNotNull(talent);
            Assert.IsNotNull(score);
            Assert.AreEqual(30, talent.NodeCount);
            var failures = new List<string>();

            GeumeumBootValidator.ValidateProgressionData(combat, talent, score,
                GeumeumBootValidator.SceneProfile.Main, failures);

            Assert.IsEmpty(failures, string.Join(" / ", failures));
        }

        [Test]
        public void ProductionBossRejectsMissingWakePatternThroughBootValidation()
        {
            var source = AssetDatabase.LoadAssetAtPath<BossPatternSO>(
                "Assets/09.Data/Boss/BossPatternSO.asset");
            Assert.IsNotNull(source);
            Assert.IsTrue(source.ValidateAdvancedDefinition(out string failure), failure);
            var boss = Object.Instantiate(source);
            _createdObjects.Add(boss);
            var data = new SerializedObject(boss);
            data.FindProperty("_phases").GetArrayElementAtIndex(0)
                .FindPropertyRelative("WakePatternId").intValue = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
            var failures = new List<string>();

            GeumeumBootValidator.ValidateData(null, null, null, boss,
                GeumeumBootValidator.SceneProfile.Main, failures);

            Assert.IsTrue(failures.Exists(message => message.StartsWith("보스 확장 패턴 정의 오류:")),
                string.Join(" / ", failures));
        }

        [Test]
        public void MissingProgressionDataIsRequiredOnlyForProduction()
        {
            var failures = new List<string>();
            GeumeumBootValidator.ValidateProgressionData(null, null, null,
                GeumeumBootValidator.SceneProfile.DevScene, failures);
            Assert.IsEmpty(failures);

            GeumeumBootValidator.ValidateProgressionData(null, null, null,
                GeumeumBootValidator.SceneProfile.Main, failures);
            Assert.AreEqual(2, failures.Count);
        }

        [Test]
        public void DuplicateTalentAndEvaluationBoundariesRejectProduction()
        {
            var talent = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TalentTreeSO>(
                "Assets/09.Data/Talent/TalentTreeSO.asset"));
            var score = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScoreRuleSO>(
                "Assets/09.Data/Score/ScoreRuleSO.asset"));
            _createdObjects.Add(talent);
            _createdObjects.Add(score);
            var talentData = new SerializedObject(talent);
            SerializedProperty nodes = talentData.FindProperty("_nodes");
            nodes.GetArrayElementAtIndex(1).FindPropertyRelative("NodeId").intValue =
                nodes.GetArrayElementAtIndex(0).FindPropertyRelative("NodeId").intValue;
            talentData.ApplyModifiedPropertiesWithoutUndo();
            var scoreData = new SerializedObject(score);
            scoreData.FindProperty("_evaluationGrades").GetArrayElementAtIndex(1)
                .FindPropertyRelative("MinimumScore").intValue = 0;
            scoreData.ApplyModifiedPropertiesWithoutUndo();
            var failures = new List<string>();

            GeumeumBootValidator.ValidateProgressionData(null, talent, score,
                GeumeumBootValidator.SceneProfile.Main, failures);

            Assert.AreEqual(2, failures.Count);
        }

        [Test]
        public void InvalidCombatExecutionLimitsRejectProduction()
        {
            var combat = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatConfigSO>(
                "Assets/09.Data/Combat/CombatConfigSO.asset"));
            _createdObjects.Add(combat);
            var data = new SerializedObject(combat);
            data.FindProperty("_minimumResourceCost").floatValue = 0f;
            data.FindProperty("_maximumAttackSpeedMultiplier").floatValue = float.NaN;
            data.FindProperty("_burstShotInterval").floatValue = 0f;
            data.FindProperty("_supportBuffDuration").floatValue = -1f;
            data.ApplyModifiedPropertiesWithoutUndo();
            var failures = new List<string>();

            GeumeumBootValidator.ValidateProgressionData(combat,
                AssetDatabase.LoadAssetAtPath<TalentTreeSO>("Assets/09.Data/Talent/TalentTreeSO.asset"),
                AssetDatabase.LoadAssetAtPath<ScoreRuleSO>("Assets/09.Data/Score/ScoreRuleSO.asset"),
                GeumeumBootValidator.SceneProfile.Main, failures);

            Assert.AreEqual(4, failures.Count);
        }

        [Test]
        public void DataValidationAppendsToExistingFailures()
        {
            List<string> failures = new List<string> { "앞선 검사에서 발견한 오류" };

            GeumeumBootValidator.ValidateData(
                null,
                null,
                null,
                null,
                GeumeumBootValidator.SceneProfile.DevScene,
                failures);

            CollectionAssert.Contains(failures, "앞선 검사에서 발견한 오류");
            CollectionAssert.Contains(failures, "결계 데이터가 없다");
        }

        [Test]
        public void SceneValidationAppendsToExistingFailures()
        {
            List<string> failures = new List<string> { "앞선 씬 검사 오류" };

            GeumeumBootValidator.ValidateScene(GeumeumBootValidator.SceneProfile.DevScene, failures);

            CollectionAssert.Contains(failures, "앞선 씬 검사 오류");
        }

        [Test]
        public void NullLevelArrayLookupsAreSafe()
        {
            LevelCurveSO curve = Create<LevelCurveSO>();
            SetField(curve, "_levels", null);

            LevelCurveSO.LevelEntry entry;
            Assert.IsFalse(curve.TryGetLevel(Constants.Combat.START_LEVEL, out entry));
            Assert.AreEqual(0f, curve.GetMaxLevelHealth());
            Assert.AreEqual(0, curve.GetTotalExperienceToMaxLevel());
        }

        [Test]
        public void ProductionSessionPrefabContainsGrowthAndWallet()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/Network/SessionManagers.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<IGrowthProvider>());
            Assert.IsNotNull(prefab.GetComponent<IPartyWallet>());
        }

        [Test]
        public void DevSceneDoesNotRequirePartyServices()
        {
            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateScene(GeumeumBootValidator.SceneProfile.DevScene, failures);

            for (int i = 0; i < failures.Count; ++i)
            {
                StringAssert.DoesNotContain("IGrowthProvider", failures[i]);
                StringAssert.DoesNotContain("IPartyWallet", failures[i]);
            }
        }

        [Test]
        public void MirrorPreprocessedSceneIdentityKeepsEnabledSessionServiceUsable()
        {
            GameObject serviceObject = CreateObject("MirrorPreprocessedSessionService");
            NetworkIdentity identity = serviceObject.AddComponent<NetworkIdentity>();
            identity.sceneId = 1;
            RunStatistics statistics = serviceObject.AddComponent<RunStatistics>();
            serviceObject.SetActive(false);

            Assert.IsTrue(InvokeIsUsableSessionService(statistics));
        }

        [Test]
        public void SessionServiceRejectsDisabledOrUnspawnableInactiveObjects()
        {
            GameObject disabledObject = CreateObject("DisabledSessionService");
            SessionServiceStub disabled = disabledObject.AddComponent<SessionServiceStub>();
            disabled.enabled = false;

            GameObject inactiveObject = CreateObject("InactiveNonNetworkSessionService");
            SessionServiceStub inactive = inactiveObject.AddComponent<SessionServiceStub>();
            inactiveObject.SetActive(false);

            GameObject editorOnlyObject = CreateObject("EditorOnlySessionService");
            editorOnlyObject.tag = "EditorOnly";
            SessionServiceStub editorOnly = editorOnlyObject.AddComponent<SessionServiceStub>();

            Assert.IsFalse(InvokeIsUsableSessionService(disabled));
            Assert.IsFalse(InvokeIsUsableSessionService(inactive));
            Assert.IsFalse(InvokeIsUsableSessionService(editorOnly));
        }

        [Test]
        public void ReleaseValidationReportsMissingGrowthLevel()
        {
            LevelCurveSO curve = Create<LevelCurveSO>();
            LevelCurveSO.LevelEntry[] levels = new LevelCurveSO.LevelEntry[Constants.Combat.MAX_LEVEL - 1];
            int index = 0;
            for (int level = Constants.Combat.START_LEVEL; level <= Constants.Combat.MAX_LEVEL; ++level)
            {
                if (level == 6)
                {
                    continue;
                }

                levels[index++] = new LevelCurveSO.LevelEntry
                {
                    Level = level,
                    MaxHealth = 100f,
                    RequiredExperience = (level == Constants.Combat.MAX_LEVEL) ? 0 : 100,
                };
            }
            SetField(curve, "_levels", levels);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                null,
                curve,
                null,
                null,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            AssertHasFailure(failures, "레벨 곡선에 레벨 6이 없다");
        }

        [TestCase("_phase1AttackIntervalSeconds", 0f, "보스 1페이즈 공격 간격")]
        [TestCase("_phase2AttackIntervalSeconds", -1f, "보스 2페이즈 공격 간격")]
        [TestCase("_enragedAttackIntervalSeconds", float.NaN, "보스 광폭화 공격 간격")]
        [TestCase("_enrageDamageMultiplier", float.PositiveInfinity, "보스 광폭화 피해 배율")]
        public void ReleaseValidationRejectsInvalidBossCadence(string field, float value, string label)
        {
            BossPatternSO boss = Create<BossPatternSO>();
            SetField(boss, field, value);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                null,
                null,
                null,
                boss,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            AssertHasFailure(failures, $"{label}이 유한한 양수가 아니다: {value}");
        }

        [Test]
        public void ReleaseValidationReportsInvalidBossPatternAndPhaseIds()
        {
            BossPatternSO boss = Create<BossPatternSO>();
            BossPatternSO.PatternEntry[] patterns =
            {
                new BossPatternSO.PatternEntry
                {
                    PatternId = 1,
                    TelegraphDuration = 1f,
                    ActiveDuration = 1f,
                    Cooldown = 1f,
                    Damage = 1f,
                    IsParryable = true,
                },
                new BossPatternSO.PatternEntry
                {
                    PatternId = 1,
                    TelegraphDuration = 1f,
                    ActiveDuration = 1f,
                    Cooldown = 1f,
                    Damage = 1f,
                },
            };
            BossPatternSO.PhaseEntry[] phases =
            {
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 1,
                    EnterHealthRatio = 1f,
                    PatternIds = new[] { 1 },
                },
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 2,
                    EnterHealthRatio = 0.5f,
                    PatternIds = new[] { 99 },
                },
            };
            SetField(boss, "_patterns", patterns);
            SetField(boss, "_phases", phases);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                null,
                null,
                null,
                boss,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            AssertHasFailure(failures, "보스 패턴 id가 중복되었다: 1");
            AssertHasFailure(failures, "보스 페이즈 2가 없는 패턴 id를 참조한다: 99");
        }

        [Test]
        public void ReleaseValidationReportsMissingProductionPatternId()
        {
            BossPatternSO boss = Create<BossPatternSO>();
            SetField(boss, "_maxHealth", 100f);

            BossPatternSO.PatternEntry[] patterns = new BossPatternSO.PatternEntry[9];
            for (int i = 0; i < patterns.Length; ++i)
            {
                int patternId = (i < 1) ? 1 : i + 2;
                patterns[i] = new BossPatternSO.PatternEntry
                {
                    PatternId = patternId,
                    TelegraphDuration = 1f,
                    ActiveDuration = 1f,
                    Cooldown = 1f,
                    Damage = 1f,
                    IsParryable = (patternId == 1) || (patternId == 6),
                };
            }

            BossPatternSO.PhaseEntry[] phases =
            {
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 1,
                    EnterHealthRatio = 1f,
                    PatternIds = new[] { 1 },
                },
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 2,
                    EnterHealthRatio = 0.5f,
                    PatternIds = new[] { 6 },
                },
            };
            SetField(boss, "_patterns", patterns);
            SetField(boss, "_phases", phases);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                null,
                null,
                null,
                boss,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            AssertHasFailure(failures, "보스 패턴 id 2가 없다");
        }

        [Test]
        public void ReleaseValidationOrdersBossPhaseRatiosByPhaseId()
        {
            BossPatternSO boss = Create<BossPatternSO>();
            SetField(boss, "_maxHealth", 100f);

            BossPatternSO.PatternEntry[] patterns = new BossPatternSO.PatternEntry[10];
            for (int i = 0; i < patterns.Length; ++i)
            {
                int patternId = i + 1;
                patterns[i] = new BossPatternSO.PatternEntry
                {
                    PatternId = patternId,
                    TelegraphDuration = 1f,
                    ActiveDuration = 1f,
                    Cooldown = 1f,
                    Damage = 1f,
                    IsParryable = (patternId == 1) || (patternId == 6),
                };
            }

            BossPatternSO.PhaseEntry[] phases =
            {
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 2,
                    EnterHealthRatio = 1f,
                    PatternIds = new[] { 6 },
                },
                new BossPatternSO.PhaseEntry
                {
                    PhaseIndex = 1,
                    EnterHealthRatio = 0.5f,
                    PatternIds = new[] { 1 },
                },
            };
            SetField(boss, "_patterns", patterns);
            SetField(boss, "_phases", phases);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                null,
                null,
                null,
                boss,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            AssertHasFailure(failures, "보스 페이즈 진입 HP 비율이 내림차순이 아니다");
        }

        [Test]
        public void ProductionGameDataPassesStrictValidation()
        {
            GameDatabaseSO database =
                AssetDatabase.LoadAssetAtPath<GameDatabaseSO>("Assets/09.Data/Resources/GameDB.asset");
            Assert.IsNotNull(database);

            List<string> failures = new List<string>();
            GeumeumBootValidator.ValidateData(
                database.Barrier,
                database.LevelCurve,
                database.Spawn,
                database.Boss,
                GeumeumBootValidator.SceneProfile.Main,
                failures);

            Assert.IsEmpty(failures, string.Join("\n", failures.ToArray()));
        }

        [Test]
        public void ProductionPlayerServicesPassValidation()
        {
            GameDatabaseSO database =
                AssetDatabase.LoadAssetAtPath<GameDatabaseSO>("Assets/09.Data/Resources/GameDB.asset");
            var failures = new List<string>();
            GeumeumBootValidator.ValidatePlayerServices(database.Combat, database.Shop, failures);
            Assert.IsEmpty(failures, string.Join("\n", failures.ToArray()));
        }

        [Test]
        public void InvalidPlayerServicesPreserveEarlierFailuresAndRejectBadValues()
        {
            CombatConfigSO combat = Create<CombatConfigSO>();
            ShopSO shop = Create<ShopSO>();
            SetField(combat, "_precisionShotHoldDuration", float.NaN);
            SetField(combat, "_precisionShotMoveSpeedMultiplier", 2f);
            SetField(shop, "_items", new[]
            {
                new ShopSO.ItemEntry
                {
                    Kind = ShopItemKind.HealthPotion,
                    Price = -1,
                    EffectValue = float.PositiveInfinity,
                    CarryLimit = 0,
                    MaxStack = 0,
                },
                new ShopSO.ItemEntry
                {
                    Kind = ShopItemKind.HealthPotion,
                    Price = 60,
                    EffectValue = 40f,
                    CarryLimit = 3,
                    MaxStack = 3,
                },
            });
            var failures = new List<string> { "earlier" };
            GeumeumBootValidator.ValidatePlayerServices(combat, shop, failures);
            Assert.AreEqual("earlier", failures[0]);
            Assert.GreaterOrEqual(failures.Count, 6);
        }

        [Test]
        public void EmptyShopRejectsLookupWithoutThrowing()
        {
            ShopSO shop = Create<ShopSO>();
            Assert.IsFalse(shop.TryGetItem(ShopItemKind.HealthPotion, out _));
            var failures = new List<string>();
            GeumeumBootValidator.ValidatePlayerServices(Create<CombatConfigSO>(), shop, failures);
            AssertHasFailure(failures, "객주 상품 데이터가 없다");
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _createdObjects.Add(value);
            return value;
        }

        private GameObject CreateObject(string name)
        {
            GameObject value = new GameObject(name);
            _createdObjects.Add(value);
            return value;
        }

        private static bool InvokeIsUsableSessionService(MonoBehaviour behaviour)
        {
            MethodInfo method = typeof(GeumeumBootValidator).GetMethod(
                "IsUsableSessionService", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool)method.Invoke(null, new object[] { behaviour });
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static void AssertHasFailure(List<string> failures, string expected)
        {
            CollectionAssert.Contains(failures, expected);
        }

        private sealed class SessionServiceStub : MonoBehaviour
        {
        }
    }
}
