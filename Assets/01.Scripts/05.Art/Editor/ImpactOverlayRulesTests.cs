// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    /// <summary> 화면 가장자리 오버레이의 순수 규칙 - 받아베기 먹 번짐(COMBAT_STYLE_DESIGN S6) </summary>
    public sealed class ImpactOverlayRulesTests
    {
        private const float TOLERANCE = 0.0001f;

        /****************************************
        *            Ink Envelope
        ****************************************/

        [Test]
        public void InkSwellsThenRecedes()
        {
            Assert.AreEqual(0f, ImpactRules.InkWeight(0f, 0.25f), TOLERANCE);
            Assert.AreEqual(1f, ImpactRules.InkWeight(0.125f, 0.25f), TOLERANCE);
            Assert.Greater(ImpactRules.InkWeight(0.05f, 0.25f), 0f);
            Assert.Greater(ImpactRules.InkWeight(0.2f, 0.25f), 0f);
            Assert.AreEqual(0f, ImpactRules.InkWeight(0.25f, 0.25f), TOLERANCE);
        }

        [TestCase(-0.01f, 0.25f)]
        [TestCase(0.1f, 0f)]
        [TestCase(0.1f, -1f)]
        public void InkIsZeroOutsideRangeOrWithoutDuration(float elapsed, float duration)
        {
            Assert.AreEqual(0f, ImpactRules.InkWeight(elapsed, duration), TOLERANCE);
        }

        /****************************************
        *          Counter Ink Trigger
        ****************************************/

        [TestCase((byte)3, true)]
        [TestCase((byte)4, true)]
        [TestCase((byte)2, false)]
        [TestCase((byte)1, false)]
        [TestCase((byte)0, false)]
        public void OnlyThreeMarkCounterPlaysInk(byte marks, bool expected)
        {
            Assert.AreEqual(expected,
                ImpactRules.ShouldPlayCounterInk(DamageSourceKind.CounterSlash, marks, 7u, 11u, 0u, 0u));
        }

        [TestCase(DamageSourceKind.SwordSkill)]
        [TestCase(DamageSourceKind.Melee)]
        [TestCase(DamageSourceKind.PrecisionShot)]
        public void OtherSourcesNeverPlayInk(DamageSourceKind source)
        {
            Assert.IsFalse(ImpactRules.ShouldPlayCounterInk(source, 3, 7u, 11u, 0u, 0u));
        }

        [Test]
        public void SameExecutionPlaysOnce()
        {
            // 같은 받아베기가 두 번째 적을 맞힌 결과
            Assert.IsFalse(ImpactRules.ShouldPlayCounterInk(DamageSourceKind.CounterSlash, 3, 7u, 11u, 7u, 11u));

            // 다음 받아베기, 다른 공격자의 같은 번호
            Assert.IsTrue(ImpactRules.ShouldPlayCounterInk(DamageSourceKind.CounterSlash, 3, 7u, 12u, 7u, 11u));
            Assert.IsTrue(ImpactRules.ShouldPlayCounterInk(DamageSourceKind.CounterSlash, 3, 8u, 11u, 7u, 11u));
        }

        [Test]
        public void UnknownExecutionIsNotMergedByExecution()
        {
            Assert.IsTrue(ImpactRules.ShouldPlayCounterInk(DamageSourceKind.CounterSlash, 3, 7u, 0u, 7u, 0u));
        }
    }
}
