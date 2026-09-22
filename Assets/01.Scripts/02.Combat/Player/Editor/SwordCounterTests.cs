using NUnit.Framework;
using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;
using System.Reflection;

namespace Geumeum.Combat.Tests
{
    public sealed partial class WeaponAbilityTests
    {
        private sealed class Sequence : IParryable, IParrySequenceSource
        {
            public uint AttackInstanceId { get; set; } = 17;
            public byte RepeatIndex { get; set; }
            public bool IsDeflect => true;
            public bool IsSequenceActive { get; set; } = true;
            public float ParryWindowDuration => .32f;
            public void OnParried(uint defender) { }
        }

        private ParryGauge Counter(PlayerAttack sword)
        {
            var gauge = sword.gameObject.AddComponent<ParryGauge>();
            InitializeIdentity(sword.gameObject);
            typeof(ParryGauge).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gauge, null);
            Set(sword, "_gauge", gauge);
            return gauge;
        }

        [Test]
        public void CounterAccumulatesOnlyDistinctHitsOfSameSequenceAndClosesGrace()
        {
            var sword = Owner(WeaponKind.Sword); var gauge = Counter(sword); var target = TargetAt();
            uint attacker = target.GetComponent<NetworkIdentity>().netId;
            var source = new Sequence();
            gauge.ServerAddCounter(attacker, 1, source, .3f);
            gauge.ServerAddCounter(attacker, 1, source, .3f);
            Assert.AreEqual(1, gauge.CounterMarks);
            source.RepeatIndex = 1; gauge.ServerAddCounter(attacker, 2, source, .3f);
            source.RepeatIndex = 2; gauge.ServerAddCounter(attacker, 3, source, .3f);
            Assert.AreEqual(3, gauge.CounterMarks);
            ++source.AttackInstanceId; gauge.ServerAddCounter(attacker, 4, source, 0);
            Assert.AreEqual(1, gauge.CounterMarks);
            source.IsSequenceActive = false;
            typeof(ParryGauge).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gauge, null);
            Assert.AreEqual(GameDB.Combat.CounterEndGrace, gauge.CounterRemaining, .001f);
            _clock.GameplayElapsedSeconds += 1.3;
            Assert.AreEqual(0, gauge.CounterMarks);
        }

        [TestCase(1, 24f)] [TestCase(2, 52f)] [TestCase(3, 90f)]
        public void CounterRConsumesAtImpactAndLeavesBasicCooldownUnchanged(int marks, float damage)
        {
            var sword = Owner(WeaponKind.Sword); var gauge = Counter(sword); var target = SkillTargetAt(1.5f);
            var source = new Sequence();
            for (byte hit = 0; hit < marks; ++hit)
            { source.RepeatIndex = hit; gauge.ServerAddCounter(target.GetComponent<NetworkIdentity>().netId, (uint)hit + 1, source, 0); }
            Set(sword, "_swordSkillReady", 100d);
            Assert.AreEqual(ActionRejection.None, sword.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            Assert.AreEqual(0, gauge.CounterMarks);
            Assert.AreEqual(16f, sword.ReservedSkillStamina);
            Assert.AreEqual(100f, sword.GetComponent<PlayerStatus>().Stamina);
            AdvanceSkill(sword, .19);
            Assert.AreEqual(1000f - damage, target.Health, .01f);
            Assert.AreEqual(84f, sword.GetComponent<PlayerStatus>().Stamina);
            Assert.AreEqual(100d, typeof(PlayerAttack).GetField("_swordSkillReady", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sword));
        }

        [Test]
        public void CounterMissSpendsAndPreImpactHitDiscardsWithoutSpending()
        {
            var sword = Owner(WeaponKind.Sword); var gauge = Counter(sword); var target = TargetAt();
            gauge.ServerAddCounter(target.GetComponent<NetworkIdentity>().netId, 1, new Sequence(), 0);
            sword.ServerRequestWeaponSkill(1, 7, 2, Vector2.up);
            sword.ServerInterruptForHit();
            Assert.AreEqual(0, sword.ReservedSkillStamina);
            Assert.AreEqual(0, gauge.CounterMarks);
            Assert.AreEqual(100, sword.GetComponent<PlayerStatus>().Stamina);
            gauge.ServerAddCounter(target.GetComponent<NetworkIdentity>().netId, 2, new Sequence(), 0);
            sword.ServerRequestWeaponSkill(2, 7, 2, Vector2.down);
            AdvanceSkill(sword, .19);
            Assert.AreEqual(84, sword.GetComponent<PlayerStatus>().Stamina);
            Assert.AreEqual(0, gauge.CounterMarks);
        }

        [Test]
        public void BufferedRollWinsOverBasicInputOnRecoveryEndFrame()
        {
            var sword = Owner(WeaponKind.Sword);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isLocalPlayer)).SetValue(sword.netIdentity, true);
            Set(sword, "_actionFacingEnd", 10.05d);
            int rolls = 0, attacks = 0;
            Assert.IsTrue(sword.TryBufferCombatAction(4, () => ++rolls));
            Assert.IsTrue(sword.TryBufferCombatAction(1, () => ++attacks));
            _clock.GameplayElapsedSeconds = 10.06d;
            Assert.IsTrue(sword.TryBufferCombatAction(1, () => ++attacks));
            Assert.AreEqual(1, rolls); Assert.AreEqual(0, attacks);
            Assert.IsFalse(sword.TryBufferCombatAction(1, () => ++attacks));
        }

        [Test]
        public void DamageDiscardsCounterReservationWithoutSpendingStamina()
        {
            var sword = Owner(WeaponKind.Sword); var gauge = Counter(sword); var target = TargetAt();
            gauge.ServerAddCounter(target.GetComponent<NetworkIdentity>().netId, 1, new Sequence(), 0);
            Assert.AreEqual(ActionRejection.None, sword.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            sword.ServerDiscardCombatRewardsOnDamage();
            AdvanceSkill(sword, .3);
            Assert.AreEqual(0, gauge.CounterMarks);
            Assert.AreEqual(0, sword.ReservedSkillStamina);
            Assert.AreEqual(100, sword.GetComponent<PlayerStatus>().Stamina);
        }

        [TestCase(false, 952f)] [TestCase(true, 956f)]
        public void WedgeBurstUsesFiringTalentSnapshot(bool precision, float remainingHealth)
        {
            var bow = Owner(WeaponKind.Bow); var target = TargetAt(); Install(bow, target);
            var stats = new CombatStatsSnapshot { WedgeNormalBurstPercent = 20, WedgePrecisionBurstPercent = 10, WedgeFirstHitCooldown = 1 };
            Set(bow, "_bowSkillReady", 20d);
            if (!precision)
            {
                PlayerAttack.ServerNotifyDirectHit(bow.netId, 1, false, false, target, target.transform.position,
                    new Vector3(0, 1.2f, 0), Vector3.forward, 5, 20, stats);
            }

            PlayerAttack.ServerNotifyDirectHit(bow.netId, 2, precision, false, target, target.transform.position,
                new Vector3(0, 1.2f, 0), Vector3.forward, 5, 20, stats);
            Assert.AreEqual(remainingHealth, target.Health, .01f);
            Assert.AreEqual(19d, typeof(PlayerAttack).GetField("_bowSkillReady", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bow));
        }
    }
}
