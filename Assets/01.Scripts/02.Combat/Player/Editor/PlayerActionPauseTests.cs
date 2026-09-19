// System
using System.Reflection;

// Third party
using NUnit.Framework;

namespace Geumeum.Combat.Tests
{
    /// <summary> 런 일시정지가 공격과 치유 deadline을 소진하지 않는지 검증한다 </summary>
    public sealed class PlayerActionPauseTests
    {
        private const BindingFlags PRIVATE_STATIC =
            BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void AttackInProgressStopsMovementUntilItEnds()
        {
            // 공격 중에는 서 있고, 후딜까지 끝나면 같은 조건에서 다시 움직인다
            Assert.IsFalse(PlayerActionGate.CanMove(true, true));
            Assert.IsTrue(PlayerActionGate.CanMove(true, false));

            // 행동과 무관하게 막힌 상태(사망, 일시정지, 로비)는 그대로 막힌다
            Assert.IsFalse(PlayerActionGate.CanMove(false, false));
            Assert.IsFalse(PlayerActionGate.CanMove(false, true));
        }

        [Test]
        public void BurstDeadlineAfterPauseStartMovesPastResumeTick()
        {
            const double pauseStart = 10.0;
            const double resumeTime = 20.0;
            const double burstDeadline = 10.12;

            double shifted = Shift(typeof(PlayerAttack), burstDeadline,
                resumeTime, resumeTime - pauseStart);

            Assert.AreEqual(20.12, shifted, 0.0001);
            Assert.Greater(shifted, resumeTime);
        }

        [Test]
        public void DeadlineAlreadyExpiredBeforePauseIsNotExtended()
        {
            const double deadline = 9.9;
            const double resumeTime = 20.0;
            const double pausedSeconds = 10.0;

            Assert.AreEqual(deadline,
                Shift(typeof(PlayerAttack), deadline, resumeTime, pausedSeconds),
                0.0001);
            Assert.AreEqual(deadline,
                Shift(typeof(HealController), deadline, resumeTime, pausedSeconds),
                0.0001);
        }

        [Test]
        public void HealCastAndCooldownRetainTheirRemainingDurations()
        {
            const double resumeTime = 30.0;
            const double pausedSeconds = 8.0;

            double castDeadline = Shift(typeof(HealController), 23.5,
                resumeTime, pausedSeconds);
            double cooldownDeadline = Shift(typeof(HealController), 27.0,
                resumeTime, pausedSeconds);

            Assert.AreEqual(31.5, castDeadline, 0.0001);
            Assert.AreEqual(35.0, cooldownDeadline, 0.0001);
        }

        private static double Shift(System.Type type, double deadline,
            double resumeTime, double pausedSeconds)
        {
            return (double)type.GetMethod("ShiftPausedDeadline", PRIVATE_STATIC)
                .Invoke(null, new object[] { deadline, resumeTime, pausedSeconds });
        }
    }
}
