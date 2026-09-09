// Unity
using UnityEngine;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;

namespace Geumeum.Combat.Tests
{
    /// <summary> 패링 게이지 snapshot의 유한값 경계를 검증한다 </summary>
    public sealed class ParryGaugeSnapshotTests
    {
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.01f)]
        public void InvalidGaugeCannotRestore(float value)
        {
            GameObject gameObject = new GameObject("ParryGaugeSnapshotTest",
                typeof(NetworkIdentity), typeof(ParryGauge));
            ParryGauge gauge = gameObject.GetComponent<ParryGauge>();
            NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .Invoke(identity, null);
            identity.GetType().GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, true);
            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                ParryGauge = value,
            };

            try
            {
                Assert.IsFalse(gauge.Validate(snapshot, out _));
                Assert.IsFalse(gauge.Restore(snapshot));
                Assert.AreEqual(0f, gauge.Gauge);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
