// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat.Tests
{
    /// <summary> 정밀 사격 홀드 경계와 요청 세대 수명을 검증한다 </summary>
    public sealed class PrecisionShotControllerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ReleaseBeforeHoldBoundaryCancelsWithoutFiring()
        {
            PrecisionReleaseDecision decision = PrecisionShotRules.ResolveRelease(
                PrecisionShotState.Holding, 3u, 3u, 10.0, 10.5999, 0.6f);

            Assert.AreEqual(PrecisionReleaseDecision.Cancel, decision);
        }

        [TestCase(PrecisionShotState.Holding)]
        [TestCase(PrecisionShotState.Ready)]
        public void ReleaseAtExactBoundaryFiresBeforeNextServerTick(PrecisionShotState state)
        {
            double exactBoundary = 10.0 + (double)0.6f;
            PrecisionReleaseDecision decision = PrecisionShotRules.ResolveRelease(
                state, 3u, 3u, 10.0, exactBoundary, 0.6f);

            Assert.AreEqual(PrecisionReleaseDecision.Fire, decision);
        }

        [Test]
        public void StaleHoldGenerationCannotReleaseCurrentHold()
        {
            PrecisionReleaseDecision decision = PrecisionShotRules.ResolveRelease(
                PrecisionShotState.Ready, 4u, 3u, 10.0, 11.0, 0.6f);

            Assert.AreEqual(PrecisionReleaseDecision.Reject, decision);
        }

        [Test]
        public void AimRayRejectsNonFiniteNonNormalizedAndRemoteOrigins()
        {
            Vector3 player = Vector3.zero;

            Assert.IsFalse(PrecisionShotRules.IsValidAimRay(new PrecisionAimRay
            {
                Origin    = new Vector3(float.NaN, 0f, 0f),
                Direction = Vector3.forward,
            }, player, 10f));
            Assert.IsFalse(PrecisionShotRules.IsValidAimRay(new PrecisionAimRay
            {
                Origin    = Vector3.zero,
                Direction = Vector3.forward * 2f,
            }, player, 10f));
            Assert.IsFalse(PrecisionShotRules.IsValidAimRay(new PrecisionAimRay
            {
                Origin    = Vector3.right * 10.01f,
                Direction = Vector3.forward,
            }, player, 10f));
            Assert.IsTrue(PrecisionShotRules.IsValidAimRay(new PrecisionAimRay
            {
                Origin    = Vector3.right * 10f,
                Direction = Vector3.forward,
            }, player, 10f));
        }

        [Test]
        public void DelayedBeginFromPreviousRunOrSceneIsRejected()
        {
            Assert.IsFalse(PrecisionShotRules.IsCurrentRequestContext(8u, 2u, 9u, 2u));
            Assert.IsFalse(PrecisionShotRules.IsCurrentRequestContext(9u, 1u, 9u, 2u));
            Assert.IsTrue(PrecisionShotRules.IsCurrentRequestContext(9u, 2u, 9u, 2u));
        }

        [Test]
        public void DisableCancelsServerHoldAndMovementReduction()
        {
            bool wasServerActive = NetworkServer.active;
            GameObject owner = CreateServerController(out PrecisionShotController controller);

            try
            {
                Set(controller, "_state", PrecisionShotState.Ready);
                Set(controller, "_activeRunIdValue", 7u);
                Set(controller, "_activeSceneRevisionValue", 2u);
                Set(controller, "_activeHoldGeneration", 4u);

                Invoke(controller, "OnDisable");

                Assert.AreEqual(PrecisionShotState.Idle, controller.State);
                Assert.AreEqual(0u, controller.HoldGeneration);
                Assert.AreEqual(1f, controller.ResolveMovementMultiplier(true));
            }
            finally
            {
                SetIsServer(owner.GetComponent<NetworkIdentity>(), false);
                Object.DestroyImmediate(owner);
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        [Test]
        public void NewRunCancelPreservesRequestAndGenerationHighWater()
        {
            bool wasServerActive = NetworkServer.active;
            GameObject owner = CreateServerController(out PrecisionShotController controller);

            try
            {
                RequestIdTracker tracker = default;
                Assert.IsTrue(tracker.TryAccept(50u));
                Set(controller, "_requestIds", tracker);
                Set(controller, "_lastServerGeneration", 80u);
                Set(controller, "_nextServerActionId", 90u);
                Set(controller, "_state", PrecisionShotState.Holding);

                controller.ResetForNewRun();

                RequestIdTracker preserved = (RequestIdTracker)Get(controller, "_requestIds");
                Assert.IsFalse(preserved.TryAccept(50u));
                Assert.AreEqual(80u, Get(controller, "_lastServerGeneration"));
                Assert.AreEqual(90u, Get(controller, "_nextServerActionId"));
                Assert.AreEqual(PrecisionShotState.Idle, controller.State);
            }
            finally
            {
                SetIsServer(owner.GetComponent<NetworkIdentity>(), false);
                Object.DestroyImmediate(owner);
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        [Test]
        public void ReadyReleaseSpendsStaminaAndSpawnsOneArrowOnlyOnce()
        {
            using (var fixture = new PrecisionServerFixture(0))
            {
                fixture.PrepareReadyHold();
                float staminaBefore = fixture.Status.Stamina;

                PrecisionReleaseDecision first = fixture.Release(1u, out ActionRejection firstRejection);

                Assert.AreEqual(PrecisionReleaseDecision.Fire, first);
                Assert.AreEqual(ActionRejection.None, firstRejection);
                Assert.AreEqual(staminaBefore - 15f, fixture.Status.Stamina);
                Assert.AreEqual(1, fixture.SpawnedArrowCount);

                PrecisionReleaseDecision retransmit = fixture.Release(1u, out _);

                Assert.AreEqual(PrecisionReleaseDecision.Reject, retransmit);
                Assert.AreEqual(staminaBefore - 15f, fixture.Status.Stamina);
                Assert.AreEqual(1, fixture.SpawnedArrowCount);
            }
        }

        [Test]
        public void MuzzleStartOverlapRejectsWithoutCostOrArrow()
        {
            using (var fixture = new PrecisionServerFixture(1 << 0))
            {
                fixture.PrepareReadyHold();
                Assert.AreEqual(
                    PrecisionReleaseDecision.Fire,
                    fixture.Release(1u, out ActionRejection prerequisiteRejection));
                Assert.AreEqual(ActionRejection.None, prerequisiteRejection);
                Assert.AreEqual(1, fixture.SpawnedArrowCount);

                fixture.PrepareReadyHold();
                float staminaBefore = fixture.Status.Stamina;
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "PrecisionMuzzleOverlapWall";
                wall.layer = 0;
                wall.transform.position = new Vector3(0f, 1.2f, 0.6f);
                Physics.SyncTransforms();

                try
                {
                    PrecisionReleaseDecision decision = fixture.Release(
                        2u, out ActionRejection rejection);

                    Assert.AreEqual(PrecisionReleaseDecision.Reject, decision);
                    Assert.AreEqual(ActionRejection.NotReady, rejection);
                    Assert.AreEqual(staminaBefore, fixture.Status.Stamina);
                    Assert.AreEqual(1, fixture.SpawnedArrowCount);
                }
                finally
                {
                    Object.DestroyImmediate(wall);
                }
            }
        }

        [Test]
        public void PrecisionArrowUsesHitTargetsOwnPartReference()
        {
            bool wasServerActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            GameObject arrowObject = new GameObject(
                "PrecisionPartRefArrow", typeof(NetworkIdentity), typeof(ArrowProjectile));
            GameObject targetObject = new GameObject("PrecisionPartRefTarget");
            PrecisionTargetStub target = targetObject.AddComponent<PrecisionTargetStub>();
            GameObject hitPart = new GameObject("PrecisionPartRefCollider", typeof(SphereCollider));
            hitPart.transform.SetParent(targetObject.transform);

            try
            {
                NetworkIdentity identity = arrowObject.GetComponent<NetworkIdentity>();
                Invoke(identity, "InitializeNetworkBehaviours");
                SetIsServer(identity, true);

                ArrowProjectile arrow = arrowObject.GetComponent<ArrowProjectile>();
                Set(arrow, "_actionId", 4u);
                Set(arrow, "_attacker", new ParticipantId(9UL));
                Set(arrow, "_attackerNetId", 17u);
                Set(arrow, "_runIdValue", 7u);
                Set(arrow, "_sceneRevisionValue", 2u);
                target.Target = TargetRef.Create(
                    new RunId(7u), new SceneRevision(2u), 99u, 3);

                Invoke(arrow, "TryInterruptPrecisionTarget", hitPart.GetComponent<Collider>());

                Assert.AreEqual(1, target.InterruptCount);
                Assert.AreEqual(3, target.LastContext.Target.PartId);
                Assert.AreEqual(target.Target, target.LastContext.Target);
            }
            finally
            {
                SetIsServer(arrowObject.GetComponent<NetworkIdentity>(), false);
                Object.DestroyImmediate(hitPart);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(arrowObject);
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject CreateServerController(out PrecisionShotController controller)
        {
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            GameObject owner = new GameObject(
                "PrecisionShotControllerTest",
                typeof(NetworkIdentity),
                typeof(PrecisionShotController));
            NetworkIdentity identity = owner.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            SetIsServer(identity, true);
            controller = owner.GetComponent<PrecisionShotController>();
            return owner;
        }

        private static void SetIsServer(NetworkIdentity identity, bool value)
        {
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, value);
        }

        private static object Get(object target, string fieldName)
        {
            return Field(target, fieldName).GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            Field(target, fieldName).SetValue(target, value);
        }

        private static FieldInfo Field(object target, string fieldName)
        {
            System.Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    PRIVATE_INSTANCE | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            throw new System.MissingFieldException(target.GetType().FullName, fieldName);
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            return target.GetType().GetMethod(methodName, PRIVATE_INSTANCE).Invoke(target, arguments);
        }

        private static void SetStaticProperty(System.Type type, string propertyName, object value)
        {
            type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, value);
        }

        private sealed class PrecisionServerFixture : System.IDisposable
        {
            private const uint PLAYER_NET_ID = 17u;
            private const uint RUN_ID = 7u;
            private const uint SCENE_REVISION = 2u;
            private const uint HOLD_GENERATION = 5u;

            private readonly bool _wasServerActive;
            private readonly RunManager _previousRun;
            private readonly RunSceneDirector _previousDirector;
            private readonly GeumeumAuthenticator _previousAuthenticator;
            private readonly IRunTimeSource _previousRunTime;

            private readonly GameObject _playerObject;
            private readonly GameObject _arrowPrefabObject;
            private readonly GameObject _runObject;
            private readonly GameObject _directorObject;
            private readonly GameObject _authenticatorObject;
            private readonly GameDatabaseSO _database;
            private readonly CombatConfigSO _combat;
            private readonly ArrowConfigSO _arrowConfig;
            private readonly CameraConfigSO _camera;
            private readonly TestRunTimeSource _runTime;
            private readonly ArrowProjectile _arrowPrefab;
            private readonly TestPlayerAttack _attack;
            private readonly NetworkConnectionToClient _connection;

            public PlayerStatus Status { get; }
            public PrecisionShotController Controller { get; }

            public int SpawnedArrowCount => _attack.SpawnCount;

            public PrecisionServerFixture(int worldMask)
            {
                _wasServerActive = NetworkServer.active;
                _previousRun = RunManager.ServerInstance;
                _previousDirector = RunSceneDirector.ServerInstance;
                _previousAuthenticator = GeumeumAuthenticator.ServerInstance;
                _previousRunTime = RunTimeLocator.Current;

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, true);

                _combat = ScriptableObject.CreateInstance<CombatConfigSO>();
                _arrowConfig = ScriptableObject.CreateInstance<ArrowConfigSO>();
                _camera = ScriptableObject.CreateInstance<CameraConfigSO>();
                _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
                Set(_combat, "_precisionShotStaminaCost", 15f);
                Set(_arrowConfig, "_worldMask", (LayerMask)worldMask);
                Set(_database, "_combat", _combat);
                Set(_database, "_arrow", _arrowConfig);
                Set(_database, "_camera", _camera);
                GameDB.Initialize(_database);

                _runObject = new GameObject(
                    "PrecisionShotTestRun", typeof(NetworkIdentity), typeof(RunManager));
                RunManager run = _runObject.GetComponent<RunManager>();
                Set(run, "_runIdValue", RUN_ID);
                Set(run, "_phase", RunPhase.Farming);
                SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), run);

                _directorObject = new GameObject(
                    "PrecisionShotTestDirector", typeof(NetworkIdentity), typeof(RunSceneDirector));
                RunSceneDirector director = _directorObject.GetComponent<RunSceneDirector>();
                Set(director, "_revisionValue", SCENE_REVISION);
                SetStaticProperty(
                    typeof(RunSceneDirector), nameof(RunSceneDirector.ServerInstance), director);

                _runTime = new TestRunTimeSource { GameplayElapsedSeconds = 10d };
                RunTimeLocator.Register(_runTime);

                _authenticatorObject = new GameObject("PrecisionShotTestAuthenticator");
                GeumeumAuthenticator authenticator =
                    _authenticatorObject.AddComponent<GeumeumAuthenticator>();
                SetStaticProperty(
                    typeof(GeumeumAuthenticator),
                    nameof(GeumeumAuthenticator.ServerInstance),
                    authenticator);
                var acceptedIds = (Dictionary<int, PlayerPlatformId>)Get(
                    authenticator, "_acceptedIds");
                _connection = new NetworkConnectionToClient(42);
                acceptedIds.Add(_connection.connectionId, new PlayerPlatformId(9UL));

                _arrowPrefabObject = new GameObject(
                    "PrecisionShotTestArrow", typeof(NetworkIdentity), typeof(ArrowProjectile));
                _arrowPrefab = _arrowPrefabObject.GetComponent<ArrowProjectile>();

                _playerObject = new GameObject("PrecisionShotTestPlayer");
                _playerObject.SetActive(false);
                NetworkIdentity identity = _playerObject.AddComponent<NetworkIdentity>();
                Status = _playerObject.AddComponent<PlayerStatus>();
                PlayerWeapon weapon = _playerObject.AddComponent<PlayerWeapon>();
                _playerObject.AddComponent<PlayerPresentation>();
                _playerObject.AddComponent<TestLoadoutProvider>();
                _attack = _playerObject.AddComponent<TestPlayerAttack>();
                Controller = _playerObject.AddComponent<PrecisionShotController>();

                typeof(PlayerAttack).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(_attack, null);
                typeof(PrecisionShotController).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(Controller, null);

                Invoke(identity, "InitializeNetworkBehaviours");
                SetIsServer(identity, true);
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId))
                    .SetValue(identity, PLAYER_NET_ID);
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.connectionToClient))
                    .SetValue(identity, _connection);

                Set(Status, "_stamina", 100f);
                Set(Status, "_lifeState", PlayerLifeState.Alive);
                Set(Status, "_participant", new ParticipantId(9UL));
                Set(weapon, "_weapon", WeaponKind.Bow);
                Set(weapon, "_hasWeapon", true);
                Set(_attack, "_targetMask", (LayerMask)0);
                Set(_attack, "_arrowPrefab", _arrowPrefab);
            }

            public void PrepareReadyHold()
            {
                Set(Controller, "_state", PrecisionShotState.Ready);
                Set(Controller, "_activeRunIdValue", RUN_ID);
                Set(Controller, "_activeSceneRevisionValue", SCENE_REVISION);
                Set(Controller, "_activeHoldGeneration", HOLD_GENERATION);
                Set(Controller, "_holdStartGameTime", 9d);
                Set(Controller, "_recoveryEndGameTime", 0d);
            }

            public PrecisionReleaseDecision Release(
                uint requestId,
                out ActionRejection rejection)
            {
                object[] arguments =
                {
                    RUN_ID,
                    SCENE_REVISION,
                    requestId,
                    HOLD_GENERATION,
                    new PrecisionAimRay
                    {
                        Origin = new Vector3(0f, 1.6f, -4.5f),
                        Direction = Vector3.forward,
                    },
                    ActionRejection.None,
                };

                PrecisionReleaseDecision decision = (PrecisionReleaseDecision)Invoke(
                    Controller, "ServerResolveRelease", arguments);
                rejection = (ActionRejection)arguments[5];
                return decision;
            }

            public void Dispose()
            {
                SetIsServer(_playerObject.GetComponent<NetworkIdentity>(), false);
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.connectionToClient))
                    .SetValue(_playerObject.GetComponent<NetworkIdentity>(), null);
                Object.DestroyImmediate(_playerObject);
                Object.DestroyImmediate(_arrowPrefabObject);
                Object.DestroyImmediate(_authenticatorObject);
                Object.DestroyImmediate(_directorObject);
                Object.DestroyImmediate(_runObject);

                SetStaticProperty(
                    typeof(RunSceneDirector),
                    nameof(RunSceneDirector.ServerInstance),
                    _previousDirector);
                SetStaticProperty(
                    typeof(RunManager), nameof(RunManager.ServerInstance), _previousRun);
                SetStaticProperty(
                    typeof(GeumeumAuthenticator),
                    nameof(GeumeumAuthenticator.ServerInstance),
                    _previousAuthenticator);

                RunTimeLocator.Unregister(_runTime);
                if (_previousRunTime != null)
                {
                    RunTimeLocator.Register(_previousRunTime);
                }

                GameDB.ResetStatic();
                Object.DestroyImmediate(_database);
                Object.DestroyImmediate(_camera);
                Object.DestroyImmediate(_arrowConfig);
                Object.DestroyImmediate(_combat);

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, _wasServerActive);
            }
        }

        private sealed class TestLoadoutProvider : MonoBehaviour, ICombatLoadoutProvider
        {
            public uint Revision => 1u;

            public bool TryBuildLoadout(
                ParticipantId participant,
                out CombatStatsSnapshot snapshot)
            {
                snapshot = new CombatStatsSnapshot
                {
                    Revision                    = 1u,
                    MaxHealth                   = 100f,
                    MeleeAttackPower            = 10f,
                    RangedAttackPower           = 20f,
                    AttackSpeedMultiplier       = 1f,
                    StaminaCostMultiplier       = 1f,
                    SpiritCostMultiplier        = 1f,
                    CriticalMultiplierMin       = 2f,
                    CriticalMultiplierMax       = 3f,
                    ThirdComboDamageMultiplier  = 1f,
                    ArrowSpeed                  = 30f,
                    ArrowHitRadius              = 0.4f,
                    GuidanceDuration            = 0.4f,
                    PierceCount                 = 1,
                    BurstShotCount              = 1,
                    HealAmount                  = 40f,
                    HealRadius                  = 6f,
                    SupportBuffDamageMultiplier = 1f,
                    SupportBuffDuration         = 6f,
                };
                return participant.IsValid;
            }
        }

        private sealed class TestPlayerAttack : PlayerAttack
        {
            public int SpawnCount { get; private set; }

            protected override void SpawnAcceptedArrow(
                in ArrowFireSnapshot snapshot,
                ArrowConfigSO arrowConfig)
            {
                ++SpawnCount;
            }
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0d;
            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool IsPaused => false;
        }

        private sealed class PrecisionTargetStub : MonoBehaviour, IPrecisionInterruptTarget
        {
            public TargetRef Target { get; set; }
            public TargetRef PrecisionTargetRef => Target;
            public int InterruptCount { get; private set; }
            public PrecisionInterruptContext LastContext { get; private set; }

            public bool TryInterruptPrecisionShot(in PrecisionInterruptContext context)
            {
                ++InterruptCount;
                LastContext = context;
                return true;
            }
        }
    }
}
