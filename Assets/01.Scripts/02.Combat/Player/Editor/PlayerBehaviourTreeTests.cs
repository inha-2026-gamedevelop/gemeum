using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Geumeum.Combat.Tests
{
    public sealed class PlayerBehaviourTreeTests
    {
        private static BotScheduledAction Reservation() => new BotScheduledAction
        {
            Pending = true, Run = 1u, Scene = 2u, Generation = 3u, Target = 4u,
            ExecuteAt = 10d, ExpiresAt = 10.2d,
        };

        [Test]
        public void ReservationDoesNotExecuteEarlyOrTwice()
        {
            var action = Reservation();
            Assert.IsFalse(action.TryConsume(9.9d, 1u, 2u, 3u, 4u, true));
            Assert.IsTrue(action.Pending);
            Assert.IsTrue(action.TryConsume(10d, 1u, 2u, 3u, 4u, true));
            Assert.IsFalse(action.TryConsume(10.1d, 1u, 2u, 3u, 4u, true));
        }

        [TestCase(2u, 2u, 3u, 4u, true, 10d)]
        [TestCase(1u, 3u, 3u, 4u, true, 10d)]
        [TestCase(1u, 2u, 4u, 4u, true, 10d)]
        [TestCase(1u, 2u, 3u, 5u, true, 10d)]
        [TestCase(1u, 2u, 3u, 4u, false, 9d)]
        [TestCase(1u, 2u, 3u, 4u, true, 10.3d)]
        public void InvalidReservationIsDiscardedInsteadOfExecutingOnResume(
            uint run, uint scene, uint generation, uint target, bool visible, double now)
        {
            var action = Reservation();
            Assert.IsFalse(action.TryConsume(now, run, scene, generation, target, visible));
            Assert.IsFalse(action.Pending);
            Assert.IsFalse(action.TryConsume(10d, 1u, 2u, 3u, 4u, true));
        }

        [Test]
        public void ReactionSamplesAreSeededAndNeverBeforeFirstObservation()
        {
            var profile = ScriptableObject.CreateInstance<BotSkillProfileSO>();
            try
            {
                var first = new System.Random(77);
                var replay = new System.Random(77);
                for (int i = 0; i < 200; ++i)
                {
                    double delay = profile.SampleReaction(first);
                    Assert.AreEqual(delay, profile.SampleReaction(replay));
                    Assert.That(delay, Is.InRange((double)profile.ReactionMin, (double)profile.ReactionMax));
                    Assert.GreaterOrEqual(12d + delay, 12d);
                }
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void SensorRejectsBehindOutOfRangeAndOccludedTargets()
        {
            var config = ScriptableObject.CreateInstance<BotConfigSO>();
            var skill = ScriptableObject.CreateInstance<BotSkillProfileSO>();
            var self = new GameObject("SensorSelf");
            var enemy = new GameObject("SensorTarget", typeof(NetworkIdentity));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                config.EyeHeight = 0f;
                var sensor = new BotPerception(self.transform, config, skill);
                var target = new BotPerception.Target { Identity = enemy.GetComponent<NetworkIdentity>(), Health = new HealthStub() };
                enemy.transform.position = Vector3.back * 5f;
                Assert.IsFalse(sensor.Visible(target, Vector3.forward));
                enemy.transform.position = Vector3.forward * (skill.VisionRange + 1f);
                Assert.IsFalse(sensor.Visible(target, Vector3.forward));
                enemy.transform.position = Vector3.forward * 5f;
                wall.transform.position = Vector3.forward * 2f;
                Physics.SyncTransforms();
                Assert.IsFalse(sensor.Visible(target, Vector3.forward));
                wall.SetActive(false);
                Physics.SyncTransforms();
                Assert.IsTrue(sensor.Visible(target, Vector3.forward));
            }
            finally
            {
                Object.DestroyImmediate(self); Object.DestroyImmediate(enemy); Object.DestroyImmediate(wall);
                Object.DestroyImmediate(config); Object.DestroyImmediate(skill);
            }
        }

        private sealed class HealthStub : IDamageable
        {
            public bool IsDead => false;
            public void TakeDamage(in DamageInfo damage) { }
        }

        [Test]
        public void ContinuousSightKeepsFirstObservationButReacquisitionStartsNewDelay()
        {
            var profile = ScriptableObject.CreateInstance<BotSkillProfileSO>();
            try
            {
                profile.MissChance = 0f;
                var observation = new BotObservation(profile, new System.Random(9));
                observation.Observe(7u, 10d);
                double ready = observation.ReadyAt;
                observation.Observe(7u, 10.1d);
                Assert.AreEqual(10d, observation.ObservedAt);
                Assert.AreEqual(ready, observation.ReadyAt);
                observation.Observe(0u, 11d);
                observation.Observe(7u, 15d);
                Assert.AreEqual(15d, observation.ObservedAt);
                Assert.GreaterOrEqual(observation.ReadyAt, 15d + profile.ReactionMin);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void MissedTargetIsNotRerolledEveryTickOrIgnoredForever()
        {
            var profile = ScriptableObject.CreateInstance<BotSkillProfileSO>();
            try
            {
                profile.MissChance = 1f;
                var observation = new BotObservation(profile, new System.Random(9));
                observation.Observe(7u, 10d);
                Assert.IsTrue(observation.Missed);
                double retry = observation.ReadyAt + profile.ObservationRetrySeconds;
                profile.MissChance = 0f;
                observation.Observe(7u, retry - 0.01d);
                Assert.IsTrue(observation.Missed);
                observation.Observe(7u, retry);
                Assert.IsFalse(observation.Missed);
                Assert.Greater(observation.ReadyAt, retry);
            }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
