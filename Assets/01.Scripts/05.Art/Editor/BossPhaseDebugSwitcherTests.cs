using System.Reflection;
using Geumeum.AI;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Geumeum.Art.Tests
{
    public sealed class BossPhaseDebugSwitcherTests
    {
        [Test]
        public void BossLookupIgnoresUnspawnedPreviewAndInactiveServerBoss()
        {
            var preview = new GameObject("UnspawnedBossPreview", typeof(NetworkIdentity), typeof(BossStatus), typeof(BossController));
            var server = new GameObject("SpawnedServerBoss", typeof(NetworkIdentity), typeof(BossStatus), typeof(BossController));
            NetworkIdentity identity = server.GetComponent<NetworkIdentity>();
            const uint netId = 9399u;
            Assert.IsFalse(NetworkServer.spawned.ContainsKey(netId));
            try
            {
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(identity, null);
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId)).SetValue(identity, netId);
                NetworkServer.spawned.Add(netId, identity);
                var resolve = typeof(BossPhaseDebugSwitcher).GetMethod("ResolveServerBoss", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.AreSame(server.GetComponent<BossController>(), resolve.Invoke(null, null));
                server.SetActive(false);
                Assert.IsNull(resolve.Invoke(null, null));
            }
            finally
            {
                NetworkServer.spawned.Remove(netId);
                // 파괴 전에 서버 콜백을 해제해 테스트가 실제 런 수명에 참여하지 않게 한다.
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, false);
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(server);
            }
        }
    }
}
