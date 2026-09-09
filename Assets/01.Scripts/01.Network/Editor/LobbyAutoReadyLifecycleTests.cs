// System
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;

namespace Geumeum.Network.EditorTests
{
    public sealed class LobbyAutoReadyLifecycleTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PUBLIC_STATIC =
            BindingFlags.Static | BindingFlags.Public;

        private bool _wasServerActive;

        private GameObject _lobbyObject;
        private GameObject _rosterObject;
        private GameObject _runObject;
        private GameObject _directorObject;

        private LobbyController      _lobby;
        private LobbyRoster          _roster;
        private RunManager           _run;
        private RunSceneDirector     _director;
        private LobbyController      _previousLobby;
        private LobbyRoster          _previousRoster;
        private RunManager           _previousRun;
        private RunSceneDirector     _previousDirector;
        private LobbyRoleCoordinator _previousRoleCoordinator;

        [SetUp]
        public void SetUp()
        {
            _wasServerActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);

            _previousLobby = LobbyController.ServerInstance;
            _previousRoster = LobbyRoster.ServerInstance;
            _previousRun = RunManager.ServerInstance;
            _previousDirector = RunSceneDirector.ServerInstance;
            _previousRoleCoordinator = LobbyRoleCoordinator.ServerInstance;

            _lobbyObject = new GameObject(
                "LobbyAutoReadyTestLobby", typeof(NetworkIdentity), typeof(LobbyController));
            _rosterObject = new GameObject(
                "LobbyAutoReadyTestRoster", typeof(NetworkIdentity), typeof(LobbyRoster));
            _runObject = new GameObject(
                "LobbyAutoReadyTestRun", typeof(NetworkIdentity), typeof(RunManager));
            _directorObject = new GameObject(
                "LobbyAutoReadyTestDirector", typeof(NetworkIdentity), typeof(RunSceneDirector));

            _lobby = _lobbyObject.GetComponent<LobbyController>();
            _roster = _rosterObject.GetComponent<LobbyRoster>();
            _run = _runObject.GetComponent<RunManager>();
            _director = _directorObject.GetComponent<RunSceneDirector>();

            typeof(NetworkIdentity)
                .GetMethod("InitializeNetworkBehaviours", PRIVATE_INSTANCE)
                .Invoke(_rosterObject.GetComponent<NetworkIdentity>(), null);

            SetStaticProperty(typeof(LobbyController), nameof(LobbyController.ServerInstance), _lobby);
            SetStaticProperty(typeof(LobbyRoster), nameof(LobbyRoster.ServerInstance), _roster);
            SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), _run);
            SetStaticProperty(typeof(RunSceneDirector), nameof(RunSceneDirector.ServerInstance), _director);
            SetStaticProperty(typeof(LobbyRoleCoordinator),
                nameof(LobbyRoleCoordinator.ServerInstance), null);

            Set(_lobby, "_devAutoReady", true);
            Set(_run, "_phase", RunPhase.Lobby);
            Set(_director, "_currentScene", RunSceneId.Result);
            Set(_director, "_state", RunTransitionState.Loading);

            _roster.ServerAddSlot(new NetworkConnectionToClient(41));
        }

        [TearDown]
        public void TearDown()
        {
            SetStaticProperty(typeof(LobbyController),
                nameof(LobbyController.ServerInstance), _previousLobby);
            SetStaticProperty(typeof(LobbyRoster),
                nameof(LobbyRoster.ServerInstance), _previousRoster);
            SetStaticProperty(typeof(RunManager),
                nameof(RunManager.ServerInstance), _previousRun);
            SetStaticProperty(typeof(RunSceneDirector),
                nameof(RunSceneDirector.ServerInstance), _previousDirector);
            SetStaticProperty(typeof(LobbyRoleCoordinator),
                nameof(LobbyRoleCoordinator.ServerInstance), _previousRoleCoordinator);

            Object.DestroyImmediate(_directorObject);
            Object.DestroyImmediate(_runObject);
            Object.DestroyImmediate(_rosterObject);
            Object.DestroyImmediate(_lobbyObject);

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, _wasServerActive);
        }

        [Test]
        public void AutoReadyWaitsForLobbyIdleAndAppliesOnce()
        {
            _lobby.ServerResetForNewRun();

            Assert.IsFalse(_roster.GetSlot(0).IsReady,
                "Result 전환 중에는 자동 준비와 런 시작 판정을 소비하면 안 된다");
            Assert.IsTrue((bool) Get(_lobby, "_autoReadyForLobbyPending"));

            Set(_director, "_currentScene", RunSceneId.Lobby);
            Set(_director, "_state", RunTransitionState.Idle);
            _run.ServerHandleSceneTransitionReady();

            Assert.IsTrue(_roster.GetSlot(0).IsReady);
            Assert.IsFalse((bool) Get(_lobby, "_autoReadyForLobbyPending"));

            LobbySlot slot = _roster.GetSlot(0);
            slot.IsReady = false;
            _roster.SetSlot(0, slot);

            _run.ServerHandleSceneTransitionReady();

            Assert.IsFalse(_roster.GetSlot(0).IsReady,
                "같은 Idle 완료 콜백이 겹쳐도 자동 준비를 두 번 적용하면 안 된다");
        }

        private static object Get(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, PRIVATE_INSTANCE).GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private static void SetStaticProperty(System.Type type,
            string propertyName, object value)
        {
            type.GetProperty(propertyName, PUBLIC_STATIC).SetValue(null, value);
        }
    }
}
