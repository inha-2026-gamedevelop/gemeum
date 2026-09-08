// System
using System;

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
            var rules = new DropRules(DROP_CHANCE, new Random(SEED));

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
            var first  = new DropRules(DROP_CHANCE, new Random(SEED));
            var second = new DropRules(DROP_CHANCE, new Random(SEED));

            for (int i = 0; i < 200; ++i)
            {
                Assert.AreEqual(first.ShouldDrop(false), second.ShouldDrop(false));
                Assert.AreEqual(first.RollWeaponKind(), second.RollWeaponKind());
            }
        }

        [Test]
        public void BossAlwaysDropsWithoutConsumingRandom()
        {
            var withBoss    = new DropRules(DROP_CHANCE, new Random(SEED));
            var withoutBoss = new DropRules(DROP_CHANCE, new Random(SEED));

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
            var zero      = new DropRules(0f, new Random(SEED));
            var untouched = new Random(SEED);

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
            var rules = new DropRules(1f, new Random(SEED));

            for (int i = 0; i < 50; ++i)
            {
                Assert.IsTrue(rules.ShouldDrop(false));
            }
        }

        [Test]
        public void WeaponKindStaysInRangeAndCoversAll()
        {
            var rules = new DropRules(DROP_CHANCE, new Random(SEED));

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
            Assert.Throws<ArgumentOutOfRangeException>(() => new DropRules(chance, new Random(SEED)));
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
    }
}
