// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

namespace Geumeum.AI.Tests
{
    public sealed class EnemyKnockbackPushRulesTests
    {
        private const float EPSILON = 0.0001f;

        [Test]
        public void DirectionPointsAwayFromSourceOnHorizontalPlane()
        {
            Vector3 direction = EnemyKnockbackPushRules.ResolveDirection(
                new Vector3(1f, 0f, 1f), Vector3.forward, true, new Vector3(1f, 3f, -2f));

            Assert.AreEqual(0f, direction.y, EPSILON);
            Assert.AreEqual(1f, direction.magnitude, EPSILON);
            Assert.AreEqual(1f, direction.z, EPSILON); // 가해자가 -z쪽이니 +z로 밀린다
        }

        [Test]
        public void SameHorizontalPositionFallsBackToBehind()
        {
            // 위아래로만 떨어진 가해자는 수평 방향을 주지 못한다 - 잡귀 뒤쪽으로 민다
            Vector3 direction = EnemyKnockbackPushRules.ResolveDirection(
                new Vector3(2f, 0f, 2f), Vector3.forward, true, new Vector3(2f, 5f, 2f));

            Assert.AreEqual(Vector3.back.x, direction.x, EPSILON);
            Assert.AreEqual(Vector3.back.z, direction.z, EPSILON);
        }

        [Test]
        public void MissingSourceFallsBackToHorizontalBehind()
        {
            Vector3 tiltedForward = new Vector3(1f, 1f, 0f).normalized;
            Vector3 direction = EnemyKnockbackPushRules.ResolveDirection(
                Vector3.zero, tiltedForward, false, new Vector3(100f, 0f, 0f));

            Assert.AreEqual(-1f, direction.x, EPSILON);
            Assert.AreEqual(0f, direction.y, EPSILON);

            // forward가 수평 성분이 없어도 길이 1의 수평 벡터를 준다
            Vector3 degenerate = EnemyKnockbackPushRules.ResolveDirection(
                Vector3.zero, Vector3.up, false, Vector3.zero);
            Assert.AreEqual(1f, degenerate.magnitude, EPSILON);
            Assert.AreEqual(0f, degenerate.y, EPSILON);
        }

        [Test]
        public void StepsSumToDistanceWithIrregularTicksAndSlowDown()
        {
            const float distance = 2f;
            const float seconds  = 0.25f;
            float[] deltas = { 1f / 60f, 0.02f, 0.033f, 1f / 60f };

            float elapsed   = 0f;
            float total     = 0f;
            float firstStep = -1f;
            float lastStep  = 0f;
            for (int i = 0; elapsed < seconds; ++i)
            {
                float previous = elapsed;
                elapsed        = Mathf.Min(elapsed + deltas[i % deltas.Length], seconds);
                float step     = EnemyKnockbackPushRules.StepDistance(distance, seconds, previous, elapsed);

                Assert.GreaterOrEqual(step, 0f);
                if (firstStep < 0f)
                {
                    firstStep = step;
                }

                lastStep = step;
                total   += step;
            }

            Assert.AreEqual(distance, total, EPSILON);
            Assert.Greater(firstStep, lastStep); // ease-out: 처음이 가장 빠르다

            // 시간을 넘긴 뒤에는 더 밀지 않는다
            Assert.AreEqual(0f, EnemyKnockbackPushRules.StepDistance(distance, seconds, seconds, seconds + 1f), EPSILON);
        }

        [Test]
        public void ProgressIsEaseOutFromZeroToOne()
        {
            Assert.AreEqual(0f, EnemyKnockbackPushRules.EvaluateProgress(0f, 0.25f), EPSILON);
            Assert.AreEqual(0.75f, EnemyKnockbackPushRules.EvaluateProgress(0.125f, 0.25f), EPSILON);
            Assert.AreEqual(1f, EnemyKnockbackPushRules.EvaluateProgress(0.25f, 0.25f), EPSILON);
        }

        [Test]
        public void PushSecondsAreClampedToKnockbackDurationAndInvalidDataDoesNotPush()
        {
            Assert.AreEqual(0.3f, EnemyKnockbackPushRules.ClampPushSeconds(0.5f, 0.3f), EPSILON);
            Assert.AreEqual(0.25f, EnemyKnockbackPushRules.ClampPushSeconds(0.25f, 4.6f), EPSILON);
            Assert.AreEqual(0f, EnemyKnockbackPushRules.ClampPushSeconds(float.NaN, 1f));
            Assert.AreEqual(0f, EnemyKnockbackPushRules.ClampPushSeconds(0.25f, 0f));

            Assert.IsTrue(EnemyKnockbackPushRules.CanPush(2f, 0.25f));
            Assert.IsFalse(EnemyKnockbackPushRules.CanPush(0f, 0.25f));
            Assert.IsFalse(EnemyKnockbackPushRules.CanPush(-1f, 0.25f));
            Assert.IsFalse(EnemyKnockbackPushRules.CanPush(2f, 0f));
            Assert.AreEqual(0f, EnemyKnockbackPushRules.StepDistance(2f, 0f, 0f, 0.1f));
        }

        [Test]
        public void ShortMoveAgainstRequestedStepIsBlocked()
        {
            Assert.IsTrue(EnemyKnockbackPushRules.IsBlocked(0.2f, 0.02f));
            Assert.IsFalse(EnemyKnockbackPushRules.IsBlocked(0.2f, 0.19f));
            Assert.IsFalse(EnemyKnockbackPushRules.IsBlocked(0.005f, 0f)); // 곡선 끝의 작은 걸음은 판정하지 않는다
        }
    }
}
