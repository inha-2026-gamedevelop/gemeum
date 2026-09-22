using System.Reflection;
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Geumeum.Combat.Tests
{
    public sealed class BotThreatTrackerTests
    {
        private BotSkillProfileSO _skill;
        [SetUp] public void SetUp()
        {
            _skill = ScriptableObject.CreateInstance<BotSkillProfileSO>();
            _skill.ReactionMin = _skill.ReactionMax = .3f;
            _skill.TimingSigma = 0f;
            _skill.MissChance = 0f;
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_skill); }
        private static void Observe(BotThreatTracker tracker, BotPerception.Target source, uint attack,
            double now, double impact)
        {
            typeof(BotThreatTracker).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(tracker, new object[] { source, attack, (byte)0, now, impact, Vector3.forward, true, true });
        }
        [Test] public void LateObservationNeverSkipsReactionDelay()
        {
            var tracker = new BotThreatTracker(_skill, new System.Random(1));
            Observe(tracker, new BotPerception.Target(), 4u, 10d, 10.1d);
            Assert.That(tracker.Threats[0].ExecuteAt, Is.EqualTo(10.3d).Within(.00001));
            Assert.That(tracker.EarliestImpact(10.1d), Is.EqualTo(double.PositiveInfinity));
        }
        [Test] public void SameObservedAttackDoesNotRerollOrDelayOnEveryDecision()
        {
            var tracker = new BotThreatTracker(_skill, new System.Random(1));
            var source = new BotPerception.Target();
            Observe(tracker, source, 4u, 10d, 11d);
            Observe(tracker, source, 4u, 10.2d, 11.2d);
            Assert.AreEqual(1, tracker.Threats.Count);
            Assert.AreEqual(10d, tracker.Threats[0].ObservedAt);
            Assert.AreEqual(11d, tracker.Threats[0].Impact);
        }
        [Test] public void MissedCueDoesNotBlockAttackCommit()
        {
            _skill.MissChance = 1f;
            var tracker = new BotThreatTracker(_skill, new System.Random(1));
            Observe(tracker, new BotPerception.Target(), 4u, 10d, 11d);
            Assert.IsTrue(tracker.Threats[0].Missed);
            Assert.AreEqual(double.PositiveInfinity, tracker.EarliestImpact(10.5d));
        }
        [Test] public void PastImpactCannotBlockFurtherAttacksWhileAnimationFinishes()
        {
            var tracker = new BotThreatTracker(_skill, new System.Random(1));
            Observe(tracker, new BotPerception.Target(), 4u, 10d, 11d);
            Assert.AreEqual(11d, tracker.EarliestImpact(10.8d));
            Assert.AreEqual(double.PositiveInfinity, tracker.EarliestImpact(11.01d));
        }

        [Test] public void ResetRemovesOldAttackReservations()
        {
            var tracker = new BotThreatTracker(_skill, new System.Random(1));
            Observe(tracker, new BotPerception.Target(), 4u, 10d, 11d);
            tracker.Clear();
            Assert.AreEqual(0, tracker.Threats.Count);
            Assert.AreEqual(double.PositiveInfinity, tracker.EarliestImpact(20d));
        }

        [TestCase(10.5d, true, 4u, true)]
        [TestCase(11d, true, 4u, false)]
        [TestCase(10.5d, false, 4u, false)]
        [TestCase(10.5d, true, 5u, false)]
        public void ExpiredCancelledOrReplacedCueCannotKeepThreatSlot(double now, bool active, uint attack, bool expected)
        {
            var config = ScriptableObject.CreateInstance<BotConfigSO>();
            var self = new GameObject("ThreatSelf");
            var target = new GameObject("ThreatSource", typeof(NetworkIdentity));
            try
            {
                config.ObstacleMask = 0;
                target.transform.position = Vector3.forward * 5f;
                var cue = new BossTelegraphCue { AttackInstanceId = attack, IsActive = active, StartServerTime = 10d, ImpactServerTime = 11d };
                var source = new BotPerception.Target { Identity = target.GetComponent<NetworkIdentity>(), Health = new HealthStub(), Telegraph = new CueStub(cue) };
                var sensor = new BotPerception(self.transform, config, _skill);
                var tracker = new BotThreatTracker(_skill, new System.Random(1));
                Observe(tracker, source, 4u, 10d, 11d);
                Assert.AreEqual(expected, tracker.Valid(tracker.Threats[0], sensor, Vector3.forward, now));
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(self); Object.DestroyImmediate(target); }
        }
        private sealed class HealthStub : IDamageable
        {
            public bool IsDead => false;
            public void TakeDamage(in DamageInfo damage) { }
        }
        private sealed class CueStub : IBossTelegraphSource
        {
            public CueStub(BossTelegraphCue cue) { TelegraphCue = cue; TelegraphCues = new[] { cue }; }
            public BossTelegraphCue TelegraphCue { get; }
            public System.Collections.Generic.IReadOnlyList<BossTelegraphCue> TelegraphCues { get; }
            public event System.Action<BossTelegraphCue> TelegraphChanged { add { } remove { } }
        }
    }
}
