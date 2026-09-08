// 서드파티
using NUnit.Framework;

namespace Geumeum.Common.Tests
{
    public class CombatClockTests
    {
        /****************************************
        *               Constants
        ****************************************/

        private const uint   PLAYER = 1u;
        private const uint   ENEMY  = 2u;
        private const uint   OTHER  = 3u;
        private const float  GRACE  = 5f; // 게임 디자인 6.5
        private const double NOW    = 100d;

        /****************************************
        *                 Setup
        ****************************************/

        [SetUp]
        [TearDown]
        public void Clear()
        {
            // 정적 상태라 테스트 사이에 새면 다음 검사가 오염된다
            CombatClock.ClearAll();
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void UnknownTargetHasNeverFought()
        {
            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(PLAYER));
            Assert.IsFalse(CombatClock.IsInCombat(PLAYER, NOW, GRACE));
        }

        [Test]
        public void BothSidesOfAHitAreRecorded()
        {
            // 플레이어가 잡귀를 때렸다 - 맞은 잡귀와 때린 플레이어 모두 전투 중이다
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            Assert.AreEqual(NOW, CombatClock.GetLastCombatTime(ENEMY));
            Assert.AreEqual(NOW, CombatClock.GetLastCombatTime(PLAYER));
            Assert.IsTrue(CombatClock.IsInCombat(PLAYER, NOW, GRACE));
        }

        [Test]
        public void UnrelatedTargetStaysOutOfCombat()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            Assert.IsFalse(CombatClock.IsInCombat(OTHER, NOW, GRACE));
        }

        [Test]
        public void CombatOpensExactlyAfterGrace()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            Assert.IsTrue(CombatClock.IsInCombat(PLAYER, NOW + 4.9d, GRACE));
            Assert.IsFalse(CombatClock.IsInCombat(PLAYER, NOW + GRACE, GRACE));
        }

        [Test]
        public void EnvironmentDamageRecordsVictimOnly()
        {
            // 결계 피해처럼 가해자가 없는 경우
            CombatClock.ServerNotifyDamage(PLAYER, DamageInfo.NO_ATTACKER, NOW);

            Assert.AreEqual(NOW, CombatClock.GetLastCombatTime(PLAYER));
            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(DamageInfo.NO_ATTACKER));
        }

        [Test]
        public void LaterHitAdvancesTheClock()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW + 3d);

            Assert.AreEqual(NOW + 3d, CombatClock.GetLastCombatTime(PLAYER));
        }

        [Test]
        public void OutOfOrderHitDoesNotRewindTheClock()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW + 3d);

            // 늦게 도착한 오래된 기록이 시각을 되돌리면 상호작용이 다시 열린다
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            Assert.AreEqual(NOW + 3d, CombatClock.GetLastCombatTime(PLAYER));
        }

        [Test]
        public void SelfDamageIsRecordedOnce()
        {
            CombatClock.ServerNotifyDamage(PLAYER, PLAYER, NOW);

            Assert.AreEqual(NOW, CombatClock.GetLastCombatTime(PLAYER));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1d)]
        public void MalformedTimeIsIgnored(double serverTime)
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, serverTime);

            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(PLAYER));
        }

        [Test]
        public void ForgetRemovesOnlyThatTarget()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            CombatClock.Forget(PLAYER);

            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(PLAYER));
            Assert.AreEqual(NOW, CombatClock.GetLastCombatTime(ENEMY));
        }

        [Test]
        public void ClearAllWipesEverything()
        {
            CombatClock.ServerNotifyDamage(ENEMY, PLAYER, NOW);

            CombatClock.ClearAll();

            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(PLAYER));
            Assert.AreEqual(CombatClock.NEVER, CombatClock.GetLastCombatTime(ENEMY));
        }
    }
}
