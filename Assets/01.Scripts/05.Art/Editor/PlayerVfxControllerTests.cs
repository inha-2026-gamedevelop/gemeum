// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    public sealed class PlayerVfxControllerTests
    {
        private GameObject _poolHost;
        private GameObject _controllerHost;
        private GameObject _prefab;
        private LocalObjectPoolService _pool;
        private PlayerVfxController _controller;
        private FieldInfo _connectStateField;
        private object _previousConnectState;

        [SetUp]
        public void SetUp()
        {
            _connectStateField = typeof(NetworkClient).GetField("connectState",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(_connectStateField);
            _previousConnectState = _connectStateField.GetValue(null);
            SetClientState("None");

            LocalObjectPoolService.ResetStatic();
            _poolHost = new GameObject("PlayerVfxPoolTest");
            _pool = _poolHost.AddComponent<LocalObjectPoolService>();
            typeof(LocalObjectPoolService).GetMethod("OnSingletonAwake",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_pool, null);

            _prefab = new GameObject("TestVfx", typeof(LocalPoolRoot),
                typeof(PooledVisualLifetime));
            _controllerHost = new GameObject("PlayerVfxControllerTest");
            _controller = _controllerHost.AddComponent<PlayerVfxController>();
        }

        [TearDown]
        public void TearDown()
        {
            _connectStateField.SetValue(null, _previousConnectState);
            UnityEngine.Object.DestroyImmediate(_controllerHost);
            // EditMode에서 pool.Clear의 Destroy 호출을 피하고 계층을 즉시 제거한다.
            LocalObjectPoolService.ResetStatic();
            UnityEngine.Object.DestroyImmediate(_poolHost);
            UnityEngine.Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void EnsurePrewarmedAddsOnlyMissingInstancesEvenWithOutstandingRentals()
        {
            Assert.IsTrue(_pool.EnsurePrewarmed(_prefab, 2, 4));
            GameObject first = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            GameObject second = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            Assert.IsTrue(_pool.EnsurePrewarmed(_prefab, 3, 4));
            Assert.IsTrue(_pool.EnsurePrewarmed(_prefab, 3, 4));
            GameObject third = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Assert.AreNotSame(first, third);
            Assert.AreNotSame(second, third);
            Assert.AreEqual(3, CountAll(_prefab));

            Assert.IsTrue(_pool.TryReturn(first));
            Assert.IsTrue(_pool.TryReturn(second));
            Assert.IsTrue(_pool.TryReturn(third));
        }

        [Test]
        public void ExistingPrewarmAndEnsureSharePoolWithoutDuplicateRegistration()
        {
            _pool.Prewarm(_prefab, 1, 4);
            Assert.IsTrue(_pool.EnsurePrewarmed(_prefab, 3, 4));
            Assert.AreEqual(3, CountAll(_prefab));
        }

        [Test]
        public void ZeroCapacityLegacyPrewarmKeepsEffectiveMinimumPoolLimit()
        {
            _pool.Prewarm(_prefab, 0, 0);
            Assert.IsTrue(_pool.EnsurePrewarmed(_prefab, 1, 1));
            Assert.AreEqual(1, CountAll(_prefab));
        }

        [Test]
        public void InvalidAndDuplicateEntriesDoNotReplaceTheFirstValidKey()
        {
            GameObject other = new GameObject("InvalidVfx");
            try
            {
                SetEntries(NewEntry("valid", _prefab, 1, 2),
                    NewEntry("valid", _prefab, 1, 2),
                    NewEntry("invalid", other, 1, 2));
                LogAssert.Expect(LogType.Error, "[PlayerVfxController] 중복 VFX 키: 'valid'");
                LogAssert.Expect(LogType.Error,
                    "[PlayerVfxController] 'invalid' 프리팹 루트에 LocalPoolRoot와 PooledVisualLifetime이 필요하다");
                BuildEntries();

                Assert.IsTrue(_controller.TryGetPrefab("valid", out GameObject found));
                Assert.AreSame(_prefab, found);
                Assert.IsFalse(_controller.TryGetPrefab("invalid", out _));
                Assert.IsFalse(_controller.TryGetPrefab("missing", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void InitializeWaitsForClientRoleAndPoolThenPrewarms()
        {
            SetEntries(NewEntry("test", _prefab, 2, 4));
            BuildEntries();
            IEnumerator routine = (IEnumerator) typeof(PlayerVfxController)
                .GetMethod("CoInitialize", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, null);

            Assert.IsTrue(routine.MoveNext()); // Network role unresolved.
            SetClientState("Connecting");
            Assert.IsTrue(routine.MoveNext()); // Pool singleton not yet registered.
            RegisterPoolSingleton();
            Assert.IsFalse(routine.MoveNext());
            Assert.AreEqual(2, CountAll(_prefab));
        }

        [Test]
        public void StopAllCannotCancelAnotherOwnersReuseOfOldLease()
        {
            SetEntries(NewEntry("test", _prefab, 1, 2));
            BuildEntries();
            RegisterPoolSingleton();
            SetClientState("Connecting");

            PooledVisualLifetime first = _controller.Play("test");
            Assert.IsNotNull(first);
            uint oldGeneration = first.LeaseGeneration;
            Assert.IsTrue(first.TryStop(oldGeneration));

            GameObject borrowedAgain = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            PooledVisualLifetime reused = borrowedAgain.GetComponent<PooledVisualLifetime>();
            Assert.AreSame(first, reused);
            Assert.AreNotEqual(oldGeneration, reused.LeaseGeneration);

            _controller.StopAll();
            Assert.IsTrue(reused.IsRented);
            Assert.IsTrue(_pool.TryReturn(borrowedAgain));
        }

        [Test]
        public void DisabledControllerCannotStartAnUntrackedLease()
        {
            SetEntries(NewEntry("test", _prefab, 1, 2));
            BuildEntries();
            RegisterPoolSingleton();
            SetClientState("Connecting");
            _controller.enabled = false;

            Assert.IsNull(_controller.Play("test"));
            IDictionary pools = (IDictionary) typeof(LocalObjectPoolService)
                .GetField("_pools", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_pool);
            Assert.AreEqual(0, pools.Count);
        }

        private int CountAll(GameObject prefab)
        {
            IDictionary pools = (IDictionary) typeof(LocalObjectPoolService)
                .GetField("_pools", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_pool);
            object pool = pools[prefab];
            return (int) pool.GetType().GetProperty("CountAll").GetValue(pool);
        }

        private void RegisterPoolSingleton()
        {
            typeof(PersistentSingleton<LocalObjectPoolService>)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, _pool);
        }

        private void SetClientState(string name)
        {
            _connectStateField.SetValue(null,
                Enum.Parse(_connectStateField.FieldType, name));
        }

        private void SetEntries(params PlayerVfxEntry[] entries)
        {
            typeof(PlayerVfxController)
                .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_controller, new List<PlayerVfxEntry>(entries));
        }

        private void BuildEntries()
        {
            typeof(PlayerVfxController)
                .GetMethod("BuildEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, null);
        }

        private static PlayerVfxEntry NewEntry(string key, GameObject prefab,
            int initial, int max)
        {
            PlayerVfxEntry entry = new PlayerVfxEntry();
            Type type = typeof(PlayerVfxEntry);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("_key", flags).SetValue(entry, key);
            type.GetField("_prefab", flags).SetValue(entry, prefab);
            type.GetField("_initialCapacity", flags).SetValue(entry, initial);
            type.GetField("_maxSize", flags).SetValue(entry, max);
            return entry;
        }
    }
}
