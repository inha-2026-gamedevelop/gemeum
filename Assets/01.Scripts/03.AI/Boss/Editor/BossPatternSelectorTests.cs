// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI.Tests
{
    /// <summary> 보스 패턴 선택기의 서버 필터와 균등 추첨을 검증한다 </summary>
    public sealed class BossPatternSelectorTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TrySelectFiltersDistanceCooldownAndPreviousPattern()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(1, cooldownRemaining: 0f, isDistanceEligible: false),
                MakeCandidate(2, cooldownRemaining: 0.1f),
                MakeCandidate(3, cooldownRemaining: 0f),
                MakeCandidate(4, cooldownRemaining: 0f),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 3,
                new Random(1),
                out BossPatternSelector.Candidate result);

            Assert.IsTrue(selected);
            Assert.AreEqual(4, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectExcludesPatternWhileItsCooldownRemains()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(1, cooldownRemaining: 0.0001f),
                MakeCandidate(2),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 0,
                new Random(2),
                out BossPatternSelector.Candidate result);

            Assert.IsTrue(selected);
            Assert.AreEqual(2, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectExcludesTheImmediatelyPreviousPattern()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(7),
                MakeCandidate(8),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 7,
                new Random(3),
                out BossPatternSelector.Candidate result);

            Assert.IsTrue(selected);
            Assert.AreEqual(8, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectRejectsConditionalCandidates()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(9, isConditional: true),
                MakeCandidate(8),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 0,
                new Random(4),
                out BossPatternSelector.Candidate result);

            Assert.IsTrue(selected);
            Assert.AreEqual(8, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectRejectsPermanentlySealedCandidate()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(6, isSealed: true),
                MakeCandidate(8),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 0,
                new Random(41),
                out BossPatternSelector.Candidate result);

            Assert.IsTrue(selected);
            Assert.AreEqual(8, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectReturnsFalseWhenNoCandidateRemains()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(1, isDistanceEligible: false),
                MakeCandidate(2, cooldownRemaining: 1f),
                MakeCandidate(3, isConditional: true),
            };

            bool selected = BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 0,
                new Random(5),
                out BossPatternSelector.Candidate result);

            Assert.IsFalse(selected);
            Assert.AreEqual(0, result.Pattern.PatternId);
        }

        [Test]
        public void TrySelectUsesUniformCandidatesWithoutWeightData()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(1),
                MakeCandidate(2),
            };
            int firstCount = 0;
            int secondCount = 0;
            Random random = new Random(6);

            for (int i = 0; i < 64; ++i)
            {
                bool selected = BossPatternSelector.TrySelect(
                    candidates,
                    candidates.Length,
                    previousPatternId: 0,
                    random,
                    out BossPatternSelector.Candidate result);

                Assert.IsTrue(selected);
                if (result.Pattern.PatternId == 1)
                {
                    ++firstCount;
                }
                else
                {
                    Assert.AreEqual(2, result.Pattern.PatternId);
                    ++secondCount;
                }
            }

            Assert.Greater(firstCount, 0);
            Assert.Greater(secondCount, 0);
        }

        [Test]
        public void TrySelectRejectsInvalidInput()
        {
            BossPatternSelector.Candidate[] candidates =
            {
                MakeCandidate(1),
            };

            Assert.IsFalse(BossPatternSelector.TrySelect(
                null,
                0,
                previousPatternId: 0,
                new Random(7),
                out _));
            Assert.IsFalse(BossPatternSelector.TrySelect(
                candidates,
                0,
                previousPatternId: 0,
                new Random(7),
                out _));
            Assert.IsFalse(BossPatternSelector.TrySelect(
                candidates,
                candidates.Length + 1,
                previousPatternId: 0,
                new Random(7),
                out _));
            Assert.IsFalse(BossPatternSelector.TrySelect(
                candidates,
                candidates.Length,
                previousPatternId: 0,
                null,
                out _));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static BossPatternSelector.Candidate MakeCandidate(
            int patternId,
            float cooldownRemaining = 0f,
            bool isDistanceEligible = true,
            bool isConditional = false,
            bool isSealed = false)
        {
            BossPatternSO.PatternEntry pattern = new BossPatternSO.PatternEntry
            {
                PatternId = patternId,
            };

            return new BossPatternSelector.Candidate(
                pattern,
                cooldownRemaining,
                isDistanceEligible,
                isConditional,
                isSealed);
        }
    }
}
