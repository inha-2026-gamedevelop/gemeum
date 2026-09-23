using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    public sealed class BowLaserIntegrationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void StaleChargeLeaseCannotStopNewRental()
        {
            var owner = new GameObject("Bow owner");
            var visual = new GameObject("Bow pooled charge", typeof(LocalPoolRoot), typeof(PooledVisualLifetime));
            try
            {
                var presentation = owner.AddComponent<PlayerCombatPresentation>();
                var lifetime = visual.GetComponent<PooledVisualLifetime>();
                lifetime.OnRentFromPool();
                uint stale = lifetime.LeaseGeneration;
                lifetime.OnReturnToPool();
                lifetime.OnRentFromPool();
                Set(presentation, "_bowCharge", lifetime);
                Set(presentation, "_bowChargeLease", stale);
                typeof(PlayerCombatPresentation).GetMethod("StopBowCharge", Private).Invoke(presentation, null);
                Assert.IsTrue(lifetime.IsRented);
                Assert.IsNull(typeof(PlayerCombatPresentation).GetField("_bowCharge", Private).GetValue(presentation));
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(visual); }
        }

        [Test]
        public void DisableClearsShotIdentityAndReleaseSuppression()
        {
            var owner = new GameObject("Bow owner");
            try
            {
                var presentation = owner.AddComponent<PlayerCombatPresentation>();
                Set(presentation, "_bowHasShot", true);
                Set(presentation, "_bowHasReleasedHold", true);
                Set(presentation, "_bowLaserCueTime", 14d);
                typeof(PlayerCombatPresentation).GetMethod("OnDisable", Private).Invoke(presentation, null);
                Assert.AreEqual(false, typeof(PlayerCombatPresentation).GetField("_bowHasShot", Private).GetValue(presentation));
                Assert.AreEqual(false, typeof(PlayerCombatPresentation).GetField("_bowHasReleasedHold", Private).GetValue(presentation));
                Assert.AreEqual(double.NegativeInfinity, typeof(PlayerCombatPresentation).GetField("_bowLaserCueTime", Private).GetValue(presentation));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ReleaseClockFreezesWhilePausedAndNeverRunsBackwards()
        {
            MethodInfo advance = typeof(PlayerCombatPresentation).GetMethod("AdvanceBowVisualTime",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(.2f, advance.Invoke(null, new object[] { .2f, .5f, true }));
            Assert.AreEqual(.2f, advance.Invoke(null, new object[] { .2f, -.5f, false }));
            Assert.AreEqual(.3f, (float)advance.Invoke(null, new object[] { .2f, .1f, false }), .0001f);
        }

        [TestCase(10d, 12d, true)]
        [TestCase(12d, 12d, true)]
        [TestCase(12.1d, 12d, false)]
        public void DelayedHoldingSnapshotCannotRestartReleasedCharge(double holdStart, double releaseTime, bool suppressed)
        {
            MethodInfo rule = typeof(PlayerCombatPresentation).GetMethod("IsBowHoldReleased",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(suppressed, rule.Invoke(null, new object[] { holdStart, releaseTime }));
        }

        private static void Set(object target, string field, object value)
        { target.GetType().GetField(field, Private).SetValue(target, value); }
    }
}
