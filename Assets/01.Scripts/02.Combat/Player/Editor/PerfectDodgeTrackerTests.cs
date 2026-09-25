// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat.Tests
{
    /// <summary> 극한 회피 서버 판정 규칙과 클라이언트 수신 창구(COMBAT_STYLE_DESIGN S7) </summary>
    public sealed class PerfectDodgeTrackerTests
    {
        private const float  WINDOW      = 0.15f;
        private const double ROLL_START  = 10.0;
        private const double ROLL_END    = 10.45; // 구르기 무적 0.45초
        private const uint   ATTACKER    = 99u;
        private const float  AMOUNT      = 20f;

        [TearDown]
        public void TearDown()
        {
            LocalPerfectDodgeFeed.ResetStatic();
        }

        private static PerfectDodgeTracker Rolled(out uint serial)
        {
            var tracker = new PerfectDodgeTracker();
            serial = tracker.BeginRoll(ROLL_START, ROLL_END);
            return tracker;
        }

        private static bool Award(PerfectDodgeTracker tracker, double now,
            InvincibilityCause cause = InvincibilityCause.Roll, float amount = AMOUNT, uint attacker = ATTACKER)
        {
            return tracker.TryAward(cause, amount, attacker, now, WINDOW, out _);
        }

        /****************************************
        *               Window
        ****************************************/

        [TestCase(10.0,  true)]  // 무적 시작 순간
        [TestCase(10.10, true)]
        [TestCase(10.15, true)]  // 앞쪽 구간 끝
        [TestCase(10.20, false)] // 구르기 무적 안이지만 앞쪽 구간 밖
        [TestCase(10.45, false)] // 구르기 무적 종료
        [TestCase(9.99,  false)] // 구르기 전
        public void OnlyFrontWindowOfRollInvincibilityCounts(double now, bool expected)
        {
            var tracker = Rolled(out uint serial);

            bool awarded = tracker.TryAward(InvincibilityCause.Roll, AMOUNT, ATTACKER, now, WINDOW, out uint rollSerial);

            Assert.AreEqual(expected, awarded);
            Assert.AreEqual(expected ? serial : 0u, rollSerial);
        }

        [Test]
        public void WindowLongerThanInvincibilityStopsAtInvincibilityEnd()
        {
            var tracker = new PerfectDodgeTracker();
            tracker.BeginRoll(ROLL_START, ROLL_START + 0.1);

            Assert.IsFalse(tracker.TryAward(InvincibilityCause.Roll, AMOUNT, ATTACKER, ROLL_START + 0.12, 1f, out _));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void NonPositiveWindowNeverCounts(float window)
        {
            var tracker = Rolled(out _);

            Assert.IsFalse(tracker.TryAward(InvincibilityCause.Roll, AMOUNT, ATTACKER, ROLL_START, window, out _));
        }

        /****************************************
        *              Exclusions
        ****************************************/

        [TestCase(InvincibilityCause.None)]
        [TestCase(InvincibilityCause.Revive)]
        [TestCase(InvincibilityCause.Parry)]
        [TestCase(InvincibilityCause.Dev)]
        [TestCase(InvincibilityCause.Other)]
        public void NonRollCausesAreExcluded(InvincibilityCause cause)
        {
            var tracker = Rolled(out _);

            Assert.IsFalse(Award(tracker, ROLL_START + 0.05, cause));
            // 제외된 거부는 이 구르기를 소비하지 않는다
            Assert.IsTrue(Award(tracker, ROLL_START + 0.06));
        }

        [Test]
        public void EnvironmentDamageIsExcluded()
        {
            var tracker = Rolled(out _);

            Assert.IsFalse(Award(tracker, ROLL_START + 0.05, attacker: DamageInfo.NO_ATTACKER));
        }

        [TestCase(0f)]
        [TestCase(0.0005f)] // DAMAGE_EPSILON 이하
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ZeroOrInvalidDamageIsExcluded(float amount)
        {
            var tracker = Rolled(out _);

            Assert.IsFalse(Award(tracker, ROLL_START + 0.05, amount: amount));
        }

        [Test]
        public void NoRecordedRollNeverCounts()
        {
            var tracker = new PerfectDodgeTracker();

            Assert.IsFalse(tracker.IsRollInvincibleAt(ROLL_START));
            Assert.IsFalse(Award(tracker, ROLL_START));
        }

        [Test]
        public void ClearForgetsRoll()
        {
            var tracker = Rolled(out _);
            tracker.Clear();

            Assert.AreEqual(0u, tracker.CurrentRollSerial);
            Assert.IsFalse(Award(tracker, ROLL_START + 0.05));
        }

        /****************************************
        *             Once Per Roll
        ****************************************/

        [Test]
        public void SameRollCountsOnlyOnce()
        {
            var tracker = Rolled(out _);

            Assert.IsTrue(Award(tracker, ROLL_START + 0.02));
            // 같은 공격의 다른 콜라이더, 다른 적의 연타
            Assert.IsFalse(Award(tracker, ROLL_START + 0.02));
            Assert.IsFalse(Award(tracker, ROLL_START + 0.10, attacker: ATTACKER + 1u));
        }

        [Test]
        public void NextRollCanCountAgainWithNewSerial()
        {
            var tracker = Rolled(out uint first);
            Assert.IsTrue(Award(tracker, ROLL_START + 0.02));

            uint second = tracker.BeginRoll(ROLL_START + 1.0, ROLL_END + 1.0);

            Assert.AreNotEqual(first, second);
            Assert.IsTrue(tracker.TryAward(InvincibilityCause.Roll, AMOUNT, ATTACKER, ROLL_START + 1.05, WINDOW,
                out uint awarded));
            Assert.AreEqual(second, awarded);
        }

        [Test]
        public void SerialKeepsIncreasingAfterClear()
        {
            var tracker = Rolled(out uint first);
            tracker.Clear();

            uint second = tracker.BeginRoll(ROLL_START, ROLL_END);

            Assert.Greater(second, first);
        }

        /****************************************
        *            Client Feed
        ****************************************/

        [Test]
        public void FeedPublishesEachRollOnce()
        {
            int received = 0;
            double lastTime = 0d;
            LocalPerfectDodgeFeed.Received += time => { ++received; lastTime = time; };

            Assert.IsTrue(LocalPerfectDodgeFeed.Publish(3u, 12.5));
            Assert.IsFalse(LocalPerfectDodgeFeed.Publish(3u, 12.6));
            Assert.IsFalse(LocalPerfectDodgeFeed.Publish(0u, 12.7));
            Assert.IsTrue(LocalPerfectDodgeFeed.Publish(4u, 13.0));

            Assert.AreEqual(2, received);
            Assert.AreEqual(13.0, lastTime);
        }

        [Test]
        public void FeedClearForgetsLastRollAndNotifies()
        {
            int cleared = 0;
            LocalPerfectDodgeFeed.Cleared += () => ++cleared;

            Assert.IsTrue(LocalPerfectDodgeFeed.Publish(1u, 1.0));
            LocalPerfectDodgeFeed.Clear();

            Assert.AreEqual(1, cleared);
            // 새 연결의 플레이어는 번호가 1부터 다시 시작한다
            Assert.IsTrue(LocalPerfectDodgeFeed.Publish(1u, 2.0));
        }
    }
}
