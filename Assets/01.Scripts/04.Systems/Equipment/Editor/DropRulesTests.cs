// System
using System;
using SystemRandom = System.Random;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems.Tests
{
    public class DropRulesTests
    {
        /****************************************
        *               Constants
        ****************************************/

        private const int   SEED         = 927;
        private const int   SAMPLE_COUNT = 20000;
        private const float DROP_CHANCE  = 0.15f;

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void DropRateStaysNearConfiguredChance()
        {
            var rules = new DropRules(DROP_CHANCE, new SystemRandom(SEED));

            int dropped = 0;
            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                if (rules.ShouldDrop(false))
                {
                    ++dropped;
                }
            }

            float actual = ((float) dropped) / SAMPLE_COUNT;
            Assert.That(actual, Is.EqualTo(DROP_CHANCE).Within(0.01f),
                "표본 2만에서 설정 확률과 1%p 넘게 벌어지면 추첨이 치우친 것이다");
        }

        [Test]
        public void SameSeedProducesSameSequence()
        {
            var first  = new DropRules(DROP_CHANCE, new SystemRandom(SEED));
            var second = new DropRules(DROP_CHANCE, new SystemRandom(SEED));

            for (int i = 0; i < 200; ++i)
            {
                Assert.AreEqual(first.ShouldDrop(false), second.ShouldDrop(false));
                Assert.AreEqual(first.RollWeaponKind(), second.RollWeaponKind());
            }
        }

        [Test]
        public void BossAlwaysDropsWithoutConsumingRandom()
        {
            var withBoss    = new DropRules(DROP_CHANCE, new SystemRandom(SEED));
            var withoutBoss = new DropRules(DROP_CHANCE, new SystemRandom(SEED));

            // 보스 판정이 난수를 먹으면 이후 잡귀 추첨 결과가 갈린다
            Assert.IsTrue(withBoss.ShouldDrop(true));

            for (int i = 0; i < 50; ++i)
            {
                Assert.AreEqual(withoutBoss.ShouldDrop(false), withBoss.ShouldDrop(false));
            }
        }

        [Test]
        public void ZeroChanceNeverDropsAndKeepsSequence()
        {
            var zero      = new DropRules(0f, new SystemRandom(SEED));
            var untouched = new SystemRandom(SEED);

            for (int i = 0; i < 50; ++i)
            {
                Assert.IsFalse(zero.ShouldDrop(false));
            }

            // 확률 0이 난수를 소비했다면 아래 무기 추첨이 어긋난다
            Assert.AreEqual((WeaponKind) untouched.Next(0, Constants.Combat.WEAPON_KIND_COUNT),
                zero.RollWeaponKind());
        }

        [Test]
        public void FullChanceAlwaysDrops()
        {
            var rules = new DropRules(1f, new SystemRandom(SEED));

            for (int i = 0; i < 50; ++i)
            {
                Assert.IsTrue(rules.ShouldDrop(false));
            }
        }

        [Test]
        public void WeaponKindStaysInRangeAndCoversAll()
        {
            var rules = new DropRules(DROP_CHANCE, new SystemRandom(SEED));

            var seen = new bool[Constants.Combat.WEAPON_KIND_COUNT];
            for (int i = 0; i < 300; ++i)
            {
                WeaponKind kind = rules.RollWeaponKind();
                Assert.IsTrue(Enum.IsDefined(typeof(WeaponKind), kind));
                seen[(int) kind] = true;
            }

            for (int i = 0; i < seen.Length; ++i)
            {
                Assert.IsTrue(seen[i], $"무기 {(WeaponKind) i}가 300회 안에 한 번도 나오지 않았다");
            }
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        public void InvalidChanceIsRejected(float chance)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DropRules(chance, new SystemRandom(SEED)));
        }

        [Test]
        public void NullRandomIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new DropRules(DROP_CHANCE, null));
        }

        [Test]
        public void BossRewardUsesLastFieldLevel()
        {
            // 게임 디자인 6.4의 3차 가중치는 보스 보상 전용이다
            Assert.AreEqual(DropRules.BOSS_REWARD_FIELD_LEVEL, DropRules.ResolveFieldLevel(true, 0));
            Assert.AreEqual(3, DropRules.BOSS_REWARD_FIELD_LEVEL);

            Assert.AreEqual(0, DropRules.ResolveFieldLevel(false, 0));
            Assert.AreEqual(2, DropRules.ResolveFieldLevel(false, 2));
        }

        [Test]
        public void DeathClaimUsesRunAndEnemyIdentity()
        {
            var rules = new DropRules(DROP_CHANCE, new SystemRandom(SEED));

            Assert.IsFalse(rules.TryClaimDeath(RunId.None, 11u));
            Assert.IsFalse(rules.TryClaimDeath(new RunId(1u), 0u));
            Assert.IsTrue(rules.TryClaimDeath(new RunId(1u), 11u));
            Assert.IsFalse(rules.TryClaimDeath(new RunId(1u), 11u));
            Assert.IsTrue(rules.TryClaimDeath(new RunId(2u), 11u));
        }

        [Test]
        public void DuplicateDeathDoesNotConsumeDropRandom()
        {
            var random = new CountingRandom(0.1d);
            var rules = new DropRules(DROP_CHANCE, random);

            Assert.IsTrue(rules.TryClaimDeath(new RunId(1u), 11u));
            Assert.IsTrue(rules.ShouldDrop(false));
            int callsAfterFirstDecision = random.NextDoubleCalls;

            Assert.IsFalse(rules.TryClaimDeath(new RunId(1u), 11u));
            Assert.AreEqual(callsAfterFirstDecision, random.NextDoubleCalls);
        }

        [Test]
        public void SpawnSnapshotSelectsOriginalFieldAfterCurrentFieldChanges()
        {
            EnemyDiedEvent died = EnemyDiedEvent.ForEnemy(11u, EnemyKind.Heokkaebi,
                Vector3.zero, DamageInfo.NO_ATTACKER, 1, 1.25f);
            int currentFieldLevel = 2;

            Assert.AreNotEqual(currentFieldLevel, died.FieldLevelAtSpawn);
            Assert.IsTrue(DropRules.TryResolveFieldLevel(died.IsBoss, died.FieldLevelAtSpawn,
                out int resolvedFieldLevel));
            Assert.AreEqual(1, resolvedFieldLevel);
            Assert.AreEqual(1.25f, died.RewardMultiplier);
        }

        [Test]
        public void InvalidSpawnFieldIsRejectedBeforeDropResolution()
        {
            Assert.IsFalse(DropRules.TryResolveFieldLevel(false, -1, out _));
            Assert.IsFalse(DropRules.TryResolveFieldLevel(false,
                Constants.Combat.FIELD_LEVEL_COUNT, out _));
            Assert.IsFalse(DropRules.TryResolveFieldLevel(true,
                DropRules.BOSS_REWARD_FIELD_LEVEL - 1, out _));
        }

        private sealed class CountingRandom : SystemRandom
        {
            private readonly double _value;

            public int NextDoubleCalls { get; private set; }

            public CountingRandom(double value)
            {
                _value = value;
            }

            public override double NextDouble()
            {
                ++NextDoubleCalls;
                return _value;
            }
        }
    }
}
