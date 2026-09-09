// System
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

namespace Geumeum.Network.Tests
{
    public sealed class GeumeumNetworkManagerConnectionTests
    {
        [TearDown]
        public void TearDown()
        {
            InvokeStatic("ResetClientSceneSyncExpectation");
        }

        [TestCase("", "", false)]
        [TestCase("Main", "Main", false)]
        [TestCase("Lobby", "", true)]
        [TestCase("Field", "Main", true)]
        public void InitialSceneSyncMatchesTheSceneMessageCondition(
            string currentNetworkScene, string offlineScene, bool expected)
        {
            Assert.AreEqual(expected, InvokeRequiresSceneSync(
                currentNetworkScene, offlineScene));
        }

        [Test]
        public void AcceptedAuthCommitsSceneExpectationBeforeClientConnectEvent()
        {
            GameObject host = new GameObject("AuthenticatorSceneSyncFixture");
            GeumeumAuthenticator authenticator = host.AddComponent<GeumeumAuthenticator>();
            bool? observedDuringAccept = null;
            authenticator.OnClientAuthenticated.AddListener(() =>
                observedDuringAccept = GeumeumAuthenticator.ClientSceneSyncExpected);

            try
            {
                InvokeAuthResponse(authenticator, new AuthResponseMessage
                {
                    Code = AuthResultCode.Accepted,
                    Reason = string.Empty,
                    SceneSyncRequired = true,
                });

                Assert.AreEqual(true, observedDuringAccept);
                Assert.IsTrue(GeumeumAuthenticator.ClientSceneSyncExpected);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static bool InvokeRequiresSceneSync(string currentNetworkScene,
            string offlineScene)
        {
            MethodInfo method = typeof(GeumeumAuthenticator).GetMethod(
                "RequiresInitialSceneSync", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool) method.Invoke(null,
                new object[] { currentNetworkScene, offlineScene });
        }

        private static void InvokeAuthResponse(GeumeumAuthenticator authenticator,
            AuthResponseMessage response)
        {
            MethodInfo method = typeof(GeumeumAuthenticator).GetMethod(
                "OnAuthResponse", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(authenticator, new object[] { response });
        }

        private static void InvokeStatic(string methodName)
        {
            MethodInfo method = typeof(GeumeumAuthenticator).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }
    }
}
