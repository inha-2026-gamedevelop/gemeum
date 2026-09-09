// System
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;

// Mirror
using Mirror;

// 서드파티
using NUnit.Framework;
using UnityEngine.TestTools;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat.Tests
{
    /// <summary>부활 관전 시계와 구조 확정의 서버 경계를 검증한다.</summary>
    public sealed class ReviveControllerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private const BindingFlags PRIVATE_STATIC =
            BindingFlags.Static | BindingFlags.NonPublic;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(true, true, ReviveResolutionKind.Rescue)]
        [TestCase(true, false, ReviveResolutionKind.Rescue)]
        [TestCase(false, true, ReviveResolutionKind.Automatic)]
        [TestCase(false, false, ReviveResolutionKind.None)]
        public void RescueResolutionAlwaysHasPriority(bool rescueReady, bool automaticReady,
            ReviveResolutionKind expected)
        {
            Assert.AreEqual(expected,
                ReviveResolutionRules.Select(rescueReady, automaticReady));
        }

        [Test]
        public void PausedSpectateRemainingUsesPauseStart()
        {
            double remaining = (double)InvokeStatic(
                "CalculateSpectateRemainingSeconds", 100.0, 40.0);

            Assert.AreEqual(60.0, remaining, 0.0001);
        }

        [Test]
        public void RestoredSpectateDeadlineAddsOnlyPostRestoreWait()
        {
            double extension = (double)InvokeStatic(
                "CalculateResumeExtensionSeconds", 60.0, 100.0, 70.0, true);

            Assert.AreEqual(30.0, extension, 0.0001);
        }

        [Test]
        public void UnrestoredSpectateDeadlineAddsTheWholePause()
        {
            double extension = (double)InvokeStatic(
                "CalculateResumeExtensionSeconds", 60.0, 100.0, 70.0, false);

            Assert.AreEqual(60.0, extension, 0.0001);
        }

        [Test]
        public void ResumeAtRestoreMomentAddsNoExtraWait()
        {
            double extension = (double)InvokeStatic(
                "CalculateResumeExtensionSeconds", 60.0, 70.0, 70.0, true);

            Assert.AreEqual(0.0, extension, 0.0001);
        }

        [Test]
        public void FinalRescueValidationRejectsMovementDistanceAndStaleGeneration()
        {
            bool wasServerActive = NetworkServer.active;
            GameDatabaseSO database = null;
            CombatConfigSO combat = null;
            GameObject rescuerObject = null;
            GameObject targetObject = null;
            GameObject obstacle = null;

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);

            try
            {
                combat = ScriptableObject.CreateInstance<CombatConfigSO>();
                database = ScriptableObject.CreateInstance<GameDatabaseSO>();
                Set(database, "_combat", combat);
                GameDB.Initialize(database);

                rescuerObject = CreatePlayer("ReviveRescuerTest", PlayerLifeState.Alive,
                    out ReviveController rescuer);
                targetObject = CreatePlayer("ReviveTargetTest", PlayerLifeState.Spectating,
                    out ReviveController target);

                Set(target, "_deathGeneration", 7u);
                Set(rescuer, "_rescueTargetKind", PlayerRescueTargetKind.Spectating);
                Set(rescuer, "_rescueTargetGeneration", 7u);
                Set(rescuer, "_rescueStartDamageGeneration", 0u);

                Assert.IsTrue((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));

                rescuerObject.GetComponent<Rigidbody>().linearVelocity = Vector3.right;
                Assert.IsFalse((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));

                rescuerObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                targetObject.transform.position = Vector3.right * (combat.RescueRadius + 0.01f);
                Assert.IsFalse((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));

                targetObject.transform.position = Vector3.right;
                Set(target, "_deathGeneration", 8u);
                Assert.IsFalse((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));

                Set(target, "_deathGeneration", 7u);
                Set(rescuerObject.GetComponent<PlayerStatus>(),
                    "_serverDamageGeneration", 1u);
                Assert.IsFalse((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));

                Set(rescuerObject.GetComponent<PlayerStatus>(),
                    "_serverDamageGeneration", 0u);
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = "ReviveLineOfSightObstacle";
                obstacle.transform.position = new Vector3(0.5f, 1f, 0f);
                Physics.SyncTransforms();
                Assert.IsFalse((bool)Invoke(rescuer, "ServerCanResolveRescue",
                    100.0, target));
            }
            finally
            {
                if (obstacle != null)
                {
                    Object.DestroyImmediate(obstacle);
                }

                if (rescuerObject != null)
                {
                    Object.DestroyImmediate(rescuerObject);
                }

                if (targetObject != null)
                {
                    Object.DestroyImmediate(targetObject);
                }

                GameDB.ResetStatic();

                if (database != null)
                {
                    Object.DestroyImmediate(database);
                }

                if (combat != null)
                {
                    Object.DestroyImmediate(combat);
                }

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        [Test]
        public void ResolverRegistrationSurvivesLastControllerStopping()
        {
            InvokeStatic("ResetServerControllers");
            SetStatic("G_ResolverRegistered", true);

            GameObject gameObject = new GameObject("ReviveResolverLifetimeTest",
                typeof(NetworkIdentity), typeof(ReviveController));
            ReviveController controller = gameObject.GetComponent<ReviveController>();

            try
            {
                controller.OnStopServer();

                Assert.IsTrue((bool)GetStatic("G_ResolverRegistered"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                InvokeStatic("ResetServerControllers");
            }
        }

        [Test]
        public void SameSchedulerServerRestartReRegistersResolver()
        {
            bool wasServerActive = NetworkServer.active;
            GameObject schedulerObject = null;
            GameObject controllerObject = null;

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);
            InvokeStatic("ResetServerControllers");

            try
            {
                schedulerObject = new GameObject("ReviveSchedulerLifetimeTest",
                    typeof(NetworkIdentity), typeof(AuthorityTickScheduler));
                AuthorityTickScheduler scheduler =
                    schedulerObject.GetComponent<AuthorityTickScheduler>();
                controllerObject = new GameObject("ReviveSchedulerControllerTest",
                    typeof(NetworkIdentity), typeof(ReviveController));
                ReviveController controller = controllerObject.GetComponent<ReviveController>();

                scheduler.OnStartServer();
                ulong firstGeneration = AuthorityTickScheduler.ServerGeneration;
                Invoke(controller, "EnsureResolutionScheduler");
                Invoke(scheduler, "FlushPendingChanges");

                scheduler.OnStopServer();
                scheduler.OnStartServer();
                Assert.AreNotEqual(firstGeneration, AuthorityTickScheduler.ServerGeneration);

                Invoke(controller, "EnsureResolutionScheduler");
                Invoke(scheduler, "FlushPendingChanges");

                object callbacks = Get(scheduler, "_callbacks");
                Assert.AreEqual(1, ((System.Collections.ICollection)callbacks).Count);
            }
            finally
            {
                if (schedulerObject != null)
                {
                    AuthorityTickScheduler scheduler =
                        schedulerObject.GetComponent<AuthorityTickScheduler>();
                    if (AuthorityTickScheduler.ServerInstance == scheduler)
                    {
                        scheduler.OnStopServer();
                    }
                }

                if (controllerObject != null)
                {
                    Object.DestroyImmediate(controllerObject);
                }

                if (schedulerObject != null)
                {
                    Object.DestroyImmediate(schedulerObject);
                }

                InvokeStatic("ResetServerControllers");
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        [Test]
        public void InvalidSpectateRemainingIsRejectedBeforeRestoreMutatesState()
        {
            GameObject gameObject = new GameObject("ReviveInvalidSnapshotTest",
                typeof(NetworkIdentity), typeof(ReviveController));
            ReviveController controller = gameObject.GetComponent<ReviveController>();
            double[] invalidValues =
            {
                -1.0,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity,
            };

            Set(controller, "_deathGeneration", 23u);
            string failure;

            for (int i = 0; i < invalidValues.Length; ++i)
            {
                RunParticipantSnapshot snapshot = new RunParticipantSnapshot
                {
                    SpectateRemainingSeconds = invalidValues[i],
                };

                Assert.IsFalse(controller.Validate(snapshot, out failure));
                Assert.IsFalse(controller.Restore(snapshot));
                Assert.AreEqual(23u, Get(controller, "_deathGeneration"));
                Assert.AreEqual(0.0, Get(controller, "_spectateEndServerTime"));
            }

            Assert.IsFalse(controller.Validate(null, out failure));
            Assert.IsFalse(controller.Restore(null));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void CaptureAndRestorePreserveDeathGeneration()
        {
            GameObject gameObject = new GameObject("ReviveControllerTest",
                typeof(NetworkIdentity), typeof(ReviveController));
            ReviveController controller = gameObject.GetComponent<ReviveController>();

            Set(controller, "_deathGeneration", 23u);
            RunParticipantSnapshot captured = new RunParticipantSnapshot();
            controller.Capture(captured);

            Assert.AreEqual(23u, captured.ReviveDeathGeneration);

            RunParticipantSnapshot restored = new RunParticipantSnapshot
            {
                LifeState                = PlayerLifeSnapshotState.Spectating,
                ReviveDeathGeneration    = captured.ReviveDeathGeneration,
                SpectateRemainingSeconds = 5.0,
            };

            Assert.IsTrue(controller.Restore(restored));
            Assert.AreEqual(23u, Get(controller, "_deathGeneration"));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void LegacySpectatingSnapshotGetsAStableFirstGeneration()
        {
            GameObject gameObject = new GameObject("ReviveLegacySnapshotTest",
                typeof(NetworkIdentity), typeof(ReviveController));
            ReviveController controller = gameObject.GetComponent<ReviveController>();

            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                LifeState                = PlayerLifeSnapshotState.Spectating,
                SpectateRemainingSeconds = 5.0,
                ReviveDeathGeneration    = 0u,
            };

            Assert.IsTrue(controller.Restore(snapshot));
            Assert.AreEqual(1u, Get(controller, "_deathGeneration"));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void ThrowingStatisticsSinkCannotEraseSpectateDeadlineOrEscapeRescueRecording()
        {
            bool wasServerActive = NetworkServer.active;
            GameDatabaseSO database = null;
            CombatConfigSO combat = null;
            GameObject runObject = null;
            GameObject rescuerObject = null;
            GameObject targetObject = null;
            var sink = new ThrowingStatisticsSink();

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);
            RunStatisticsSinkLocator.Register(sink);

            try
            {
                combat = ScriptableObject.CreateInstance<CombatConfigSO>();
                database = ScriptableObject.CreateInstance<GameDatabaseSO>();
                Set(database, "_combat", combat);
                GameDB.Initialize(database);

                runObject = new GameObject("ReviveStatisticsRun",
                    typeof(NetworkIdentity), typeof(RunManager));
                RunManager run = runObject.GetComponent<RunManager>();
                Set(run, "_runIdValue", 1u);
                typeof(RunManager).GetProperty(nameof(RunManager.ServerInstance))
                    .SetValue(null, run);

                rescuerObject = CreatePlayer("ReviveStatisticsRescuer",
                    PlayerLifeState.Downed, out ReviveController rescuer);
                targetObject = CreatePlayer("ReviveStatisticsTarget",
                    PlayerLifeState.Spectating, out ReviveController target);
                InitializeServerIdentity(rescuerObject);
                InitializeServerIdentity(targetObject);
                Set(rescuerObject.GetComponent<PlayerStatus>(), "_participant",
                    new ParticipantId(81UL));
                Set(targetObject.GetComponent<PlayerStatus>(), "_participant",
                    new ParticipantId(82UL));

                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[ReviveController\\] 사망 통계 기록 실패:"));
                Invoke(rescuer, "ServerUpdateSpectate", 100.0);

                Assert.AreEqual(PlayerLifeState.Spectating,
                    rescuerObject.GetComponent<PlayerStatus>().LifeState);
                Assert.Greater((double)Get(rescuer, "_spectateEndServerTime"), 100.0);

                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[ReviveController\\] 구조 통계 기록 실패:"));
                InvokeStatic("RecordRescueFact", rescuer, target, 1u);
                Assert.AreEqual(2, sink.RecordLifeAttempts);
            }
            finally
            {
                RunStatisticsSinkLocator.Unregister(sink);
                typeof(RunManager).GetProperty(nameof(RunManager.ServerInstance))
                    .SetValue(null, null);

                if (rescuerObject != null)
                {
                    Object.DestroyImmediate(rescuerObject);
                }

                if (targetObject != null)
                {
                    Object.DestroyImmediate(targetObject);
                }

                if (runObject != null)
                {
                    Object.DestroyImmediate(runObject);
                }

                GameDB.ResetStatic();
                if (database != null)
                {
                    Object.DestroyImmediate(database);
                }

                if (combat != null)
                {
                    Object.DestroyImmediate(combat);
                }

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject CreatePlayer(string name, PlayerLifeState lifeState,
            out ReviveController controller)
        {
            GameObject gameObject = new GameObject(name,
                typeof(NetworkIdentity), typeof(PlayerStatus), typeof(Rigidbody),
                typeof(CapsuleCollider), typeof(ReviveController));
            controller = gameObject.GetComponent<ReviveController>();

            Invoke(controller, "Awake");
            Set(gameObject.GetComponent<PlayerStatus>(), "_lifeState", lifeState);
            return gameObject;
        }

        private static void InitializeServerIdentity(GameObject gameObject)
        {
            NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, true);
        }

        private static object Get(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, PRIVATE_INSTANCE).GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private static object GetStatic(string fieldName)
        {
            return typeof(ReviveController).GetField(fieldName, PRIVATE_STATIC)
                .GetValue(null);
        }

        private static void SetStatic(string fieldName, object value)
        {
            typeof(ReviveController).GetField(fieldName, PRIVATE_STATIC)
                .SetValue(null, value);
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            return target.GetType().GetMethod(methodName, PRIVATE_INSTANCE)
                .Invoke(target, arguments);
        }

        private static object InvokeStatic(string methodName, params object[] arguments)
        {
            return typeof(ReviveController).GetMethod(methodName, PRIVATE_STATIC)
                .Invoke(null, arguments);
        }

        private sealed class ThrowingStatisticsSink : IRunStatisticsSink
        {
            public int RecordLifeAttempts { get; private set; }

            public bool RecordDamage(in RunAppliedDamageFact fact) => false;
            public bool RecordHealing(in RunAppliedHealingFact fact) => false;

            public bool RecordLife(in RunPlayerLifeFact fact)
            {
                ++RecordLifeAttempts;
                throw new System.InvalidOperationException("statistics test");
            }

            public bool RecordCounter(in RunCounterFact fact) => false;
            public bool RecordParticipantState(in RunParticipantStateFact fact) => false;
        }
    }
}
