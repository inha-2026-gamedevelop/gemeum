// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Systems.Tests
{
    public sealed class CurrencyManagerTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _runObject;
        private GameObject _walletObject;
        private RunManager _run;
        private CurrencyManager _wallet;
        private RunManager _previousRun;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            _previousRun = RunManager.ServerInstance;

            _runObject = new GameObject("CurrencyManagerTestRun", typeof(NetworkIdentity));
            _run = _runObject.AddComponent<RunManager>();
            Set(_run, "_runIdValue", 5u);
            Set(_run, "_phase", RunPhase.Shrink1);
            SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), _run);

            _walletObject = new GameObject("CurrencyManagerTestWallet", typeof(NetworkIdentity));
            _wallet = _walletObject.AddComponent<CurrencyManager>();
            NetworkIdentity identity = _walletObject.GetComponent<NetworkIdentity>();
            Invoke(identity, "InitializeNetworkBehaviours");
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            Set(_wallet, "_serverBalance", 100);
            Set(_wallet, "_serverBalanceRevision", 1u);
            Set(_wallet, "_balance", 100);
            Set(_wallet, "_balanceRevision", 1u);
        }

        [TearDown]
        public void TearDown()
        {
            SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), _previousRun);
            if (_walletObject != null)
            {
                Object.DestroyImmediate(_walletObject);
            }

            if (_runObject != null)
            {
                Object.DestroyImmediate(_runObject);
            }

            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        [Test]
        public void ReviveKeyAndRawBalanceAreFinalBeforeBalanceNotification()
        {
            ParticipantId participant = new ParticipantId(17UL);
            bool callbackRan = false;
            bool retrySucceeded = false;
            int retryLoss = -1;
            int callbackBalance = -1;
            uint callbackRevision = 0u;
            _wallet.BalanceChanged += _ =>
            {
                callbackRan = true;
                callbackBalance = _wallet.Balance;
                callbackRevision = _wallet.Revision;
                retrySucceeded = _wallet.TryApplyReviveLoss(participant, 3u, 0.2f,
                    out retryLoss);
            };

            Assert.IsTrue(_wallet.TryApplyReviveLoss(participant, 3u, 0.2f,
                out int loss));
            // 비-spawn EditMode NetworkBehaviour에서는 SyncVar hook가 자동 실행되지 않는다
            // 실제 publish 시점과 같은 인자로 hook를 호출해 재진입 경계를 검증한다
            Invoke(_wallet, "OnBalanceChanged", 100, 80);

            Assert.AreEqual(20, loss);
            Assert.IsTrue(callbackRan);
            Assert.AreEqual(80, callbackBalance);
            Assert.AreEqual(2u, callbackRevision);
            Assert.IsTrue(retrySucceeded);
            Assert.AreEqual(0, retryLoss);
            Assert.AreEqual(80, _wallet.Balance);
            Assert.AreEqual(2u, _wallet.Revision);
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, PRIVATE_INSTANCE).Invoke(target, arguments);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private static void SetStaticProperty(System.Type type, string name, object value)
        {
            type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, value);
        }
    }
}
