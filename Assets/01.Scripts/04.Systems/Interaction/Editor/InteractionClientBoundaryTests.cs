// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems.Tests
{
    public sealed class InteractionClientBoundaryTests
    {
        [TestCase(0u, 10u, false)]
        [TestCase(7u, 0u, false)]
        [TestCase(7u, 10u, true)]
        public void ClientPickupPreviewUsesReplicatedEquipmentWithoutCallingServer(
            uint itemId, uint actorId, bool expected)
        {
            var gameObject = new GameObject("ClientPickupPreview", typeof(NetworkIdentity));
            try
            {
                NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isClient))
                    .SetValue(identity, true);
                DroppedItem item = gameObject.AddComponent<DroppedItem>();
                // EditMode에서 Mirror의 컴포넌트 바인딩 수명을 명시적으로 진행한다.
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(identity, null);
                typeof(DroppedItem).GetField("_equipment",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(item, new WeaponCombatSnapshot { InstanceId = itemId });

                Assert.AreEqual(expected, item.CanInteract(actorId));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
