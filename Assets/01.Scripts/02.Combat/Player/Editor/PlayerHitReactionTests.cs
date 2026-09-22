// System
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;

// Third party
using Mirror;
using NUnit.Framework;
using UnityEngine.TestTools;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat.Tests
{
    /// <summary> 삼켜짐 전이, 경합, 복구와 구조 시간을 검증한다 </summary>
    public sealed class PlayerHitReactionTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private const float MAX_HEALTH = 100f;
        private const float RESTORE_RATIO = 0.3f;
        private const float SWALLOW_DURATION = 10f;

        private bool _wasServerActive;
        private GameDatabaseSO _database;
        private CombatConfigSO _combat;
        private BossPatternSO _boss;
        private LevelCurveSO _curve;
        private TestRunTimeSource _runTime;
        private IRunTimeSource _previousRunTime;

        [SetUp]
        public void SetUp()
        {
            _wasServerActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);

            _combat = ScriptableObject.CreateInstance<CombatConfigSO>();
            _boss = ScriptableObject.CreateInstance<BossPatternSO>();
            _curve = ScriptableObject.CreateInstance<LevelCurveSO>();
            Set(_curve, "_levels", new[]
            {
                new LevelCurveSO.LevelEntry
                {
                    Level = Constants.Combat.START_LEVEL,
                    MaxHealth = MAX_HEALTH,
                    RequiredExperience = 0,
                },
            });

            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_combat", _combat);
            Set(_database, "_boss", _boss);
            Set(_database, "_levelCurve", _curve);
            GameDB.Initialize(_database);

            GrowthProviderLocator.Clear();
            GrowthParticipantLocator.Clear();
            _previousRunTime = RunTimeLocator.Current;
            _runTime = new TestRunTimeSource();
            RunTimeLocator.Register(_runTime);
        }

        [TearDown]
        public void TearDown()
        {
            GrowthProviderLocator.Clear();
            GrowthParticipantLocator.Clear();
            RunTimeLocator.Unregister(_runTime);
            RunTimeLocator.Register(_previousRunTime);
            GameDB.ResetStatic();

            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_combat);
            Object.DestroyImmediate(_boss);
            Object.DestroyImmediate(_curve);

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, _wasServerActive);
        }

        [Test]
        public void PlayersDoNotPushEachOtherButStillCollideWithWorldAndEnemies()
        {
            int player = LayerMask.NameToLayer(Constants.Layer.PLAYER);
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(player, player));
            foreach (string layer in new[] { "Default", "Ground", "Enemy", "Boss" })
            {
                Assert.IsFalse(Physics.GetIgnoreLayerCollision(player, LayerMask.NameToLayer(layer)), layer);
            }
        }

        private GameObject Create(out PlayerStatus status)
        {
            var go = new GameObject("HitReactionTest", typeof(NetworkIdentity), typeof(PlayerStatus));
            var identity = go.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            status = go.GetComponent<PlayerStatus>();
            Set(status, "_maxHealth", MAX_HEALTH);
            Set(status, "_health", MAX_HEALTH);
            Set(status, "_serverHealth", MAX_HEALTH);
            Set(status, "_resourceStateInitialized", true);
            Set(status, "_level", Constants.Combat.START_LEVEL);
            _runTime.GameplayElapsedSeconds = 10d;
            return go;
        }

        [Test]
        public void ActualHitLocksUntilExactGameplayBoundary()
        {
            var go = Create(out var status);
            try
            {
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Light));
                Assert.AreEqual(90f, status.Health);
                Assert.IsTrue(status.IsHitReacting);
                Assert.AreEqual(1u, status.HitReaction.Generation);
                _runTime.GameplayElapsedSeconds = status.HitReaction.EndGameplayTime - 0.00001;
                Assert.IsTrue(status.IsHitReacting);
                _runTime.GameplayElapsedSeconds = status.HitReaction.EndGameplayTime;
                Assert.IsFalse(status.IsHitReacting);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RepeatedHitsDealDamageWithoutExtendingReactionOrResistance()
        {
            var go = Create(out var status);
            try
            {
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Heavy));
                var first = status.HitReaction;
                _runTime.GameplayElapsedSeconds += 0.1;
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Heavy));
                Assert.AreEqual(first.EndGameplayTime, status.HitReaction.EndGameplayTime);
                _runTime.GameplayElapsedSeconds = first.EndGameplayTime + 0.1;
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Heavy));
                Assert.IsFalse(status.IsHitReacting);
                Assert.AreEqual(first.Generation, status.HitReaction.Generation);
                Assert.AreEqual(70f, status.Health);
                _runTime.GameplayElapsedSeconds = first.EndGameplayTime + _combat.PlayerHitReactionResistanceDuration;
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Heavy));
                Assert.IsTrue(status.IsHitReacting);
                Assert.AreEqual(4u, status.HitReaction.Generation);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0f, 99u, false)]
        [TestCase(10f, 0u, false)]
        [TestCase(10f, 99u, true)]
        public void NoReactionForZeroEnvironmentOrInvincibleDamage(float amount, uint attacker, bool invincible)
        {
            var go = Create(out var status);
            try
            {
                Set(status, "_isInvincible", invincible);
                status.TakeDamage(new DamageInfo(amount, attacker, HitStrength.Light));
                Assert.IsFalse(status.IsHitReacting);
                Assert.AreEqual(0u, status.HitReaction.Generation);
                Assert.AreEqual(invincible ? MAX_HEALTH : MAX_HEALTH - amount, status.Health);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PauseDoesNotConsumeGameplayLockAndLethalDamageOverridesIt()
        {
            var go = Create(out var status);
            try
            {
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Light));
                _runTime.IsPaused = true;
                Assert.IsTrue(status.IsHitReacting);
                status.TakeDamage(new DamageInfo(MAX_HEALTH, 99u, HitStrength.Light));
                Assert.IsTrue(status.IsDead);
                Assert.IsFalse(status.IsHitReacting);
                Assert.AreEqual(0u, status.HitReaction.Generation);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NewRunClearsLockAndRetriggerResistance()
        {
            var go = Create(out var status);
            try
            {
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Light));
                status.ServerResetForNewRun();
                Assert.IsFalse(status.IsHitReacting);
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Light));
                Assert.IsTrue(status.IsHitReacting);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HitCancelsPendingSwingBurstHealRollAndPrecisionHold()
        {
            var go = Create(out var status);
            try
            {
                var attack = go.AddComponent<PlayerAttack>();
                var heal = go.AddComponent<HealController>();
                var roll = go.AddComponent<PlayerRollController>();
                var precision = go.AddComponent<PrecisionShotController>();
                var parry = go.AddComponent<ParryController>();
                Invoke(status, "Awake");
                Invoke(go.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
                Set(attack, "_isSwinging", true);
                Set(attack, "_remainingBurstShots", 2);
                Set(attack, "_recoveryServerTime", 100d);
                Set(heal, "_isCasting", true);
                Set(heal, "_cooldownEndServerTime", 100d);
                Set(roll, "_isRolling", true);
                Set(precision, "_state", PrecisionShotState.Holding);
                Set(precision, "_activeHoldGeneration", 1u);
                Set(parry, "_actionLockEndServerTime", 100d);
                status.TakeDamage(new DamageInfo(10f, 99u, HitStrength.Light));
                Assert.IsFalse((bool)Get(attack, "_isSwinging"));
                Assert.AreEqual(0, Get(attack, "_remainingBurstShots"));
                Assert.IsFalse((bool)Get(heal, "_isCasting"));
                Assert.AreEqual(100d, Get(heal, "_cooldownEndServerTime"));
                Assert.IsFalse(roll.IsRolling);
                Assert.AreEqual(PrecisionShotState.Idle, Get(precision, "_state"));
                Assert.IsFalse(parry.IsActionInProgress(10d));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase("ChasaLocomotion")]
        [TestCase("SingungLocomotion")]
        [TestCase("MunyeoLocomotion")]
        [TestCase("BowLocomotion")]
        [TestCase("PlayerLocomotion")]
        public void EveryPlayerControllerHasNonLoopingHumanoidHitState(string name)
        {
            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/05.Animator/" + name + ".controller");
            Assert.IsNotNull(controller);
            UnityEditor.Animations.AnimatorState hit = null;
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                if (state.state.name == "HitReaction") { hit = state.state; }
            }

            Assert.IsNotNull(hit);
            var clip = hit.motion as AnimationClip;
            Assert.IsNotNull(clip);
            Assert.IsTrue(clip.humanMotion);
            Assert.IsFalse(clip.isLooping);
            Assert.IsTrue(hit.speedParameterActive);
            Assert.AreEqual("HitReactionSpeed", hit.speedParameter);
        }

        private static object Invoke(object target, string methodName,
            params object[] arguments)
        {
            return target.GetType().GetMethod(methodName, PRIVATE_INSTANCE)
                .Invoke(target, arguments);
        }

        private static object InvokeStatic(System.Type type, string methodName,
            params object[] arguments)
        {
            return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);
        }

        private static object Get(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, PRIVATE_INSTANCE)
                .GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE)
                .SetValue(target, value);
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0.0;
            public double GameplayElapsedSeconds { get; set; }
            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool IsPaused { get; set; }
        }
    }
}
