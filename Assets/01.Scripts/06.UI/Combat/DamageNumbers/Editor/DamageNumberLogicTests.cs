// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 데미지 숫자(COMBAT_STYLE_DESIGN S10)의 순수 규칙 - 숫자 형식, 합산 키·창, 상한 교체, 크기 선택
    /// 화면 배치·글꼴·투영은 Play 모드 L2에서 본다
    /// </summary>
    public sealed class DamageNumberLogicTests
    {
        private const uint  ATTACKER   = 7;
        private const uint  TARGET_A   = 100;
        private const uint  TARGET_B   = 200;
        private const float WINDOW     = 0.08f;
        private const float CRIT_SCALE = 1.2f;
        private const float TIER_MAX   = 1.2f;
        private const uint  TARGET_C   = 300;
        private const float REUSE      = 0.2f;

        /****************************************
        *            숫자 형식
        ****************************************/

        [TestCase(1f,         "1")]
        [TestCase(1.49f,      "1")]
        [TestCase(1.5f,       "2")]
        [TestCase(2.5f,       "3")]
        [TestCase(999.4f,     "999")]
        [TestCase(999.5f,     "1,000")]
        [TestCase(1234f,      "1,234")]
        [TestCase(1234567f,   "1,234,567")]
        [TestCase(12345678f,  "12,345,678")]
        public void FormatRoundsHalfUpWithThousandsSeparator(float amount, string expected)
        {
            Assert.AreEqual(expected, DamageNumberFormatter.Format(amount));
        }

        [TestCase(0.01f)]
        [TestCase(0.5f)]
        [TestCase(0.999f)]
        public void FormatBelowOneShowsLessThanOne(float amount)
        {
            Assert.AreEqual("<1", DamageNumberFormatter.Format(amount));
        }

        [TestCase(0f)]
        [TestCase(-3f)]
        [TestCase(float.NaN)]
        public void FormatSkipsNonPositive(float amount)
        {
            Assert.IsFalse(DamageNumberFormatter.IsDisplayable(amount));
            Assert.AreEqual(string.Empty, DamageNumberFormatter.Format(amount));
        }

        [Test]
        public void FormatWrapsCriticalMark()
        {
            Assert.AreEqual("*1,500!", DamageNumberFormatter.Format(1500f, "*", "!"));
            Assert.AreEqual("*<1", DamageNumberFormatter.Format(0.3f, "*", null));
        }

        [Test]
        public void FormatWritesIntoFixedBuffer()
        {
            char[] buffer = new char[DamageNumberFormatter.BUFFER_LENGTH];
            int length    = DamageNumberFormatter.Write(20480.4f, null, null, buffer);

            Assert.AreEqual("20,480", new string(buffer, 0, length));
        }

        /****************************************
        *            합산 키·창
        ****************************************/

        [Test]
        public void KeySeparatesPartSourceAndCritical()
        {
            DamageNumberKey baseKey = DamageNumberKey.From(Hit(TARGET_A, 1, 10f));

            Assert.AreEqual(baseKey, DamageNumberKey.From(Hit(TARGET_A, 1, 99f)));
            Assert.AreNotEqual(baseKey, DamageNumberKey.From(Hit(TARGET_A, 1, 10f, part: 0)));
            Assert.AreNotEqual(baseKey, DamageNumberKey.From(Hit(TARGET_A, 1, 10f, source: DamageSourceKind.Arrow)));
            Assert.AreNotEqual(baseKey, DamageNumberKey.From(Hit(TARGET_A, 1, 10f, isCritical: true)));
            Assert.AreNotEqual(baseKey, DamageNumberKey.From(Hit(TARGET_A, 2, 10f)));
            Assert.IsFalse(DamageNumberKey.From(Hit(TARGET_A, 0, 10f)).CanMerge);
        }

        [Test]
        public void SameKeyInsideWindowMergesWithRawAmount()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Assert.AreEqual(DamageNumberAction.Spawned, Add(book, Hit(TARGET_A, 1, 0.6f), 0f, rules, out int first));
            Assert.AreEqual(DamageNumberAction.Merged,  Add(book, Hit(TARGET_A, 1, 0.6f), 0.05f, rules, out int second));

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, book.ActiveCount);
            Assert.AreEqual(1.2f, book.Get(first).Amount, 0.0001f);

            // 반올림 전 값으로 더했으므로 1.2 -> "1". 따로 반올림해 더했다면 2가 된다
            Assert.AreEqual("1", DamageNumberFormatter.Format(book.Get(first).Amount));
        }

        [Test]
        public void MergeDoesNotExtendLifetime()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Add(book, Hit(TARGET_A, 1, 5f), 1f, rules, out int index);
            Add(book, Hit(TARGET_A, 1, 5f), 1.07f, rules, out _);

            Assert.AreEqual(1f, book.Get(index).SpawnTime);
        }

        [Test]
        public void SameKeyAfterWindowSpawnsNewNumber()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Add(book, Hit(TARGET_A, 1, 5f), 0f, rules, out _);

            Assert.AreEqual(DamageNumberAction.Spawned, Add(book, Hit(TARGET_A, 1, 5f), 0.2f, rules, out _));
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void ZeroExecutionIdNeverMerges()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Add(book, Hit(TARGET_A, 0, 5f), 0f, rules, out _);

            Assert.AreEqual(DamageNumberAction.Spawned, Add(book, Hit(TARGET_A, 0, 5f), 0.01f, rules, out _));
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void CriticalAndNormalDoNotMerge()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Add(book, Hit(TARGET_A, 1, 5f), 0f, rules, out _);

            Assert.AreEqual(DamageNumberAction.Spawned, Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0.01f, rules, out _));
        }

        [Test]
        public void MergeRaisesTierButKeepsCritical()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0f, rules, out int index, ImpactTier.Heavy);
            Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0.02f, rules, out _, ImpactTier.Kill);

            Assert.AreEqual(ImpactTier.Kill, book.Get(index).Tier);
            Assert.IsTrue(book.Get(index).Key.IsCritical);
        }

        [Test]
        public void OffscreenOnlyMergesAndNeverSpawns()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 4);

            Assert.AreEqual(DamageNumberAction.Ignored, Add(book, Hit(TARGET_A, 1, 5f), 0f, rules, out _, canSpawn: false));

            Add(book, Hit(TARGET_A, 2, 5f), 0f, rules, out _);
            Assert.AreEqual(DamageNumberAction.Merged, Add(book, Hit(TARGET_A, 2, 5f), 0.01f, rules, out _, canSpawn: false));
        }

        [Test]
        public void ZeroAmountIsIgnored()
        {
            DamageNumberBook book = new DamageNumberBook(8);

            Assert.AreEqual(DamageNumberAction.Ignored, Add(book, Hit(TARGET_A, 1, 0f), 0f, Rules(8, 4), out _));
            Assert.AreEqual(0, book.ActiveCount);
        }

        /****************************************
        *            상한 교체
        ****************************************/

        [Test]
        public void TargetCapReplacesOldestNormalOfThatTarget()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 2);

            Add(book, Hit(TARGET_B, 1, 5f), 0f, rules, out int other);
            Add(book, Hit(TARGET_A, 2, 5f), 0.1f, rules, out int oldest);
            Add(book, Hit(TARGET_A, 3, 5f), 0.2f, rules, out _);

            Assert.AreEqual(DamageNumberAction.Replaced, Add(book, Hit(TARGET_A, 4, 5f), 0.3f, rules, out int replaced));
            Assert.AreEqual(oldest, replaced);
            Assert.AreEqual(2, book.CountForTarget(TARGET_A));
            Assert.IsTrue(book.Get(other).IsActive);
        }

        [Test]
        public void CapPrefersReplacingNormalOverCritical()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 2);

            Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0f, rules, out int critical);
            Add(book, Hit(TARGET_A, 2, 5f), 0.1f, rules, out int normal);

            Assert.AreEqual(DamageNumberAction.Replaced, Add(book, Hit(TARGET_A, 3, 5f, isCritical: true), 0.2f, rules, out int replaced));
            Assert.AreEqual(normal, replaced);
            Assert.IsTrue(book.Get(critical).Key.IsCritical);
        }

        [Test]
        public void NormalCannotPushOutCriticalButCriticalCan()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 2);

            Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0f, rules, out int oldestCritical);
            Add(book, Hit(TARGET_A, 2, 5f, isCritical: true), 0.1f, rules, out _);

            Assert.AreEqual(DamageNumberAction.Ignored, Add(book, Hit(TARGET_A, 3, 5f), 0.2f, rules, out _));
            Assert.AreEqual(DamageNumberAction.Replaced, Add(book, Hit(TARGET_A, 4, 5f, isCritical: true), 0.3f, rules, out int replaced));
            Assert.AreEqual(oldestCritical, replaced);
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void CapMergesSameKeyOutsideWindowBeforeReplacing()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(8, 1);

            Add(book, Hit(TARGET_A, 1, 5f), 0f, rules, out int index);

            Assert.AreEqual(DamageNumberAction.Merged, Add(book, Hit(TARGET_A, 1, 7f), 0.5f, rules, out int merged));
            Assert.AreEqual(index, merged);
            Assert.AreEqual(12f, book.Get(index).Amount, 0.0001f);
            Assert.AreEqual(0f, book.Get(index).SpawnTime);
        }

        [Test]
        public void ScreenCapCountsCriticalToo()
        {
            DamageNumberBook book = new DamageNumberBook(8);
            DamageNumberRules rules = Rules(2, 4);

            Add(book, Hit(TARGET_A, 1, 5f, isCritical: true), 0f, rules, out _);
            Add(book, Hit(TARGET_B, 2, 5f), 0.1f, rules, out int normal);

            Assert.AreEqual(DamageNumberAction.Replaced, Add(book, Hit(TARGET_A, 3, 5f, isCritical: true), 0.2f, rules, out int replaced));
            Assert.AreEqual(normal, replaced);
            Assert.AreEqual(2, book.ActiveCount);
        }

        [Test]
        public void ReleaseAndClearFreeSlots()
        {
            DamageNumberBook book = new DamageNumberBook(4);
            DamageNumberRules rules = Rules(4, 4);

            Add(book, Hit(TARGET_A, 1, 5f), 0f, rules, out int index);
            Add(book, Hit(TARGET_B, 2, 5f), 0f, rules, out _);

            book.Release(index);
            book.Release(index);
            Assert.AreEqual(1, book.ActiveCount);

            book.Clear();
            Assert.AreEqual(0, book.ActiveCount);
            Assert.AreEqual(0, book.CountForTarget(TARGET_B));
        }

        /****************************************
        *            크기 선택
        ****************************************/

        [Test]
        public void ScaleUsesLargerOfCriticalAndTierNotProduct()
        {
            Assert.AreEqual(1f,   DamageNumberRules.ResolveScale(false, ImpactTier.Light, CRIT_SCALE, TIER_MAX));
            Assert.AreEqual(1.2f, DamageNumberRules.ResolveScale(true,  ImpactTier.Light, CRIT_SCALE, TIER_MAX), 0.0001f);
            Assert.AreEqual(1.2f, DamageNumberRules.ResolveScale(false, ImpactTier.Medium, CRIT_SCALE, TIER_MAX), 0.0001f);
            Assert.AreEqual(1.2f, DamageNumberRules.ResolveScale(true,  ImpactTier.Kill, CRIT_SCALE, TIER_MAX), 0.0001f);
            Assert.AreEqual(1.3f, DamageNumberRules.ResolveScale(true,  ImpactTier.Heavy, 1.3f, 1.1f), 0.0001f);
            Assert.AreEqual(1.4f, DamageNumberRules.ResolveScale(false, ImpactTier.Kill, 1.3f, 1.4f), 0.0001f);
            Assert.AreEqual(1f,   DamageNumberRules.ResolveScale(false, ImpactTier.Party, CRIT_SCALE, TIER_MAX));
        }

        /****************************************
        *            배치
        ****************************************/

        [Test]
        public void CenterGuardPushesPointsToRadius()
        {
            Vector2 inside = DamageNumberPlacement.PushOutOfCenter(new Vector2(10f, 0f), 50f);
            Assert.AreEqual(50f, inside.magnitude, 0.001f);
            Assert.AreEqual(0f, inside.y, 0.001f);

            Vector2 outside = new Vector2(80f, 10f);
            Assert.AreEqual(outside, DamageNumberPlacement.PushOutOfCenter(outside, 50f));

            Assert.AreEqual(new Vector2(0f, 50f), DamageNumberPlacement.PushOutOfCenter(Vector2.zero, 50f));
        }

        /****************************************
        *            치명 표식
        ****************************************/

        [Test]
        public void CriticalMarkUsesGlyphOnlyWhenFontAlreadyHasIt()
        {
            DamageNumberCriticalMark withGlyph = DamageNumberCriticalMark.Resolve(true);
            Assert.AreEqual(DamageNumberCriticalMark.GLYPH_PREFIX, withGlyph.Prefix);
            Assert.IsNull(withGlyph.Suffix);
            Assert.IsFalse(withGlyph.IsFallback);
            StringAssert.Contains(DamageNumberCriticalMark.GLYPH.ToString(), withGlyph.Prefix);
        }

        [Test]
        public void CriticalMarkFallsBackToSuffixWithoutGlyph()
        {
            DamageNumberCriticalMark fallback = DamageNumberCriticalMark.Resolve(false);

            Assert.IsNull(fallback.Prefix);
            Assert.AreEqual(DamageNumberCriticalMark.FALLBACK_SUFFIX, fallback.Suffix);
            Assert.IsTrue(fallback.IsFallback);

            // 표식이 없어도 치명은 글자로 구별된다. 색만으로 구분하지 않는다
            Assert.AreEqual("1,500!", DamageNumberFormatter.Format(1500f, fallback.Prefix, fallback.Suffix));
            StringAssert.DoesNotContain(DamageNumberCriticalMark.GLYPH.ToString(),
                DamageNumberFormatter.Format(1500f, fallback.Prefix, fallback.Suffix));
        }

        /****************************************
        *            가림 검사
        ****************************************/

        [Test]
        public void OcclusionReusesResultPerTargetAndPartInsideWindow()
        {
            DamageNumberOcclusion occlusion = new DamageNumberOcclusion(4);

            Assert.IsFalse(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 0f, REUSE, out _));

            occlusion.Store(TARGET_A, DamageResult.NO_PART, 1f, true);

            Assert.IsTrue(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 1.1f, REUSE, out bool occluded));
            Assert.IsTrue(occluded);

            // 부위가 다르면 따로 본다(본체는 가려도 꼬리는 보일 수 있다)
            Assert.IsFalse(occlusion.TryGet(TARGET_A, 1, 1.1f, REUSE, out _));
            Assert.IsFalse(occlusion.TryGet(TARGET_B, DamageResult.NO_PART, 1.1f, REUSE, out _));

            // 창이 지나면 다시 검사해야 한다
            Assert.IsFalse(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 1f + REUSE + 0.01f, REUSE, out _));
        }

        [Test]
        public void OcclusionStoreOverwritesSameKey()
        {
            DamageNumberOcclusion occlusion = new DamageNumberOcclusion(4);

            occlusion.Store(TARGET_A, DamageResult.NO_PART, 0f, true);
            occlusion.Store(TARGET_A, DamageResult.NO_PART, 0.5f, false);

            Assert.IsTrue(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 0.6f, REUSE, out bool occluded));
            Assert.IsFalse(occluded);
        }

        [Test]
        public void OcclusionBudgetLimitsChecksPerFrame()
        {
            DamageNumberOcclusion occlusion = new DamageNumberOcclusion(4);

            Assert.IsTrue(occlusion.TryConsumeBudget(10, 2));
            Assert.IsTrue(occlusion.TryConsumeBudget(10, 2));
            Assert.IsFalse(occlusion.TryConsumeBudget(10, 2));

            // 다음 프레임에는 예산이 다시 찬다
            Assert.IsTrue(occlusion.TryConsumeBudget(11, 2));

            // 0이면 검사하지 않는다
            Assert.IsFalse(occlusion.TryConsumeBudget(12, 0));
        }

        [Test]
        public void OcclusionEvictsOldestWhenFull()
        {
            DamageNumberOcclusion occlusion = new DamageNumberOcclusion(2);

            occlusion.Store(TARGET_A, DamageResult.NO_PART, 0f, true);
            occlusion.Store(TARGET_B, DamageResult.NO_PART, 0.05f, true);
            occlusion.Store(TARGET_C, DamageResult.NO_PART, 0.1f, true);

            Assert.IsFalse(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 0.1f, REUSE, out _));
            Assert.IsTrue(occlusion.TryGet(TARGET_B, DamageResult.NO_PART, 0.1f, REUSE, out _));
            Assert.IsTrue(occlusion.TryGet(TARGET_C, DamageResult.NO_PART, 0.1f, REUSE, out _));
        }

        [Test]
        public void OcclusionClearForgetsResultsAndBudget()
        {
            DamageNumberOcclusion occlusion = new DamageNumberOcclusion(4);

            occlusion.Store(TARGET_A, DamageResult.NO_PART, 0f, true);
            occlusion.TryConsumeBudget(3, 1);
            occlusion.Clear();

            Assert.IsFalse(occlusion.TryGet(TARGET_A, DamageResult.NO_PART, 0f, REUSE, out _));
            Assert.IsTrue(occlusion.TryConsumeBudget(3, 1));
        }

        [Test]
        public void OcclusionSegmentEndIsPulledTowardCamera()
        {
            Vector3 camera = Vector3.zero;
            Vector3 hit    = new Vector3(0f, 0f, 10f);

            Assert.IsTrue(DamageNumberOcclusion.TryResolveSegmentEnd(camera, hit, 0.3f, out Vector3 end));
            Assert.AreEqual(9.7f, end.z, 0.0001f);
            Assert.AreEqual(0f, end.x, 0.0001f);

            Assert.IsTrue(DamageNumberOcclusion.TryResolveSegmentEnd(camera, hit, 0f, out end));
            Assert.AreEqual(10f, end.z, 0.0001f);

            // 적중점이 당기는 거리보다 가까우면 가릴 것이 없다
            Assert.IsFalse(DamageNumberOcclusion.TryResolveSegmentEnd(camera, new Vector3(0f, 0f, 0.2f), 0.3f, out _));
            Assert.IsFalse(DamageNumberOcclusion.TryResolveSegmentEnd(camera, camera, 0f, out _));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static DamageNumberRules Rules(int maxOnScreen, int maxPerTarget)
        {
            return new DamageNumberRules(WINDOW, maxOnScreen, maxPerTarget);
        }

        private static DamageNumberAction Add(DamageNumberBook book, DamageResult result, float now,
            DamageNumberRules rules, out int index, ImpactTier tier = ImpactTier.Light, bool canSpawn = true)
        {
            return book.Add(result, tier, now, rules, canSpawn, out index);
        }

        private static DamageResult Hit(uint target, uint executionId, float amount, sbyte part = DamageResult.NO_PART,
            DamageSourceKind source = DamageSourceKind.Melee, bool isCritical = false)
        {
            return new DamageResult
            {
                AttackerNetId = ATTACKER,
                ExecutionId   = executionId,
                TargetNetId   = target,
                PartIndex     = part,
                Source        = source,
                Amount        = amount,
                IsCritical    = isCritical,
            };
        }
    }
}
