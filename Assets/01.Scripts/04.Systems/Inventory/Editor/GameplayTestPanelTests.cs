using System.Collections.Generic;
using System.Reflection;
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Geumeum.Systems.Tests
{
    public sealed class GameplayTestPanelTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private List<PlayerStatus> _players;
        private PlayerStatus[] _previousPlayers;
        private RunManager _run;
        private RunManager _previousRun;
        private IRunTimeSource _previousTime;
        private GameplayTestPanel _panel;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active);
            _previousRun = RunManager.ServerInstance;
            _previousTime = RunTimeLocator.Current;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            DevAccess.Unlock();
            GameDB.Initialize(AssetDatabase.LoadAssetAtPath<GameDatabaseSO>("Assets/09.Data/Resources/GameDB.asset"));
            _players = (List<PlayerStatus>)typeof(PlayerStatus).GetField("G_ServerPlayers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousPlayers = _players.ToArray();
            _players.Clear();
            _run = Create("GameplayPanelTestRun", typeof(NetworkIdentity), typeof(RunManager)).GetComponent<RunManager>();
            InitializeIdentity(_run.GetComponent<NetworkIdentity>(), 899u);
            Set(_run, "_runIdValue", 7u);
            Set(_run, "_phase", RunPhase.Boss);
            typeof(RunManager).GetProperty(nameof(RunManager.ServerInstance)).SetValue(null, _run);
            _panel = Create("GameplayPanelTest", typeof(GameplayTestPanel)).GetComponent<GameplayTestPanel>();
            var drops = Create("GameplayPanelTestDrops", typeof(NetworkIdentity), typeof(DropDirector)).GetComponent<DropDirector>();
            InitializeIdentity(drops.GetComponent<NetworkIdentity>(), 900u);
            Set(_panel, "_drops", drops);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject obj in _objects)
            {
                if (obj != null && obj.TryGetComponent(out NetworkIdentity identity)) { NetworkServer.spawned.Remove(identity.netId); }
                if (obj != null) { Object.DestroyImmediate(obj); }
            }
            _objects.Clear();
            _players.Clear();
            _players.AddRange(_previousPlayers);
            typeof(RunManager).GetProperty(nameof(RunManager.ServerInstance)).SetValue(null, _previousRun);
            RunTimeLocator.Register(_previousTime);
            GameDB.ResetStatic();
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        [Test]
        public void SelectedParticipantSurvivesSceneRespawnAndReceivesEquipment()
        {
            var first = AddPlayer(11u, new ParticipantId(101UL));
            Set(first.GetComponent<PlayerNameTag>(), "_displayName", "다른 사용자");
            var old = AddPlayer(12u, new ParticipantId(102UL));
            Invoke(_panel, "Build", new DevPanelBuilder());
            Command("target", "1");
            _players.Remove(old.GetComponent<PlayerStatus>());
            NetworkServer.spawned.Remove(12u);
            old.SetActive(false);
            var replacement = AddPlayer(44u, new ParticipantId(102UL));
            // 스냅샷 갱신 전에 누른 명령도 같은 참가자의 새 서버 오브젝트에 적용한다.
            Command("weapon", "2");
            Command("grade", "3");
            Command("grant");
            InventoryController inventory = replacement.GetComponent<InventoryController>();
            InventorySnapshot snapshot = (InventorySnapshot)Get(inventory, "_inventory");
            Assert.AreEqual(1u, inventory.Revision);
            Assert.AreEqual(WeaponKind.Bell, snapshot.GetSlot(0).Equipment.WeaponKind);
            Assert.AreEqual(ItemGrade.Divine, snapshot.GetSlot(0).Equipment.Grade);
            Assert.That((string)Get(_panel, "_message"), Does.Contain("김민성: 성공"));
        }

        [Test]
        public void MissingSelectedParticipantDoesNotGiveToAnotherPlayer()
        {
            var first = AddPlayer(11u, new ParticipantId(101UL));
            var selected = AddPlayer(12u, new ParticipantId(102UL));
            Invoke(_panel, "Build", new DevPanelBuilder());
            Command("target", "1");
            _players.Remove(selected.GetComponent<PlayerStatus>());
            NetworkServer.spawned.Remove(12u);
            Command("supply.1");
            Assert.AreEqual(0u, first.GetComponent<InventoryController>().Revision);
            Assert.That((string)Get(_panel, "_message"), Does.Contain("대상이 사라졌다"));
        }

        [Test]
        public void ConsumableLimitFailureDoesNotReportSuccessOrChangeInventory()
        {
            var player = AddPlayer(11u, new ParticipantId(101UL));
            Invoke(_panel, "Build", new DevPanelBuilder());
            Command("amount", "999");
            Command("supply.1");
            Assert.AreEqual(0u, player.GetComponent<InventoryController>().Revision);
            Assert.That((string)Get(_panel, "_message"), Does.Contain("실패"));
            Assert.That((string)Get(_panel, "_message"), Does.Not.Contain("성공"));
        }

        [Test]
        public void ClientCommandCannotMutateInventory()
        {
            var player = AddPlayer(11u, new ParticipantId(101UL));
            Invoke(_panel, "Build", new DevPanelBuilder());
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
            Command("grant");
            Assert.AreEqual(0u, player.GetComponent<InventoryController>().Revision);
            Assert.That((string)Get(_panel, "_message"), Does.Contain("호스트"));
        }

        [Test]
        public void TargetChoiceShowsUserNameAndWeaponInsteadOfObjectOrNetworkId()
        {
            var player = AddPlayer(11u, new ParticipantId(101UL));
            var weapon = player.AddComponent<PlayerWeapon>();
            InitializeIdentity(player.GetComponent<NetworkIdentity>(), 11u);
            Set(weapon, "_hasWeapon", true);
            Set(weapon, "_weapon", WeaponKind.Bow);
            var builder = new DevPanelBuilder();
            Invoke(_panel, "Build", builder);
            DevControlView choice = System.Array.Find(builder.ToArray(), control => control.id == "target");
            Assert.AreEqual("김민성 · 활", choice.options[0]);
            Assert.AreEqual(choice.options[0], choice.text);
            Assert.That(choice.options[0], Does.Not.Contain("GameplayPanelTestPlayer"));
            Assert.That(choice.options[0], Does.Not.Contain("#11"));
        }

        [Test]
        public void DuplicateNamesHaveDifferentPartyLabelsAndSelectCorrectParticipant()
        {
            var first = AddPlayer(11u, new ParticipantId(101UL));
            var second = AddPlayer(12u, new ParticipantId(102UL));
            var builder = new DevPanelBuilder();
            Invoke(_panel, "Build", builder);
            DevControlView choice = System.Array.Find(builder.ToArray(), control => control.id == "target");
            Assert.AreEqual("김민성 (파티 1)", choice.options[0]);
            Assert.AreEqual("김민성 (파티 2)", choice.options[1]);
            Command("target", "1");
            Command("supply.1");
            Assert.AreEqual(0u, first.GetComponent<InventoryController>().Revision);
            Assert.AreEqual(1u, second.GetComponent<InventoryController>().Revision);
        }

        private GameObject AddPlayer(uint netId, ParticipantId participant)
        {
            var player = Create("GameplayPanelTestPlayer", typeof(NetworkIdentity), typeof(PlayerStatus), typeof(PlayerNameTag), typeof(InventoryController));
            InitializeIdentity(player.GetComponent<NetworkIdentity>(), netId);
            Set(player.GetComponent<PlayerStatus>(), "_participant", participant);
            Set(player.GetComponent<PlayerNameTag>(), "_displayName", "김민성");
            Invoke(player.GetComponent<InventoryController>(), "EnsureSlotCount");
            _players.Add(player.GetComponent<PlayerStatus>());
            NetworkServer.spawned.Add(netId, player.GetComponent<NetworkIdentity>());
            return player;
        }

        private GameObject Create(string name, params System.Type[] components)
        {
            var obj = new GameObject(name, components);
            _objects.Add(obj);
            return obj;
        }

        private static void InitializeIdentity(NetworkIdentity identity, uint netId)
        {
            Invoke(identity, "InitializeNetworkBehaviours");
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId)).SetValue(identity, netId);
        }

        private void Command(string control, string value = "") => Invoke(_panel, "Handle", new DevCommand("gameplay", control, value));
        private static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
        private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
        private static object Invoke(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    }
}
