// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// Test
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    /// <summary> 성장 적용과 재접속 복구가 HP를 되살리지 않는지 확인한다 </summary>
    public class GrowthParticipantComponentTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject   _player;
        private GameObject   _managerObject;
        private LevelCurveSO _curve;
        private GameDatabaseSO _database;
        private EnemySO _enemy;
        private FakeGrowthProvider _provider;
        private IRunTimeSource _previousRunTimeSource;
        private TestRunTimeSource _testRunTimeSource;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서는 이 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _previousRunTimeSource = RunTimeLocator.Current;
            _testRunTimeSource = new TestRunTimeSource();
            RunTimeLocator.Register(_testRunTimeSource);

            _curve = ScriptableObject.CreateInstance<LevelCurveSO>();
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

            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_levelCurve", _curve);
            _enemy = ScriptableObject.CreateInstance<EnemySO>();
            Set(_enemy, "_stats", new[]
            {
                new EnemySO.StatEntry
                {
                    Kind       = EnemyKind.Heokkaebi,
                    Experience = 50,
                },
            });
            Set(_enemy, "_enhances", new[]
            {
                new EnemySO.EnhanceEntry
                {
                    FieldLevel      = 1,
                    RewardMultiplier = 1.5f,
                },
            });
            Set(_database, "_enemy", _enemy);
            GameDB.Initialize(_database);

            _player = new GameObject("GrowthParticipantTestPlayer", typeof(NetworkIdentity));
            _managerObject = new GameObject("GrowthManagerTest", typeof(NetworkIdentity));
        }

        [TearDown]
        public void TearDown()
        {
            GrowthProviderLocator.Clear();
            GrowthParticipantLocator.Clear();

            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }
            if (_managerObject != null)
            {
                Object.DestroyImmediate(_managerObject);
            }

            GameDB.ResetStatic();
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_curve);
            Object.DestroyImmediate(_enemy);
            RunTimeLocator.Unregister(_testRunTimeSource);
            if (_previousRunTimeSource != null)
            {
                RunTimeLocator.Register(_previousRunTimeSource);
            }
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        [Test]
        public void MaxHealthDeltaAddsToAliveCurrentHealth()
        {
            PlayerStatus status = CreateStatus();
            Assert.IsTrue(status.Restore(new RunParticipantSnapshot
            {
                Health     = 50f,
                LifeState  = PlayerLifeSnapshotState.Alive,
                Level      = 1,
                Experience = 0,
            }));

            status.ServerApplyGrowth(new GrowthState(3, 223, 118f, 2u, 2));

            Assert.AreEqual(118f, status.MaxHealth);
            Assert.AreEqual(68f, status.Health);
            Assert.AreEqual(3, status.Level);
            Assert.AreEqual(223, status.Experience);
        }

        [Test]
        public void MaxHealthDeltaDoesNotReviveDeadParticipant()
        {
            PlayerStatus status = CreateStatus();
            Assert.IsTrue(status.Restore(new RunParticipantSnapshot
            {
                Health     = 0f,
                LifeState  = PlayerLifeSnapshotState.Spectating,
                Level      = 1,
                Experience = 0,
            }));

            status.ServerApplyGrowth(new GrowthState(3, 223, 118f, 2u, 2));

            Assert.AreEqual(118f, status.MaxHealth);
            Assert.AreEqual(0f, status.Health);
            Assert.AreEqual(PlayerLifeState.Spectating, status.LifeState);
        }

        [Test]
        public void ReconnectUsesCurrentPartyGrowthOnce()
        {
            _provider = new FakeGrowthProvider(new GrowthState(3, 223, 118f, 9u, 2));
            GrowthProviderLocator.Register(_provider);

            PlayerStatus status = CreateStatus();
            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                Participant = new ParticipantId(77UL),
                Health      = 50f,
                Stamina     = 0f,
                Spirit      = 0f,
                LifeState   = PlayerLifeSnapshotState.Alive,
                Level       = 1,
                Experience  = 0,
            };

            Assert.IsTrue(status.Restore(snapshot));
            Assert.AreEqual(3, status.Level);
            Assert.AreEqual(223, status.Experience);
            Assert.AreEqual(118f, status.MaxHealth);
            Assert.AreEqual(68f, status.Health);
        }

        [Test]
        public void ReconnectDoesNotReviveDeadParticipant()
        {
            _provider = new FakeGrowthProvider(new GrowthState(3, 223, 118f, 9u, 2));
            GrowthProviderLocator.Register(_provider);

            PlayerStatus status = CreateStatus();
            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                Participant = new ParticipantId(77UL),
                Health      = 0f,
                Stamina     = 0f,
                Spirit      = 0f,
                LifeState   = PlayerLifeSnapshotState.Downed,
                Level       = 1,
                Experience  = 0,
            };

            Assert.IsTrue(status.Restore(snapshot));
            Assert.AreEqual(3, status.Level);
            Assert.AreEqual(118f, status.MaxHealth);
            Assert.AreEqual(0f, status.Health);
            Assert.AreEqual(PlayerLifeState.Downed, status.LifeState);
        }

        [Test]
        public void FailedReconnectGrowthDoesNotCommitParticipantOrLocator()
        {
            PlayerStatus status = CreateStatus();
            ParticipantId previousParticipant = new ParticipantId(88UL);
            Set(status, "_participant", previousParticipant);
            Set(status, "_health", 50f);
            Set(status, "_maxHealth", 109f);
            Set(status, "_level", 2);
            Set(status, "_experience", 100);
            Set(status, "_growthRevision", 4u);
            Set(status, "_lifeState", PlayerLifeState.Alive);
            GrowthParticipantLocator.Register(status);

            GrowthProviderLocator.Register(new FailingGrowthProvider());
            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                Participant = new ParticipantId(77UL),
                Health      = 25f,
                Stamina     = 0f,
                Spirit      = 0f,
                LifeState   = PlayerLifeSnapshotState.Alive,
                Level       = 1,
                Experience  = 0,
            };

            Assert.IsFalse(status.Restore(snapshot));
            Assert.AreEqual(previousParticipant, status.Participant);
            Assert.AreEqual(50f, status.Health);
            Assert.AreEqual(109f, status.MaxHealth);
            Assert.AreEqual(2, status.Level);
            Assert.AreEqual(100, status.Experience);

            Assert.IsTrue(GrowthParticipantLocator.TryGet(previousParticipant,
                out IGrowthParticipant registered));
            Assert.AreSame(status, registered);
            Assert.IsFalse(GrowthParticipantLocator.TryGet(snapshot.Participant,
                out IGrowthParticipant _));
        }

        [Test]
        public void EnemyRewardIsAppliedOnceWithEventRewardMultiplier()
        {
            GrowthManager manager = _managerObject.AddComponent<GrowthManager>();
            Assert.IsTrue(manager.ServerBeginRun());

            EnemyDiedEvent died = EnemyDiedEvent.ForEnemy(11u, EnemyKind.Heokkaebi, Vector3.zero,
                0u, 1, 1.25f);

            Assert.IsTrue(manager.ServerAwardEnemyExperience(died));
            Assert.IsFalse(manager.ServerAwardEnemyExperience(died));
            Assert.AreEqual(62, manager.Experience);

            manager.ServerResetForNewRun();
            Assert.IsFalse(manager.IsRunActive);
            Assert.IsTrue(manager.ServerBeginRun());
            Assert.IsTrue(manager.ServerAwardEnemyExperience(died));
            Assert.AreEqual(62, manager.Experience);
        }

        [Test]
        public void PausedRewardDoesNotConsumeEnemyDeduplication()
        {
            GrowthManager manager = _managerObject.AddComponent<GrowthManager>();
            Assert.IsTrue(manager.ServerBeginRun());

            EnemyDiedEvent died = EnemyDiedEvent.ForEnemy(12u, EnemyKind.Heokkaebi, Vector3.zero,
                0u, 1, 1.25f);

            _testRunTimeSource.IsPaused = true;
            Assert.IsFalse(manager.ServerAwardEnemyExperience(died));
            Assert.AreEqual(0, manager.AwardedEnemyCount);

            _testRunTimeSource.IsPaused = false;
            Assert.IsTrue(manager.ServerAwardEnemyExperience(died));
            Assert.AreEqual(62, manager.Experience);
        }

        [Test]
        public void HostGrowthChangedNotifiesEachInitializationOnce()
        {
            GrowthManager manager = _managerObject.AddComponent<GrowthManager>();
            int notificationCount = 0;
            GrowthState notifiedState = default;
            manager.GrowthChanged += state =>
            {
                ++notificationCount;
                notifiedState = state;
            };

            Assert.IsTrue(manager.ServerBeginRun());
            Assert.AreEqual(1, notificationCount);
            Assert.AreEqual(manager.State, notifiedState);

            Assert.IsTrue(manager.ServerTryAddExperience(1));
            Assert.AreEqual(2, notificationCount);
            Assert.AreEqual(manager.State, notifiedState);

            manager.ServerResetForNewRun();
            Assert.AreEqual(3, notificationCount);
            Assert.AreEqual(manager.State, notifiedState);
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

        private static object Invoke(object target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, PRIVATE_INSTANCE).Invoke(target, arguments);
        }

        private PlayerStatus CreateStatus()
        {
            PlayerStatus status = _player.AddComponent<PlayerStatus>();
            NetworkIdentity identity = _player.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            return status;
        }

        private sealed class FakeGrowthProvider : IGrowthProvider
        {
            private readonly GrowthState _state;

            public FakeGrowthProvider(GrowthState state)
            {
                _state = state;
            }

            public bool TryGetState(ParticipantId participant, out GrowthState state)
            {
                state = _state;
                return participant.IsValid;
            }
        }

        private sealed class FailingGrowthProvider : IGrowthProvider
        {
            public bool TryGetState(ParticipantId participant, out GrowthState state)
            {
                state = default;
                return false;
            }
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds    => 0d;
            public double BossElapsedSeconds     => 0d;
            public double GameplayElapsedSeconds => 0d;
            public RunPauseReason PauseReasons   => RunPauseReason.None;
            public bool IsPaused                 { get; set; }
        }
    }
}
