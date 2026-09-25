// Third party
using NUnit.Framework;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art.Tests
{
    /// <summary> 타이밍 섬광(S4) 예약 규칙: 늦은 창, 닫힌 창, 중복 키, 비대상 근사와 세기, 불가 경고 시각, 보스 타 분류 </summary>
    public sealed class ParryFlashTimingTests
    {
        private const double EPSILON = 1e-6;

        /****************************************
        *        Late / closed windows
        ****************************************/

        [Test]
        public void OnTimeWindowBeginsOnceAtStartAndEndsAtEnd()
        {
            var scheduler = new ParryFlashScheduler(4);
            ulong key = ParryFlashTiming.ExactWindowKey(7u, 1u);

            Assert.AreEqual(ParryFlashUpsertResult.Scheduled,
                scheduler.Upsert(key, 0u, ParryFlashKind.Parryable, true, 10.0, 10.32, 9.0));

            scheduler.Advance(9.99);
            Assert.IsFalse(scheduler[0].Begun, "시작 전에 켜졌다");

            scheduler.Advance(10.0);
            Assert.IsTrue(scheduler[0].JustBegan, "창 시작에 섬광이 시작돼야 한다");
            Assert.IsFalse(scheduler[0].RemainingOnly);

            scheduler.Advance(10.1);
            Assert.IsFalse(scheduler[0].JustBegan, "시작 섬광은 한 번만");

            scheduler.Advance(10.32);
            Assert.AreEqual(0, scheduler.Count, "창 끝에서 빠져야 한다");
        }

        [Test]
        public void LateWindowStillOpenHighlightsRemainingOnlyWithoutStartFlash()
        {
            var scheduler = new ParryFlashScheduler(4);
            ulong key = ParryFlashTiming.ExactWindowKey(7u, 2u);

            Assert.AreEqual(ParryFlashUpsertResult.RemainingOnly,
                scheduler.Upsert(key, 0u, ParryFlashKind.Parryable, true, 10.0, 10.32, 10.2));

            Assert.IsTrue(scheduler[0].Begun);
            Assert.IsTrue(scheduler[0].RemainingOnly);

            scheduler.Advance(10.21);
            Assert.IsFalse(scheduler[0].JustBegan, "지난 시작 섬광을 새로 재생하면 안 된다");

            scheduler.Advance(10.32);
            Assert.AreEqual(0, scheduler.Count);
        }

        [Test]
        public void ClosedWindowIsNotShown()
        {
            var scheduler = new ParryFlashScheduler(4);

            Assert.AreEqual(ParryFlashUpsertResult.IgnoredClosed,
                scheduler.Upsert(1ul, 0u, ParryFlashKind.Parryable, true, 10.0, 10.32, 10.32));
            Assert.AreEqual(0, scheduler.Count);
        }

        [Test]
        public void FrameHitchPastWholeWindowNeverBegins()
        {
            var scheduler = new ParryFlashScheduler(4);
            scheduler.Upsert(1ul, 0u, ParryFlashKind.Parryable, true, 10.0, 10.32, 9.0);

            scheduler.Advance(10.5);

            Assert.AreEqual(0, scheduler.Count, "한 프레임에 시작과 끝을 모두 지난 창은 닫힌 창이다");
        }

        [Test]
        public void InvalidTimesAreRejected()
        {
            var scheduler = new ParryFlashScheduler(4);

            Assert.AreEqual(ParryFlashUpsertResult.IgnoredInvalid,
                scheduler.Upsert(1ul, 0u, ParryFlashKind.Parryable, true, 10.0, 9.0, 0.0));
            Assert.AreEqual(ParryFlashUpsertResult.IgnoredInvalid,
                scheduler.Upsert(2ul, 0u, ParryFlashKind.Parryable, true, double.NaN, 9.0, 0.0));
            Assert.AreEqual(0, scheduler.Count);
        }

        /****************************************
        *        Dedupe: cancel / revision / repeat
        ****************************************/

        [Test]
        public void SameKeyAfterBeginDoesNotReplayStartFlash()
        {
            var scheduler = new ParryFlashScheduler(4);
            ulong key = ParryFlashTiming.CueKey(42u, 0, ParryFlashKind.Unparryable);

            scheduler.Upsert(key, 1u, ParryFlashKind.Unparryable, false, 10.0, 10.35, 9.0);
            scheduler.Advance(10.0);
            Assert.IsTrue(scheduler[0].JustBegan);

            // 다른 대상 cue, 재전송으로 같은 타가 다시 와도 시작 섬광은 한 번뿐이다
            Assert.AreEqual(ParryFlashUpsertResult.IgnoredDuplicate,
                scheduler.Upsert(key, 2u, ParryFlashKind.Unparryable, false, 10.0, 10.35, 10.05));
            Assert.AreEqual(1, scheduler.Count);

            scheduler.Advance(10.1);
            Assert.IsFalse(scheduler[0].JustBegan);
        }

        [Test]
        public void OlderRevisionIsIgnoredAndNewerRevisionMovesPendingTimes()
        {
            var scheduler = new ParryFlashScheduler(4);
            ulong key = ParryFlashTiming.CueKey(42u, 1, ParryFlashKind.Parryable);

            scheduler.Upsert(key, 5u, ParryFlashKind.Parryable, false, 10.0, 10.32, 9.0);

            Assert.AreEqual(ParryFlashUpsertResult.IgnoredStale,
                scheduler.Upsert(key, 4u, ParryFlashKind.Parryable, false, 11.0, 11.32, 9.0));
            Assert.That(scheduler[0].Start, Is.EqualTo(10.0).Within(EPSILON));

            // 엇박 지연 - 시작 전이면 새 시각을 따른다
            Assert.AreEqual(ParryFlashUpsertResult.Updated,
                scheduler.Upsert(key, 6u, ParryFlashKind.Parryable, false, 10.2, 10.52, 9.0));
            Assert.That(scheduler[0].Start, Is.EqualTo(10.2).Within(EPSILON));
            Assert.That(scheduler[0].End, Is.EqualTo(10.52).Within(EPSILON));
        }

        [Test]
        public void CancelledBeforeStartNeverFlashes()
        {
            var scheduler = new ParryFlashScheduler(4);
            ulong key = ParryFlashTiming.ExactWindowKey(7u, 3u);

            scheduler.Upsert(key, 0u, ParryFlashKind.Parryable, true, 10.0, 10.32, 9.0);
            Assert.IsTrue(scheduler.Remove(key));

            scheduler.Advance(10.0);
            Assert.AreEqual(0, scheduler.Count);
            Assert.IsFalse(scheduler.Remove(key));
        }

        [Test]
        public void RepeatsOfOneAttackAndKindsHaveDistinctKeys()
        {
            ulong first     = ParryFlashTiming.CueKey(42u, 0, ParryFlashKind.Parryable);
            ulong second    = ParryFlashTiming.CueKey(42u, 1, ParryFlashKind.Parryable);
            ulong firstRed  = ParryFlashTiming.CueKey(42u, 0, ParryFlashKind.Unparryable);
            ulong otherHit  = ParryFlashTiming.CueKey(43u, 0, ParryFlashKind.Parryable);
            ulong exact     = ParryFlashTiming.ExactWindowKey(42u, 0u);

            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first, firstRed);
            Assert.AreNotEqual(first, otherHit);
            Assert.AreNotEqual(first, exact);

            Assert.IsTrue(ParryFlashTiming.IsExactWindowKey(exact));
            Assert.IsFalse(ParryFlashTiming.IsExactWindowKey(first));
            Assert.IsFalse(ParryFlashTiming.IsExactWindowKey(firstRed));
        }

        [Test]
        public void TargetEntryDominatesNonTarget()
        {
            var scheduler = new ParryFlashScheduler(4);
            scheduler.Upsert(1ul, 0u, ParryFlashKind.Unparryable, false, 10.0, 10.5, 9.0);
            scheduler.Upsert(2ul, 0u, ParryFlashKind.Parryable, true, 9.9, 10.3, 9.0);
            scheduler.Advance(10.05);

            int dominant = scheduler.FindDominantBegun();

            Assert.GreaterOrEqual(dominant, 0);
            Assert.AreEqual(2ul, scheduler[dominant].Key);
        }

        /****************************************
        *        Time calculation
        ****************************************/

        [Test]
        public void NonTargetApproximationUsesServerFormulaWithBaseWindow()
        {
            ParryFlashTiming.ApproximateWindow(10.0, 0.32, 0.18, out double start, out double end);

            Assert.That(end, Is.EqualTo(9.82).Within(EPSILON));
            Assert.That(start, Is.EqualTo(9.50).Within(EPSILON));
        }

        [Test]
        public void NonTargetApproximationUsesProjectResolutionLead()
        {
            double impact = 50.0;
            ParryFlashTiming.ApproximateWindow(impact, 0.32, Constants.Network.PARRY_RESOLUTION_LEAD,
                out double start, out double end);

            Assert.That(impact - end, Is.EqualTo(Constants.Network.PARRY_RESOLUTION_LEAD).Within(EPSILON));
            Assert.That(end - start, Is.EqualTo(0.32).Within(EPSILON));
        }

        [Test]
        public void UnparryableWarningIsScheduledBeforeImpact()
        {
            Assert.That(ParryFlashTiming.UnparryableWarningStart(10.0, 0.35), Is.EqualTo(9.65).Within(EPSILON));
            Assert.That(ParryFlashTiming.UnparryableWarningStart(10.0, -1.0), Is.EqualTo(10.0).Within(EPSILON),
                "음수 선행시간은 0으로 본다");
        }

        [Test]
        public void ExactWindowMatchesItsOwnCueImpactOnly()
        {
            // 서버와 같은 식: impact = 공격 시작(double) + 오프셋(float), windowEnd = impact - PARRY_RESOLUTION_LEAD
            double attackStart = 1234.5678;
            double impact      = attackStart + 1.2f;
            double windowEnd   = impact - Constants.Network.PARRY_RESOLUTION_LEAD;
            double nextImpact  = attackStart + 1.5f;

            Assert.IsTrue(ParryFlashTiming.MatchesImpact(windowEnd, Constants.Network.PARRY_RESOLUTION_LEAD, impact));
            Assert.IsFalse(ParryFlashTiming.MatchesImpact(windowEnd, Constants.Network.PARRY_RESOLUTION_LEAD, nextImpact));
        }

        /****************************************
        *        Shapes
        ****************************************/

        [Test]
        public void ParryableIsOneBlinkAndUnparryableIsTwoSpreads()
        {
            Assert.That(ParryFlashTiming.SingleBlink(0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.Greater(ParryFlashTiming.SingleBlink(0.2f), ParryFlashTiming.SingleBlink(0.6f));
            Assert.AreEqual(0f, ParryFlashTiming.SingleBlink(1f));

            Assert.That(ParryFlashTiming.DoubleSpread(0f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ParryFlashTiming.DoubleSpread(0.25f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ParryFlashTiming.DoubleSpread(0.5f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ParryFlashTiming.DoubleSpread(0.75f), Is.EqualTo(1f).Within(1e-5f));
            Assert.AreEqual(0f, ParryFlashTiming.DoubleSpread(1f));

            Assert.That(ParryFlashTiming.ShapeSeconds(ParryFlashKind.Unparryable, 0.12f), Is.EqualTo(0.24f).Within(1e-5f));
            Assert.That(ParryFlashTiming.ShapeSeconds(ParryFlashKind.Parryable, 0.12f), Is.EqualTo(0.12f).Within(1e-5f));
        }

        /****************************************
        *        Non-target strength
        ****************************************/

        [Test]
        public void NonTargetFlashIsScaledAndTargetIsUnchanged()
        {
            Assert.That(ParryFlashTiming.AudienceWeight(0.8f, true, 0.5f), Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(ParryFlashTiming.AudienceWeight(0.8f, false, 0.5f), Is.EqualTo(0.4f).Within(1e-5f),
                "비대상은 설정 배수(절반)만큼 약해야 한다");
            Assert.That(ParryFlashTiming.AudienceWeight(0.8f, false, 2f), Is.EqualTo(0.8f).Within(1e-5f),
                "배수는 1을 넘지 않는다. 비대상이 대상보다 강해지면 안 된다");
            Assert.That(ParryFlashTiming.AudienceWeight(0.8f, false, -1f), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void NonTargetColorIsDimmedKeepingAlpha()
        {
            var source = new Color(0.2f, 0.6f, 1f, 0.9f);

            Color target    = ParryFlashTint.ForAudience(source, true, 0.5f);
            Color nonTarget = ParryFlashTint.ForAudience(source, false, 0.5f);

            Assert.AreEqual(source, target);
            Assert.That(nonTarget.r, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(nonTarget.g, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(nonTarget.b, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(nonTarget.a, Is.EqualTo(0.9f).Within(1e-5f), "알파는 세기와 따로다");
        }

        /****************************************
        *        Boss hit classification
        ****************************************/

        [Test]
        public void DeflectableRepeatIsParryableNotRedWarning()
        {
            // 서버는 튕겨내기 반복에도 패링 창을 연다. 비대상에게 붉은 불가 경고가 나오면 안 된다
            BossPatternSO.PatternEntry combo = CreateCombo(deflectable: new[] { 0, 1 });

            Assert.AreEqual(ParryFlashKind.Parryable, BossParryFlashClassifier.Classify(combo, 0, false));
            Assert.AreEqual(ParryFlashKind.Parryable, BossParryFlashClassifier.Classify(combo, 1, false));
            Assert.AreEqual(ParryFlashKind.Parryable, BossParryFlashClassifier.Classify(combo, 2, false));
        }

        [Test]
        public void RepeatWithoutParryDeflectOrTransferIsUnparryable()
        {
            BossPatternSO.PatternEntry combo = CreateCombo(deflectable: null);

            Assert.AreEqual(ParryFlashKind.Unparryable, BossParryFlashClassifier.Classify(combo, 0, false));
            Assert.AreEqual(ParryFlashKind.Parryable, BossParryFlashClassifier.Classify(combo, 2, false));

            var unparryable = new BossPatternSO.PatternEntry
            {
                PatternId            = 7,
                IsParryable          = false,
                ParryableRepeatIndex = -1,
                ImpactOffsets        = new[] { 1f },
            };
            Assert.AreEqual(ParryFlashKind.Unparryable, BossParryFlashClassifier.Classify(unparryable, 0, false));
            Assert.AreEqual(ParryFlashKind.Parryable, BossParryFlashClassifier.Classify(unparryable, 0, true),
                "이양 반복은 원래 불가 패턴이어도 창이 열린다");
        }

        [Test]
        public void TransferredRepeatFollowsBrokenTailAndChasaEntryOnly()
        {
            // 기본값: 꼬리1 파괴 -> 차사 기회가 패턴 7 첫 타로, 꼬리2 파괴 -> 무녀 기회가 패턴 9로 옮겨진다
            BossPatternSO data = ScriptableObject.CreateInstance<BossPatternSO>();
            try
            {
                var none  = new BossPartSnapshot { BrokenMask = 0 };
                var tail1 = new BossPartSnapshot { BrokenMask = 0b001 };
                var tail2 = new BossPartSnapshot { BrokenMask = 0b010 };

                Assert.IsFalse(BossParryFlashClassifier.IsTransferredRepeat(data, none, 7, 0));
                Assert.IsTrue(BossParryFlashClassifier.IsTransferredRepeat(data, tail1, 7, 0));
                Assert.IsFalse(BossParryFlashClassifier.IsTransferredRepeat(data, tail1, 7, 1),
                    "이양 항목의 반복 번호만 창이 열린다");
                Assert.IsFalse(BossParryFlashClassifier.IsTransferredRepeat(data, tail2, 9, 0),
                    "서버는 차사 이양 항목으로만 창을 연다");
                Assert.IsTrue(BossParryFlashClassifier.IsTransferredRepeat(data,
                    BossParryFlashClassifier.UnknownPartsSnapshot(), 7, 0),
                    "꼬리 상태를 모르면 가능 쪽으로 본다");
                Assert.IsFalse(BossParryFlashClassifier.IsTransferredRepeat(null, tail1, 7, 0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        private static BossPatternSO.PatternEntry CreateCombo(int[] deflectable)
        {
            // 여우불 봉 3연격 모양: 마지막 타만 패링, 앞 타는 튕겨내기
            return new BossPatternSO.PatternEntry
            {
                PatternId                = 1,
                IsParryable              = true,
                ParryableRepeatIndex     = 2,
                DeflectableRepeatIndices = deflectable,
                ImpactOffsets            = new[] { 1f, 1.5f, 2f },
            };
        }
    }
}
