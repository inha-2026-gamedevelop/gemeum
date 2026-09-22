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
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.Network.Tests
{
    public sealed class ServerBootGateTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> _createdObjects = new List<Object>();
        private NetworkManager _manager;
        private GameObject    _requiredDropPrefab;

        [SetUp]
        public void SetUp()
        {
            GameObject managerObject = CreateObject("BootGateTestNetworkManager");
            managerObject.SetActive(false);
            _manager = managerObject.AddComponent<NetworkManager>();
            _manager.spawnPrefabs = new List<GameObject>();

            GameObject providerObject = CreateObject("BootGateTestDropDirector");
            providerObject.SetActive(false);
            providerObject.AddComponent<NetworkIdentity>();
            DropDirector provider = providerObject.AddComponent<DropDirector>();

            _requiredDropPrefab = CreateNetworkPrefab("RequiredDroppedItem");
            _requiredDropPrefab.AddComponent<DroppedItem>();
            SetField(provider, "_droppedItemPrefab", _requiredDropPrefab);

            // 기존 부트 목록이 모두 유효해도 공급자가 요구한 프리팹은 별도로 등록되어야 한다
            for (int i = 0; i < 6; ++i)
            {
                _manager.spawnPrefabs.Add(CreateNetworkPrefab($"RegisteredSpawnPrefab{i}"));
            }
        }

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
            _manager = null;
            _requiredDropPrefab = null;
        }

        [Test]
        public void MainBootRejectsMissingRequiredDropPrefabRegistration()
        {
            List<string> failures = new List<string>();

            ServerBootGate.TryValidate(_manager, GeumeumBootValidator.SceneProfile.Main, failures);

            AssertHasRequiredRegistrationFailure(failures);
        }

        [Test]
        public void MainBootAcceptsRegisteredRequiredDropPrefab()
        {
            _manager.spawnPrefabs.Add(_requiredDropPrefab);
            List<string> failures = new List<string>();

            ServerBootGate.TryValidate(_manager, GeumeumBootValidator.SceneProfile.Main, failures);

            Assert.IsFalse(HasRequiredRegistrationFailure(failures),
                string.Join("\n", failures.ToArray()));
        }

        [Test]
        public void DevBootKeepsExistingOptionalSpawnProviderPolicy()
        {
            List<string> failures = new List<string>();

            ServerBootGate.TryValidate(_manager, GeumeumBootValidator.SceneProfile.DevScene, failures);

            Assert.IsFalse(HasRequiredRegistrationFailure(failures),
                string.Join("\n", failures.ToArray()));
        }

        private GameObject CreateObject(string name)
        {
            GameObject value = new GameObject(name);
            _createdObjects.Add(value);
            return value;
        }

        private GameObject CreateNetworkPrefab(string name)
        {
            GameObject value = CreateObject(name);
            value.SetActive(false);
            value.AddComponent<NetworkIdentity>();
            return value;
        }

        private void AssertHasRequiredRegistrationFailure(List<string> failures)
        {
            Assert.IsTrue(HasRequiredRegistrationFailure(failures),
                string.Join("\n", failures.ToArray()));
        }

        private bool HasRequiredRegistrationFailure(List<string> failures)
        {
            string expected = $"요구 스폰 프리팹 {_requiredDropPrefab.name}이 NetworkManager.spawnPrefabs에 등록되지 않았다";
            return failures.Contains(expected);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PRIVATE_INSTANCE);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
