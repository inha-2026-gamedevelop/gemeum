// System
using System;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using Object = UnityEngine.Object;

// 서드파티
using Mirror;
using NUnit.Framework;
using UnityEngine.TestTools;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    /// <summary> 인벤토리 원본과 장착 snapshot이 서버에서 함께 움직이는지 확인한다 </summary>
    public class InventoryControllerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject          _player;
        private NetworkIdentity     _identity;
        private PlayerWeapon        _weapon;
        private TalentController    _talent;
        private SupportBuffController _buff;
        private InventoryController _inventory;
        private GameDatabaseSO      _database;
        private CombatConfigSO      _combat;
        private ArrowConfigSO       _arrow;
        private TalentTreeSO        _talentTree;
        private LevelCurveSO        _levelCurve;

        /****************************************
        *                Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서는 이 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _combat     = ScriptableObject.CreateInstance<CombatConfigSO>();
            _arrow      = ScriptableObject.CreateInstance<ArrowConfigSO>();
            _talentTree = ScriptableObject.CreateInstance<TalentTreeSO>();
            _levelCurve = ScriptableObject.CreateInstance<LevelCurveSO>();
            Set(_levelCurve, "_levels", new[]
            {
                new LevelCurveSO.LevelEntry
                {
                    Level              = Constants.Combat.START_LEVEL,
                    MaxHealth          = 100f,
                    RequiredExperience = 0,
                },
            });

            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_combat", _combat);
            Set(_database, "_arrow", _arrow);
            Set(_database, "_talent", _talentTree);
            Set(_database, "_levelCurve", _levelCurve);
            GameDB.Initialize(_database);

            _player    = new GameObject("InventoryControllerTestPlayer", typeof(NetworkIdentity));
            _weapon    = _player.AddComponent<PlayerWeapon>();
            _player.AddComponent<TestGrowthParticipantComponent>();
            _talent    = _player.AddComponent<TalentController>();
            _buff      = _player.AddComponent<SupportBuffController>();
            _inventory = _player.AddComponent<InventoryController>();
            _identity  = _player.GetComponent<NetworkIdentity>();

            // EditMode에서는 Unity의 Awake/서버 시작 수명을 직접 진행한다.
            Invoke(_weapon, "Awake");
            Invoke(_talent, "Awake");
            Invoke(_buff, "Awake");
            Invoke(_inventory, "Awake");
            Invoke(_identity, "InitializeNetworkBehaviours");
            SetProperty(_identity, nameof(NetworkIdentity.isServer), true);
            _buff.OnStartServer();
            _inventory.OnStartServer();
            _talent.OnStartServer();
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

            GameDB.ResetStatic();
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_combat);
            Object.DestroyImmediate(_arrow);
            Object.DestroyImmediate(_talentTree);
            Object.DestroyImmediate(_levelCurve);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(WeaponKind.Sword, 10f, 10f, 8f)]
        [TestCase(WeaponKind.Bow,   10f,  8f, 8f)]
        [TestCase(WeaponKind.Bell,  6f,   6f, 8f)]
        public void TryBuildLoadoutUsesBasicWeaponPower(WeaponKind weapon, float expectedMelee,
            float expectedActive, float expectedRanged)
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(weapon));

            bool built = _inventory.TryBuildLoadout(new ParticipantId(1UL), out CombatStatsSnapshot snapshot);

            Assert.IsTrue(built);
            Assert.AreEqual(1u, snapshot.Revision);
            Assert.AreEqual(100f, snapshot.MaxHealth);
            Assert.AreEqual(expectedMelee, snapshot.MeleeAttackPower);
            Assert.AreEqual(expectedRanged, snapshot.RangedAttackPower);

            // expectedActive는 Bell도 현재 CombatStatsSnapshot의 근접 칸을 소비한다는 계약을 드러낸다
            Assert.AreEqual((weapon == WeaponKind.Bow) ? expectedRanged : expectedActive,
                (weapon == WeaponKind.Bow) ? snapshot.RangedAttackPower : snapshot.MeleeAttackPower);
        }

        [Test]
        public void TryBuildLoadoutUsesEquippedAttackPower()
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(WeaponKind.Bow));

            WeaponCombatSnapshot equipment = MakeWeapon(27u, WeaponKind.Bow, 123f);
            Assert.IsTrue(_weapon.ServerSetEquippedWeapon(equipment));
            Assert.IsTrue(_weapon.HasWeapon);
            Assert.AreEqual(WeaponKind.Bow, _weapon.Weapon);
            Set(_inventory, "_equippedWeapon", equipment);
            Set(_inventory, "_hasEquippedWeapon", true);
            Set(_inventory, "_serverEquippedWeapon", equipment);
            Set(_inventory, "_serverHasEquippedWeapon", true);
            Set(_inventory, "_loadoutRevision", 4u);

            bool built = _inventory.TryBuildLoadout(new ParticipantId(1UL), out CombatStatsSnapshot snapshot);

            Assert.IsTrue(built);
            Assert.AreEqual(4u, snapshot.Revision);
            Assert.AreEqual(123f, snapshot.RangedAttackPower);
            Assert.AreEqual(_combat.MeleeBasePower, snapshot.MeleeAttackPower);
        }

        [Test]
        public void TryBuildLoadoutUsesCurrentGrowthMaxHealth()
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(WeaponKind.Sword));
            GrowthProviderLocator.Register(new TestGrowthProvider(
                new GrowthState(3, 223, 118f, 2u, 2)));

            bool built = _inventory.TryBuildLoadout(new ParticipantId(1UL),
                out CombatStatsSnapshot snapshot);

            Assert.IsTrue(built);
            Assert.AreEqual(118f, snapshot.MaxHealth);
        }

        [Test]
        public void LoadoutRevisionOnlyCacheDetectsGrowthChange()
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(WeaponKind.Sword));

            ParticipantId participant = new ParticipantId(1UL);
            TestGrowthParticipant growth = new TestGrowthParticipant(participant,
                new GrowthState(1, 0, 100f, 1u, 0));
            GrowthParticipantLocator.Register(growth);

            Assert.IsTrue(_inventory.TryBuildLoadout(participant, out _));
            ICombatLoadoutProvider provider = _inventory;
            uint cachedRevision = provider.Revision;

            growth.ServerApplyGrowth(new GrowthState(2, 123, 109f, 2u, 1));

            Assert.AreEqual(cachedRevision + 1u, provider.Revision);
        }

        [Test]
        public void ReapplyingSameGrowthDoesNotAdvanceLoadoutRevision()
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(WeaponKind.Sword));

            ParticipantId participant = new ParticipantId(1UL);
            GrowthState startingGrowth = new GrowthState(1, 0, 100f, 1u, 0);
            GrowthState leveledGrowth = new GrowthState(2, 123, 109f, 2u, 1);
            TestGrowthParticipant growth = new TestGrowthParticipant(participant, startingGrowth);
            GrowthParticipantLocator.Register(growth);

            Assert.IsTrue(_inventory.TryBuildLoadout(participant, out _));
            ICombatLoadoutProvider provider = _inventory;
            uint initialRevision = provider.Revision;

            growth.ServerApplyGrowth(leveledGrowth);
            uint leveledRevision = provider.Revision;

            growth.ServerApplyGrowth(leveledGrowth);

            Assert.AreEqual(initialRevision + 1u, leveledRevision);
            Assert.AreEqual(leveledRevision, provider.Revision);
        }

        [Test]
        public void CaptureValidateAndRestoreKeepEquippedSnapshotTogether()
        {
            Assert.IsTrue(_weapon.ServerSetInitialWeapon(WeaponKind.Sword));

            RunParticipantSnapshot captured = new RunParticipantSnapshot
            {
                Weapon = WeaponKind.Sword,
            };
            _inventory.Capture(captured);

            Assert.AreEqual(0u, captured.EquippedWeapon.InstanceId);
            Assert.IsFalse(_inventory.HasEquippedWeapon, "캡처는 지연 초기화된 서버 원본을 바꾸지 않는다");
            Assert.AreEqual(0u, _inventory.LoadoutRevision);
            Assert.IsNotNull(captured.Inventory);
            Assert.IsTrue(_inventory.Validate(captured, out string captureFailure), captureFailure);

            WeaponCombatSnapshot equipment = MakeWeapon(31u, WeaponKind.Sword, 88f);
            RunParticipantSnapshot restored = new RunParticipantSnapshot
            {
                Weapon        = WeaponKind.Sword,
                EquippedWeapon = equipment,
                Inventory     = new InventorySnapshot(),
            };

            Assert.IsTrue(_inventory.Validate(restored, out string restoreFailure), restoreFailure);
            Assert.IsTrue(_inventory.Restore(restored));
            Assert.AreEqual(equipment, _inventory.EquippedWeapon);
            Assert.AreEqual(1u, _inventory.LoadoutRevision);
        }

        [Test]
        public void ValidateRejectsRoleAndEquippedWeaponKindMismatch()
        {
            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                Weapon         = WeaponKind.Sword,
                EquippedWeapon = MakeWeapon(9u, WeaponKind.Bow, 80f),
                Inventory      = new InventorySnapshot(),
            };

            Assert.IsFalse(_inventory.Validate(snapshot, out _));
        }

        [Test]
        public void DuplicateEquipRequestReturnsCachedResultWithoutReapplying()
        {
            WeaponCombatSnapshot previous = MakeWeapon(3u);
            Invoke(_inventory, "RememberEquipResult", 12u, 2, 41u, 7u,
                InventoryResult.Success, previous);

            object[] exactArguments = { 12u, 2, 41u, 7u, InventoryResult.Success, default(WeaponCombatSnapshot) };
            bool exact = (bool) Invoke(_inventory, "TryGetCachedEquipResult", exactArguments);
            Assert.IsTrue(exact);
            Assert.AreEqual(InventoryResult.Success, exactArguments[4]);
            Assert.AreEqual(previous, exactArguments[5]);

            object[] changedArguments = { 12u, 3, 41u, 7u, InventoryResult.Success, default(WeaponCombatSnapshot) };
            bool changed = (bool) Invoke(_inventory, "TryGetCachedEquipResult", changedArguments);
            Assert.IsTrue(changed);
            Assert.AreEqual(InventoryResult.DuplicateRequest, changedArguments[4]);

            object[] oldArguments = { 11u, 2, 41u, 7u, InventoryResult.Success, default(WeaponCombatSnapshot) };
            bool old = (bool) Invoke(_inventory, "TryGetCachedEquipResult", oldArguments);
            Assert.IsTrue(old);
            Assert.AreEqual(InventoryResult.DuplicateRequest, oldArguments[4]);
        }

        /****************************************
        *                Helpers
        ****************************************/

        [Test]
        public void TransactionPublishesOnlyAfterRawRevisionAndSlotAreFinal()
        {
            object[] prepareArguments =
            {
                ShopItemKind.HealthPotion, 1, 3, 3, 0u,
                default(InventoryCommitPlan),
            };
            InventoryResult prepared = (InventoryResult) Invoke(_inventory,
                "ServerPrepareStoreConsumable", prepareArguments);
            Assert.AreEqual(InventoryResult.Success, prepared);
            InventoryCommitPlan plan = (InventoryCommitPlan) prepareArguments[5];

            Assert.IsTrue((bool) Invoke(_inventory, "ServerTryAcquireTransaction", plan));
            try
            {
                Invoke(_inventory, "ServerApplyTransaction", plan);
                Assert.AreEqual(1u, _inventory.Revision,
                    "서버 getter는 아직 공개되지 않은 원본 revision을 읽는다");
                Assert.AreEqual(0u, Get<uint>(_inventory, "_revision"));
                Assert.AreEqual(InventoryResult.InvalidState,
                    _inventory.ServerTryStoreConsumable(ShopItemKind.SpiritPotion,
                        1, 3, 3, _inventory.Revision));

                uint callbackRevision = 0u;
                InventorySlotSnapshot callbackSlot = default;
                SyncList<InventorySlotSnapshot> slots =
                    Get<SyncList<InventorySlotSnapshot>>(_inventory, "_slots");
                slots.Callback += (operation, index, oldValue, newValue) =>
                {
                    callbackRevision = _inventory.Revision;
                    var captured = new RunParticipantSnapshot();
                    _inventory.Capture(captured);
                    callbackSlot = captured.Inventory.GetSlot(index);
                };

                Invoke(_inventory, "ServerPublishTransaction", plan);

                Assert.AreEqual(1u, callbackRevision);
                Assert.AreEqual(ShopItemKind.HealthPotion, callbackSlot.ItemId);
                Assert.AreEqual(1, callbackSlot.Count);
                Assert.AreEqual(1u, Get<uint>(_inventory, "_revision"));
            }
            finally
            {
                Invoke(_inventory, "ServerReleaseTransaction");
            }
        }

        [Test]
        public void ThrowingFirstSlotObserverStillPublishesSecondSlotRevisionAndNextChange()
        {
            Assert.AreEqual(InventoryResult.Success,
                _inventory.ServerTryStoreConsumable(ShopItemKind.RerollTicket,
                    1, -1, 1, 0u));
            WeaponCombatSnapshot original = MakeWeapon(61u);
            Assert.AreEqual(InventoryResult.Success,
                _inventory.ServerTryStoreEquipment(original, 1u));
            WeaponCombatSnapshot replacement = original;
            replacement.Grade = ItemGrade.Talisman;
            replacement.AttackPower = 125f;

            object[] prepareArguments =
            {
                0, ShopItemKind.RerollTicket, 1, 61u, replacement, 2u,
                default(InventoryCommitPlan),
            };
            Assert.AreEqual(InventoryResult.Success,
                (InventoryResult) Invoke(_inventory, "ServerPrepareTicketUse",
                    prepareArguments));
            InventoryCommitPlan plan = (InventoryCommitPlan) prepareArguments[6];

            SyncList<InventorySlotSnapshot> slots =
                Get<SyncList<InventorySlotSnapshot>>(_inventory, "_slots");
            bool threw = false;
            slots.Callback += (operation, index, oldValue, newValue) =>
            {
                if (!threw)
                {
                    threw = true;
                    throw new InvalidOperationException("first slot observer test");
                }
            };
            LogAssert.Expect(LogType.Error,
                new Regex("\\[InventoryController\\] 0번 슬롯 observer 실패"));

            Assert.IsTrue((bool) Invoke(_inventory, "ServerTryAcquireTransaction", plan));
            try
            {
                Invoke(_inventory, "ServerApplyTransaction", plan);
                Invoke(_inventory, "ServerPublishTransaction", plan);
            }
            finally
            {
                Invoke(_inventory, "ServerReleaseTransaction");
            }

            Assert.IsTrue(slots[0].IsEmpty);
            Assert.AreEqual(61u, slots[1].Equipment.InstanceId);
            Assert.AreEqual(ItemGrade.Talisman, slots[1].Equipment.Grade);
            Assert.AreEqual(3u, Get<uint>(_inventory, "_revision"));
            Assert.AreEqual(InventoryResult.Success,
                _inventory.ServerTryStoreConsumable(ShopItemKind.SpiritPotion,
                    1, 3, 3, 3u));
            Assert.AreEqual(4u, _inventory.Revision);
        }

        private static WeaponCombatSnapshot MakeWeapon(uint instanceId, WeaponKind kind = WeaponKind.Sword,
            float attackPower = 100f)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId   = instanceId,
                WeaponKind   = kind,
                Grade        = ItemGrade.Common,
                AttackPower  = attackPower,
                Option0Kind  = EquipmentOptionKind.None,
                Option0Value = 0f,
                Option1Kind  = EquipmentOptionKind.None,
                Option1Value = 0f,
            };
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, PRIVATE_INSTANCE).Invoke(target, arguments);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private static T Get<T>(object target, string fieldName)
        {
            return (T) target.GetType().GetField(fieldName, PRIVATE_INSTANCE).GetValue(target);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            target.GetType().GetProperty(propertyName).SetValue(target, value);
        }

        private sealed class TestGrowthProvider : IGrowthProvider
        {
            private readonly GrowthState _state;

            public TestGrowthProvider(GrowthState state)
            {
                _state = state;
            }

            public bool TryGetState(ParticipantId participant, out GrowthState state)
            {
                state = _state;
                return participant.IsValid;
            }
        }

        private sealed class TestGrowthParticipant : IGrowthParticipant
        {
            public ParticipantId Participant { get; }
            public GrowthState Growth { get; private set; }

            public event System.Action<GrowthState> GrowthChanged;

            public TestGrowthParticipant(ParticipantId participant, GrowthState growth)
            {
                Participant = participant;
                Growth = growth;
            }

            public void ServerApplyGrowth(GrowthState state)
            {
                if (!state.IsValid)
                {
                    return;
                }

                bool changed = (Growth != state);
                Growth = state;
                if (changed)
                {
                    GrowthChanged?.Invoke(state);
                }
            }
        }

        private sealed class TestGrowthParticipantComponent : MonoBehaviour,
            IGrowthParticipant
        {
            public ParticipantId Participant => new ParticipantId(1UL);

            public GrowthState Growth { get; private set; } =
                new GrowthState(1, 0, 100f, 1u, 0);

            public event Action<GrowthState> GrowthChanged;

            public void ServerApplyGrowth(GrowthState state)
            {
                if (!state.IsValid)
                {
                    return;
                }

                Growth = state;
                GrowthChanged?.Invoke(state);
            }
        }

    }
}
