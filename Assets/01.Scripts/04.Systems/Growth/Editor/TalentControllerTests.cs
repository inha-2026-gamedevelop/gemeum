// System
using System;
using System.Reflection;

// Unity
using UnityEngine;
using Object = UnityEngine.Object;

// 서드파티
using Mirror;

// Test
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    public sealed class TalentControllerTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _player;
        private GameDatabaseSO _database;
        private TalentTreeSO _tree;
        private LevelCurveSO _curve;
        private TalentController _controller;
        private TestLoadoutRevisionTarget _loadout;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _tree = ScriptableObject.CreateInstance<TalentTreeSO>();
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Sword,
                    ColumnIndex = 0,
                },
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Bow,
                    ColumnIndex = 0,
                },
            });
            Set(_tree, "_nodes", new[]
            {
                Node(1, WeaponKind.Sword, 0, TalentEffectKind.ParryGaugeGain, 5f),
                Node(2, WeaponKind.Sword, 1, TalentEffectKind.ParryWindowSeconds, 0.02f),
                Node(3, WeaponKind.Bow, 0, TalentEffectKind.AttackSpeedPercent, 5f),
            });

            _curve = ScriptableObject.CreateInstance<LevelCurveSO>();
            Set(_curve, "_talentPointPerLevel", 1);
            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_talent", _tree);
            Set(_database, "_levelCurve", _curve);
            GameDB.Initialize(_database);

            _player = new GameObject("TalentControllerTests", typeof(NetworkIdentity));
            _player.AddComponent<TestWeaponTarget>();
            _player.AddComponent<TestGrowthParticipant>();
            _loadout = _player.AddComponent<TestLoadoutRevisionTarget>();
            _controller = _player.AddComponent<TalentController>();
            Invoke(_controller, "Awake");

            NetworkIdentity identity = _player.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, true);
            _controller.OnStartServer();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_player);
            GameDB.ResetStatic();
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_curve);
            Object.DestroyImmediate(_tree);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        [Test]
        public void AllocateCommitsLoadoutBeforePublishingCompleteAllocation()
        {
            int callbackCount = 0;
            uint callbackLoadoutRevision = 0u;
            TalentAllocationSnapshot callbackSnapshot = default;
            bool everyCallbackSawCompleteRawState = true;
            SyncList<int> published = Get<SyncList<int>>(_controller, "_nodeIds");
            published.Callback += (_, __, ___, ____) =>
            {
                ++callbackCount;
                callbackLoadoutRevision = _loadout.LastRevision;
                bool captured = _controller.TryGetAllocation(
                    new ParticipantId(7UL), out callbackSnapshot);
                everyCallbackSawCompleteRawState &= captured &&
                    (callbackLoadoutRevision == 2u) &&
                    (callbackSnapshot.Revision == 2u) &&
                    callbackSnapshot.Contains(1);
            };

            Assert.AreEqual(TalentAllocationResult.Success,
                _controller.ServerTryAllocate(1, 1u));

            Assert.Greater(callbackCount, 0);
            Assert.IsTrue(everyCallbackSawCompleteRawState);
            Assert.AreEqual(2u, callbackLoadoutRevision);
            Assert.AreEqual(2u, callbackSnapshot.Revision);
            CollectionAssert.AreEqual(new[] { 1 }, callbackSnapshot.NodeIds);
            Assert.AreEqual(TalentAllocationResult.StaleRevision,
                _controller.ServerTryAllocate(2, 1u));
            Assert.AreEqual(TalentAllocationResult.Success,
                _controller.ServerTryAllocate(2, 2u));
            Assert.AreEqual(1, _controller.AvailablePoints);
        }

        [Test]
        public void StagedWeaponResetKeepsSameWeaponAndPublishesDifferentWeaponOnce()
        {
            Assert.AreEqual(TalentAllocationResult.Success,
                _controller.ServerTryAllocate(1, 1u));
            uint swordRevision = _controller.Revision;

            _controller.ServerApplyWeaponReset(WeaponKind.Sword);
            _controller.ServerPublishAllocation();
            Assert.AreEqual(swordRevision, _controller.Revision);
            Assert.IsTrue(_controller.TryGetAllocation(new ParticipantId(7UL),
                out TalentAllocationSnapshot sameWeapon));
            CollectionAssert.AreEqual(new[] { 1 }, sameWeapon.NodeIds);

            int callbackCount = 0;
            bool everyCallbackSawCompleteRawState = true;
            SyncList<int> published = Get<SyncList<int>>(_controller, "_nodeIds");
            published.Callback += (_, __, ___, ____) =>
            {
                ++callbackCount;
                bool captured = _controller.TryGetAllocation(
                    new ParticipantId(7UL), out TalentAllocationSnapshot callbackSnapshot);
                everyCallbackSawCompleteRawState &= captured &&
                    (callbackSnapshot.Weapon == WeaponKind.Bow) &&
                    (callbackSnapshot.Revision == swordRevision + 1u) &&
                    (callbackSnapshot.NodeIds.Length == 0);
            };

            Assert.IsTrue(_controller.ServerCanResetForWeapon(WeaponKind.Bow));
            _controller.ServerApplyWeaponReset(WeaponKind.Bow);
            Assert.AreEqual(1, published.Count,
                "raw 단계에서는 공개 SyncList를 바꾸지 않는다");
            Assert.IsTrue(_controller.TryGetAllocation(new ParticipantId(7UL),
                out TalentAllocationSnapshot reset));
            Assert.AreEqual(WeaponKind.Bow, reset.Weapon);
            Assert.IsEmpty(reset.NodeIds);

            _controller.ServerPublishAllocation();
            Assert.AreEqual(0, published.Count);
            Assert.Greater(callbackCount, 0);
            Assert.IsTrue(everyCallbackSawCompleteRawState);
        }

        [Test]
        public void CaptureAndRestorePreserveAllocationRevisionAndSpentPoints()
        {
            Assert.AreEqual(TalentAllocationResult.Success,
                _controller.ServerTryAllocate(1, 1u));
            var captured = new RunParticipantSnapshot
            {
                Weapon = WeaponKind.Sword,
                Level = 3,
            };
            _controller.Capture(captured);

            Assert.IsTrue(_controller.Validate(captured, out string failure), failure);
            Assert.AreEqual(TalentAllocationResult.Success,
                _controller.ServerTryReset(captured.TalentAllocation.Revision));
            Assert.IsTrue(_controller.Restore(captured));
            Assert.IsTrue(_controller.TryGetAllocation(new ParticipantId(7UL),
                out TalentAllocationSnapshot restored));
            Assert.AreEqual(captured.TalentAllocation, restored);
        }

        private static TalentTreeSO.NodeEntry Node(int id, WeaponKind weapon, int row,
            TalentEffectKind effect, float value)
        {
            return new TalentTreeSO.NodeEntry
            {
                NodeId      = id,
                WeaponKind  = weapon,
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

        private static T Get<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, PRIVATE_INSTANCE).GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            return target.GetType().GetMethod(name, PRIVATE_INSTANCE).Invoke(target, arguments);
        }

        private sealed class TestGrowthParticipant : MonoBehaviour, IGrowthParticipant
        {
            public ParticipantId Participant => new ParticipantId(7UL);
            public GrowthState Growth { get; private set; } =
                new GrowthState(4, 0, 127f, 1u, 3);
            public event Action<GrowthState> GrowthChanged;

            public void ServerApplyGrowth(GrowthState state)
            {
                Growth = state;
                GrowthChanged?.Invoke(state);
            }
        }

        private sealed class TestLoadoutRevisionTarget : MonoBehaviour,
            ICombatLoadoutRevisionTarget
        {
            public uint LastRevision { get; private set; }

            public bool ServerRegisterTalentRevision(uint revision)
            {
                if ((revision == 0u) || ((LastRevision != 0u) &&
                    (LastRevision != revision)))
                {
                    return false;
                }

                LastRevision = revision;
                return true;
            }

            public bool ServerTryCommitTalentRevision(uint previousRevision,
                uint nextRevision)
            {
                if ((LastRevision != previousRevision) ||
                    (nextRevision != previousRevision + 1u))
                {
                    return false;
                }

                LastRevision = nextRevision;
                return true;
            }
        }

        private sealed class TestWeaponTarget : MonoBehaviour, IEquipmentWeaponTarget
        {
            public bool HasWeapon => true;
            public WeaponKind Weapon => WeaponKind.Sword;
            public event Action<WeaponKind> WeaponChanged;

            public bool ServerSetEquippedWeapon(in WeaponCombatSnapshot snapshot)
            {
                WeaponChanged?.Invoke(snapshot.WeaponKind);
                return true;
            }

            public bool ServerTryAcquireEquippedWeapon(in WeaponCombatSnapshot snapshot,
                out EquipmentWeaponWrite write)
            {
                write = default;
                return false;
            }

            public void ServerApplyEquippedWeapon(in EquipmentWeaponWrite write) { }
            public void ServerPublishEquippedWeapon(in EquipmentWeaponWrite write) { }
            public void ServerReleaseEquippedWeapon() { }
        }
    }
}
