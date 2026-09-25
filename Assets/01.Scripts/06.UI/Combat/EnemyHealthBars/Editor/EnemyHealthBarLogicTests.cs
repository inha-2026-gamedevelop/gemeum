// System
using System;

// Third party
using NUnit.Framework;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 잡귀 머리 위 체력 바의 순수 규칙 - 잔상 추적, 비율·분모, 불투명도·거리 배율, 칸 상한 교체
    /// 화면 배치·투영·세 화면 일치는 Play 모드(L2·L3)에서 본다
    /// </summary>
    public sealed class EnemyHealthBarLogicTests
    {
        private const float HOLD      = 0.4f;
        private const float DRAIN     = 0.5f;
        private const float TOLERANCE = 0.0001f;

        private const int TARGET_A = 1;
        private const int TARGET_B = 2;
        private const int TARGET_C = 3;
        private const int TARGET_D = 4;

        /****************************************
        *            잔상 추적
        ****************************************/

        [Test]
        public void HitDropsHealthAtOnceAndLeavesChip()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);

            trail.SetRatio(0.6f, 10f, HOLD);

            Assert.AreEqual(0.6f, trail.Health, TOLERANCE);
            Assert.AreEqual(1f, trail.Chip, TOLERANCE);
            Assert.IsTrue(trail.HasChip);
        }

        [Test]
        public void ChipHoldsDuringDelayThenDrainsPerSecond()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.6f, 10f, HOLD);

            // 지연 안에서는 멈춰 있다
            trail.Tick(10.3f, 0.3f, DRAIN);
            Assert.AreEqual(1f, trail.Chip, TOLERANCE);

            // 지연이 끝나면 초당 DRAIN 비율로 내려간다
            trail.Tick(10.6f, 0.2f, DRAIN);
            Assert.AreEqual(0.9f, trail.Chip, TOLERANCE);
        }

        [Test]
        public void ChipNeverFallsBelowHealth()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.6f, 10f, HOLD);

            trail.Tick(20f, 5f, DRAIN);

            Assert.AreEqual(0.6f, trail.Chip, TOLERANCE);
            Assert.IsFalse(trail.HasChip);
        }

        [Test]
        public void NewHitRestartsDelay()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.8f, 10f, HOLD);
            trail.SetRatio(0.5f, 10.3f, HOLD);

            // 첫 피격 기준 지연(10.4)은 지났지만 두 번째 피격 기준(10.7) 전이라 멈춰 있다
            trail.Tick(10.5f, 0.2f, DRAIN);

            Assert.AreEqual(1f, trail.Chip, TOLERANCE);
            Assert.AreEqual(0.5f, trail.Health, TOLERANCE);
        }

        [Test]
        public void ZeroDrainSnapsAfterDelay()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.3f, 10f, HOLD);

            trail.Tick(10.5f, 0.016f, 0f);

            Assert.AreEqual(0.3f, trail.Chip, TOLERANCE);
        }

        [Test]
        public void RecoveryAboveChipSnapsBothBars()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.4f, 10f, HOLD);
            trail.Tick(11f, 1f, DRAIN);

            trail.SetRatio(0.9f, 11.1f, HOLD);

            Assert.AreEqual(0.9f, trail.Health, TOLERANCE);
            Assert.AreEqual(0.9f, trail.Chip, TOLERANCE);
            Assert.IsFalse(trail.HasChip);
        }

        [Test]
        public void RecoveryBelowChipRaisesHealthAtOnceAndKeepsChip()
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(1f);
            trail.SetRatio(0.4f, 10f, HOLD);

            trail.SetRatio(0.6f, 10.1f, HOLD);

            Assert.AreEqual(0.6f, trail.Health, TOLERANCE);
            Assert.AreEqual(1f, trail.Chip, TOLERANCE);
        }

        [TestCase(1.5f, 1f)]
        [TestCase(-0.2f, 0f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        public void RatioIsClamped(float ratio, float expected)
        {
            EnemyHealthBarTrail trail = EnemyHealthBarTrail.Create(ratio);

            Assert.AreEqual(expected, trail.Health, TOLERANCE);
            Assert.AreEqual(expected, trail.Chip, TOLERANCE);
            Assert.AreEqual(expected, EnemyHealthBarRules.ClampRatio(ratio), TOLERANCE);
        }

        /****************************************
        *            비율·분모
        ****************************************/

        [TestCase(50f, 100f, 0.5f)]
        [TestCase(150f, 100f, 1f)]
        [TestCase(10f, 0f, 0f)]
        [TestCase(10f, -5f, 0f)]
        [TestCase(10f, float.NaN, 0f)]
        public void RatioHandlesInvalidMax(float health, float maxHealth, float expected)
        {
            Assert.AreEqual(expected, EnemyHealthBarRules.Ratio(health, maxHealth), TOLERANCE);
        }

        [Test]
        public void ReferenceMaxPrefersSyncedValue()
        {
            Assert.AreEqual(300f, EnemyHealthBarRules.ReferenceMax(300f, 200f), TOLERANCE);
        }

        [Test]
        public void ReferenceMaxFallsBackToObservedPeak()
        {
            Assert.AreEqual(200f, EnemyHealthBarRules.ReferenceMax(0f, 200f), TOLERANCE);
            Assert.AreEqual(0f, EnemyHealthBarRules.ReferenceMax(0f, 0f), TOLERANCE);
            Assert.AreEqual(0f, EnemyHealthBarRules.ReferenceMax(float.NaN, float.NaN), TOLERANCE);
        }

        /****************************************
        *            불투명도·거리 배율
        ****************************************/

        [TestCase(12f, 1f)]    // 표시 시간 안
        [TestCase(13f, 1f)]    // 표시 시간 끝
        [TestCase(13.2f, 0.5f)]
        [TestCase(13.4f, 0f)]
        [TestCase(20f, 0f)]
        public void AlphaHoldsThenFades(float now, float expected)
        {
            Assert.AreEqual(expected, EnemyHealthBarRules.Alpha(now, 10f, 3f, 0.4f), TOLERANCE);
        }

        [Test]
        public void AlphaWithoutFadeEndsAtOnce()
        {
            Assert.AreEqual(1f, EnemyHealthBarRules.Alpha(13f, 10f, 3f, 0f), TOLERANCE);
            Assert.AreEqual(0f, EnemyHealthBarRules.Alpha(13.01f, 10f, 3f, 0f), TOLERANCE);
        }

        [TestCase(4f, 8f, 0.6f, 1f)]
        [TestCase(16f, 8f, 0.6f, 0.6f)]
        [TestCase(10f, 8f, 0.6f, 0.8f)]
        [TestCase(40f, 0f, 0.6f, 1f)]
        public void DistanceScaleShrinksWithFloor(float depth, float reference, float minScale, float expected)
        {
            Assert.AreEqual(expected, EnemyHealthBarRules.DistanceScale(depth, reference, minScale), TOLERANCE);
        }

        /****************************************
        *            칸 상한 교체
        ****************************************/

        [Test]
        public void BookSpawnsThenReusesSameTarget()
        {
            EnemyHealthBarBook book = new EnemyHealthBarBook(2);

            Assert.AreEqual(EnemyHealthBarAction.Spawned, book.Acquire(TARGET_A, out int first));
            Assert.AreEqual(EnemyHealthBarAction.Reused, book.Acquire(TARGET_A, out int again));
            Assert.AreEqual(first, again);
            Assert.AreEqual(1, book.ActiveCount);
        }

        [Test]
        public void BookFullReplacesLeastRecentlyHit()
        {
            EnemyHealthBarBook book = new EnemyHealthBarBook(2);
            book.Acquire(TARGET_A, out int slotA);
            book.Acquire(TARGET_B, out int slotB);

            EnemyHealthBarAction action = book.Acquire(TARGET_C, out int slotC);

            Assert.AreEqual(EnemyHealthBarAction.Replaced, action);
            Assert.AreEqual(slotA, slotC);
            Assert.AreEqual(TARGET_C, book.Get(slotC).TargetId);
            Assert.AreEqual(-1, book.Find(TARGET_A));
            Assert.AreEqual(slotB, book.Find(TARGET_B));
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void BookTouchKeepsRecentlyHitBar()
        {
            EnemyHealthBarBook book = new EnemyHealthBarBook(2);
            book.Acquire(TARGET_A, out int slotA);
            book.Acquire(TARGET_B, out int slotB);

            // A가 다시 맞았으므로 가장 오래된 것은 B다
            book.Touch(slotA);
            book.Acquire(TARGET_C, out int slotC);

            Assert.AreEqual(slotB, slotC);
            Assert.AreEqual(slotA, book.Find(TARGET_A));
        }

        [Test]
        public void BookReleaseFreesSlotForNewTarget()
        {
            EnemyHealthBarBook book = new EnemyHealthBarBook(2);
            book.Acquire(TARGET_A, out int slotA);
            book.Acquire(TARGET_B, out _);

            book.Release(slotA);
            book.Release(slotA);
            Assert.AreEqual(1, book.ActiveCount);

            Assert.AreEqual(EnemyHealthBarAction.Spawned, book.Acquire(TARGET_D, out int slotD));
            Assert.AreEqual(slotA, slotD);
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void BookClearEmptiesAll()
        {
            EnemyHealthBarBook book = new EnemyHealthBarBook(3);
            book.Acquire(TARGET_A, out _);
            book.Acquire(TARGET_B, out _);

            book.Clear();

            Assert.AreEqual(0, book.ActiveCount);
            Assert.AreEqual(-1, book.Find(TARGET_A));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void BookRejectsNonPositiveCapacity(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyHealthBarBook(capacity));
        }
    }
}
