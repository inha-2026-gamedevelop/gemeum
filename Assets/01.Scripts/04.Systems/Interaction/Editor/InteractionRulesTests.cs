// 서드파티
using NUnit.Framework;

namespace Geumeum.Systems.Tests
{
    public class InteractionRulesTests
    {
        /****************************************
        *               Constants
        ****************************************/

        private const float  MAX_DISTANCE  = 2f;
        private const float  OUT_OF_COMBAT = 5f;   // 게임 디자인 6.5
        private const double NEVER_FOUGHT  = -1d;
        private const double NOW           = 100d;

        /****************************************
        *                Helpers
        ****************************************/

        private static InteractionRequest Make(float distanceSquared = 1f, bool alive = true,
            bool runActive = true, double lastCombat = NEVER_FOUGHT, double now = NOW,
            float maxDistance = MAX_DISTANCE, float outOfCombat = OUT_OF_COMBAT)
        {
            return new InteractionRequest(distanceSquared, maxDistance, alive, runActive,
                now, lastCombat, outOfCombat);
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void CleanRequestSucceeds()
        {
            Assert.AreEqual(InteractionResult.Success, InteractionRules.Evaluate(Make()));
        }

        [Test]
        public void DistanceUsesSquaredComparisonAndAllowsBoundary()
        {
            // 경계값(정확히 최대 거리)은 허용한다
            Assert.AreEqual(InteractionResult.Success,
                InteractionRules.Evaluate(Make(distanceSquared: MAX_DISTANCE * MAX_DISTANCE)));

            Assert.AreEqual(InteractionResult.TooFar,
                InteractionRules.Evaluate(Make(distanceSquared: (MAX_DISTANCE * MAX_DISTANCE) + 0.01f)));
        }

        [Test]
        public void DeadRequesterIsRejected()
        {
            Assert.AreEqual(InteractionResult.Dead, InteractionRules.Evaluate(Make(alive: false)));
        }

        [Test]
        public void InactiveRunIsRejectedBeforeEverythingElse()
        {
            // 런이 끝난 뒤 늦게 도착한 요청은 거리나 생명과 무관하게 거부한다
            InteractionRequest request = Make(runActive: false, alive: false, distanceSquared: 999f);
            Assert.AreEqual(InteractionResult.RunNotActive, InteractionRules.Evaluate(request));
        }

        [Test]
        public void CombatLockBlocksUntilGraceElapses()
        {
            // 전투 4초 뒤 - 아직 잠겨 있다
            Assert.AreEqual(InteractionResult.InCombat,
                InteractionRules.Evaluate(Make(lastCombat: NOW - 4d)));

            // 정확히 5초 뒤 - 열린다
            Assert.AreEqual(InteractionResult.Success,
                InteractionRules.Evaluate(Make(lastCombat: NOW - OUT_OF_COMBAT)));
        }

        [Test]
        public void NeverFoughtIsNotInCombat()
        {
            Assert.IsFalse(InteractionRules.IsInCombat(Make(lastCombat: NEVER_FOUGHT)));
            Assert.AreEqual(InteractionResult.Success, InteractionRules.Evaluate(Make(lastCombat: NEVER_FOUGHT)));
        }

        [Test]
        public void ReviveSkipsCombatLockButKeepsOtherChecks()
        {
            InteractionRequest inCombat = Make(lastCombat: NOW - 1d);

            // 협동 부활은 전투 중에도 가능하다 - 게임 디자인 6.5의 예외
            Assert.AreEqual(InteractionResult.Success,
                InteractionRules.Evaluate(inCombat, requireOutOfCombat: false));

            // 예외라도 거리는 본다
            InteractionRequest far = Make(distanceSquared: 100f, lastCombat: NOW - 1d);
            Assert.AreEqual(InteractionResult.TooFar,
                InteractionRules.Evaluate(far, requireOutOfCombat: false));
        }

        [Test]
        public void RemainingLockCountsDownAndClampsToZero()
        {
            Assert.AreEqual(3d, InteractionRules.RemainingCombatLockSeconds(Make(lastCombat: NOW - 2d)), 0.0001d);
            Assert.AreEqual(0d, InteractionRules.RemainingCombatLockSeconds(Make(lastCombat: NOW - 9d)), 0.0001d);
            Assert.AreEqual(0d, InteractionRules.RemainingCombatLockSeconds(Make(lastCombat: NEVER_FOUGHT)), 0.0001d);
        }

        [Test]
        public void ZeroGraceOpensImmediately()
        {
            InteractionRequest request = Make(lastCombat: NOW, outOfCombat: 0f);
            Assert.IsFalse(InteractionRules.IsInCombat(request));
            Assert.AreEqual(InteractionResult.Success, InteractionRules.Evaluate(request));
        }

        [TestCase(float.NaN, MAX_DISTANCE)]
        [TestCase(-1f, MAX_DISTANCE)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -2f)]
        [TestCase(1f, float.PositiveInfinity)]
        public void MalformedRequestIsRejected(float distanceSquared, float maxDistance)
        {
            Assert.AreEqual(InteractionResult.InvalidRequest,
                InteractionRules.Evaluate(Make(distanceSquared: distanceSquared, maxDistance: maxDistance)));
        }

        [Test]
        public void NonFiniteTimeIsRejected()
        {
            Assert.AreEqual(InteractionResult.InvalidRequest,
                InteractionRules.Evaluate(Make(now: double.NaN)));
            Assert.AreEqual(InteractionResult.InvalidRequest,
                InteractionRules.Evaluate(Make(lastCombat: double.PositiveInfinity)));
        }
    }
}
