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
    public sealed class SwallowedLifecycleTests
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
        public void LethalFinalDamageEntersSwallowedWithoutBecomingDead()
        {
            GameObject player = CreatePlayer("SwallowedEntry", new ParticipantId(11UL),
                out PlayerStatus status, out _);

            try
            {
                BossSwallowDamageResult result = EnterSwallowed(status, 5.0);

                Assert.IsTrue(result.EnteredSwallowed);
                Assert.AreEqual(MAX_HEALTH, result.AppliedDamage, 0.0001f);
                Assert.AreEqual(PlayerLifeState.Swallowed, status.LifeState);
                Assert.IsTrue(status.IsSwallowed);
                Assert.IsFalse(status.IsDead);
                Assert.IsFalse(PlayerActionGate.CanAct(
                    RunPhase.Boss, status.LifeState, false));
                Assert.AreEqual(1u, status.SwallowGeneration);
                Assert.AreEqual(SWALLOW_DURATION,
                    status.SwallowedRemainingSeconds, 0.0001);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TenCompetingRescuesCommitExactlyOncePerGeneration()
        {
            GameObject player = CreatePlayer("SwallowedRace", new ParticipantId(21UL),
                out PlayerStatus status, out _);
            ParticipantId first = new ParticipantId(22UL);
            ParticipantId second = new ParticipantId(23UL);

            try
            {
                for (int i = 0; i < 10; ++i)
                {
                    BossSwallowDamageResult entered = EnterSwallowed(status, i * 20.0);
                    ParticipantId winner = ((i & 1) == 0) ? first : second;
                    ParticipantId loser = ((i & 1) == 0) ? second : first;

                    Assert.IsTrue(status.ServerTryRescueSwallowed(
                        winner, entered.SwallowGeneration, RESTORE_RATIO));
                    Assert.IsFalse(status.ServerTryRescueSwallowed(
                        loser, entered.SwallowGeneration, RESTORE_RATIO));
                    Assert.AreEqual(PlayerLifeState.Alive, status.LifeState);
                    Assert.AreEqual(MAX_HEALTH * RESTORE_RATIO,
                        status.Health, 0.0001f);
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TenDeadlineTiesResolveRescueBeforeExpiry()
        {
            GameObject player = CreatePlayer("SwallowedDeadlineRace",
                new ParticipantId(24UL), out PlayerStatus status, out _);

            try
            {
                for (int i = 0; i < 10; ++i)
                {
                    double enteredAt = i * 20.0;
                    BossSwallowDamageResult entered = EnterSwallowed(status, enteredAt);
                    double deadline = enteredAt + SWALLOW_DURATION;

                    Assert.IsTrue(status.ServerTryRescueSwallowed(
                        new ParticipantId(25UL), entered.SwallowGeneration,
                        RESTORE_RATIO));
                    Assert.IsFalse(status.ServerExpireSwallowed(deadline));
                    Assert.AreEqual(PlayerLifeState.Alive, status.LifeState);
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void RescueResolverRunsBeforeBossExpiryTick()
        {
            int priority = (int)typeof(ReviveController)
                .GetField("REVIVE_RESOLUTION_PRIORITY",
                    BindingFlags.Static | BindingFlags.NonPublic)
                .GetRawConstantValue();

            Assert.Less(priority, Constants.Network.TICK_PRIORITY_AI);
        }

        [Test]
        public void ExpiryTransitionsOnceAndMechanicReleaseCannotReviveTheDead()
        {
            GameObject player = CreatePlayer("SwallowedExpiry", new ParticipantId(31UL),
                out PlayerStatus status, out ReviveController revive);

            try
            {
                EnterSwallowed(status, 10.0);

                Assert.IsFalse(status.ServerExpireSwallowed(19.999));
                Assert.IsTrue(status.ServerExpireSwallowed(20.0));
                Assert.IsFalse(status.ServerExpireSwallowed(20.0));
                Assert.AreEqual(PlayerLifeState.Downed, status.LifeState);
                Assert.IsTrue(status.IsDead);
                Assert.IsFalse(status.ServerReleaseSwallowed(RESTORE_RATIO));

                revive.ServerUpdateState(20.0);
                Assert.AreEqual(PlayerLifeState.Spectating, status.LifeState);
                Assert.AreEqual(1u, revive.DeathGeneration);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void ReconnectPreservesRemainingTimeAndGeneration()
        {
            GameObject original = CreatePlayer("SwallowedReconnectSource",
                new ParticipantId(41UL), out PlayerStatus source, out _);
            GameObject replacement = CreatePlayer("SwallowedReconnectTarget",
                new ParticipantId(41UL), out PlayerStatus restored, out _);

            try
            {
                _runTime.GameplayElapsedSeconds = 100.0;
                EnterSwallowed(source, 100.0);
                _runTime.GameplayElapsedSeconds = 104.0;

                RunParticipantSnapshot snapshot = new RunParticipantSnapshot();
                source.Capture(snapshot);
                Assert.AreEqual(6.0, snapshot.SwallowedRemainingSeconds, 0.0001);

                _runTime.GameplayElapsedSeconds = 200.0;
                Assert.IsTrue(restored.Restore(snapshot));
                Assert.AreEqual(PlayerLifeState.Swallowed, restored.LifeState);
                Assert.AreEqual(source.SwallowGeneration,
                    restored.SwallowGeneration);
                Assert.AreEqual(6.0, restored.SwallowedRemainingSeconds, 0.0001);
                Assert.IsTrue(restored.ServerTryRescueSwallowed(
                    new ParticipantId(42UL), snapshot.SwallowGeneration,
                    RESTORE_RATIO));

                BossSwallowDamageResult next = EnterSwallowed(restored, 210.0);
                Assert.AreNotEqual(snapshot.SwallowGeneration,
                    next.SwallowGeneration);
                Assert.IsFalse(restored.ServerTryRescueSwallowed(
                    new ParticipantId(42UL), snapshot.SwallowGeneration,
                    RESTORE_RATIO));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void NewRunResetsSwallowGenerationWithoutChangingTheNextRunContract()
        {
            GameObject player = CreatePlayer("SwallowedTwoRuns", new ParticipantId(51UL),
                out PlayerStatus status, out _);

            try
            {
                Assert.AreEqual(1u, EnterSwallowed(status, 0.0).SwallowGeneration);
                Assert.IsTrue(status.ServerReleaseSwallowed(RESTORE_RATIO));

                status.ServerResetForNewRun();
                Assert.AreEqual(PlayerLifeState.Alive, status.LifeState);
                Assert.AreEqual(0u, status.SwallowGeneration);
                Assert.AreEqual(1u, EnterSwallowed(status, 20.0).SwallowGeneration);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SwallowedRescueUsesTwoSecondChannelConfiguration()
        {
            float duration = (float)InvokeStatic(typeof(ReviveController),
                "ResolveRescueDuration", PlayerRescueTargetKind.Swallowed);

            Assert.AreEqual(2f, duration, 0.0001f);
            Assert.AreNotEqual(GameDB.Combat.RescueDuration, duration);
        }

        [Test]
        public void SwallowedHoldReachesPendingResolutionAfterTwoSeconds()
        {
            const uint targetNetId = 7001u;
            GameObject rescuerObject = CreatePlayer("SwallowedHoldRescuer",
                new ParticipantId(71UL), out PlayerStatus rescuerStatus,
                out ReviveController rescuer);
            GameObject targetObject = CreatePlayer("SwallowedHoldTarget",
                new ParticipantId(72UL), out PlayerStatus targetStatus,
                out _);

            try
            {
                targetObject.transform.position = Vector3.right;
                BossSwallowDamageResult entered = EnterSwallowed(targetStatus, 0.0);
                NetworkServer.spawned.Add(targetNetId,
                    targetObject.GetComponent<NetworkIdentity>());

                Set(rescuer, "_rescueTargetNetId", targetNetId);
                Set(rescuer, "_rescueTargetKind", PlayerRescueTargetKind.Swallowed);
                Set(rescuer, "_rescueTargetGeneration", entered.SwallowGeneration);
                Set(rescuer, "_rescueStartDamageGeneration",
                    rescuerStatus.DamageGeneration);
                Physics.SyncTransforms();

                float duration = GameDB.Boss.SwallowRescueChannelSeconds;
                int maximumSteps = Mathf.CeilToInt(duration / Time.fixedDeltaTime) + 2;
                int completedSteps = 0;
                while ((completedSteps < maximumSteps) &&
                    (!(bool)Get(rescuer, "_pendingRescueResolution")))
                {
                    Invoke(rescuer, "ServerUpdateRescue", 0.0);
                    ++completedSteps;
                }

                Assert.IsTrue((bool)Get(rescuer, "_pendingRescueResolution"));
                Assert.GreaterOrEqual(completedSteps * Time.fixedDeltaTime, duration);
                Assert.LessOrEqual(completedSteps * Time.fixedDeltaTime,
                    duration + (Time.fixedDeltaTime * 2f));
            }
            finally
            {
                NetworkServer.spawned.Remove(targetNetId);
                Object.DestroyImmediate(rescuerObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void ThrowingLifeStateObserverDoesNotStopSwallowedCommitOrTrailingObserver()
        {
            LocalConnectionToClient previousLocalConnection =
                NetworkServer.localConnection;
            GameObject player = CreatePlayer("SwallowedObserverIsolation",
                new ParticipantId(73UL), out PlayerStatus status, out _);
            PlayerLifeState observed = PlayerLifeState.Alive;

            try
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.localConnection))
                    .SetValue(null, new LocalConnectionToClient());
                status.LifeStateChanged += _ =>
                    throw new System.InvalidOperationException("observer test");
                status.LifeStateChanged += state => observed = state;
                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[PlayerStatus\\] 생명 상태 observer 실패:"));

                BossSwallowDamageResult entered = EnterSwallowed(status, 4.0);

                Assert.IsTrue(entered.EnteredSwallowed);
                Assert.AreEqual(PlayerLifeState.Swallowed, status.LifeState);
                Assert.AreEqual(PlayerLifeState.Swallowed, observed);
                Assert.AreEqual(14.0, Get(status, "_swallowedEndGameplayTime"));
            }
            finally
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.localConnection))
                    .SetValue(null, previousLocalConnection);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SnapshotRejectsInvalidSwallowedStateWithoutMutation()
        {
            GameObject player = CreatePlayer("SwallowedInvalidSnapshot",
                new ParticipantId(61UL), out PlayerStatus status, out _);

            try
            {
                RunParticipantSnapshot snapshot = new RunParticipantSnapshot
                {
                    Health = 1f,
                    Stamina = 0f,
                    Spirit = 0f,
                    LifeState = PlayerLifeSnapshotState.Swallowed,
                    SwallowGeneration = 0u,
                    SwallowedRemainingSeconds = double.NaN,
                    Level = Constants.Combat.START_LEVEL,
                };

                Assert.IsFalse(status.Validate(snapshot, out _));
                Assert.IsFalse(status.Restore(snapshot));
                Assert.AreEqual(PlayerLifeState.Alive, status.LifeState);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        private BossSwallowDamageResult EnterSwallowed(PlayerStatus status,
            double gameplayTime)
        {
            _runTime.GameplayElapsedSeconds = gameplayTime;
            return status.ServerApplyBossDamage(
                new DamageInfo(MAX_HEALTH * 2f, DamageInfo.NO_ATTACKER,
                    HitStrength.Heavy),
                new BossSwallowDamageContext(true, gameplayTime,
                    SWALLOW_DURATION));
        }

        private static GameObject CreatePlayer(string name, ParticipantId participant,
            out PlayerStatus status, out ReviveController revive)
        {
            GameObject gameObject = new GameObject(name,
                typeof(NetworkIdentity), typeof(PlayerStatus), typeof(Rigidbody),
                typeof(CapsuleCollider), typeof(ReviveController));
            gameObject.layer = LayerMask.NameToLayer(Constants.Layer.PLAYER);
            status = gameObject.GetComponent<PlayerStatus>();
            revive = gameObject.GetComponent<ReviveController>();

            Invoke(revive, "Awake");
            NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, true);
            Set(status, "_participant", participant);
            Set(status, "_maxHealth", MAX_HEALTH);
            Set(status, "_serverHealth", MAX_HEALTH);
            Set(status, "_health", MAX_HEALTH);
            Set(status, "_lifeState", PlayerLifeState.Alive);
            Set(status, "_resourceStateInitialized", true);
            Set(status, "_level", Constants.Combat.START_LEVEL);
            return gameObject;
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
            public bool IsPaused => false;
        }
    }
}
