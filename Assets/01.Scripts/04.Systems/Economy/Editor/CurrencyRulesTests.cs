// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Systems;

namespace Geumeum.Systems.Tests
{
    public sealed class CurrencyRulesTests
    {
        private const uint RUN_ID = 17u;

        [Test]
        public void LossUsesFloorAndAllowsZeroBalance()
        {
            Assert.IsTrue(CurrencyRules.TryCalculateLoss(17, 0.2f, out int loss));
            Assert.AreEqual(3, loss);

            Assert.IsTrue(CurrencyRules.TryCalculateLoss(0, 0.2f, out loss));
            Assert.AreEqual(0, loss);
        }

        [Test]
        public void InvalidLossRatioIsRejectedWithoutLoss()
        {
            Assert.IsFalse(CurrencyRules.TryCalculateLoss(10, float.NaN, out int loss));
            Assert.AreEqual(0, loss);

            Assert.IsFalse(CurrencyRules.TryCalculateLoss(10, 1.01f, out loss));
            Assert.AreEqual(0, loss);
        }

        [Test]
        public void RewardMultiplierIsFlooredAfterInclusiveBaseRoll()
        {
            var random = new FixedRandom(0.999999);

            Assert.IsTrue(CurrencyRules.TryRollReward(10, 15, 1.5f, random,
                out int reward));
            Assert.AreEqual(22, reward);
        }

        [Test]
        public void SpendPlanIsBoundToRunAndRevision()
        {
            RunId run = new RunId(RUN_ID);
            Assert.IsTrue(CurrencyRules.TryPrepareSpend(run, 100, 4u, 25,
                out PartyWalletSpendPlan plan));
            Assert.AreEqual(run, plan.ExpectedRunId);
            Assert.IsTrue(plan.IsValid);

            Assert.IsTrue(CurrencyRules.TryCommitSpend(plan, run, 100, 4u,
                out int nextBalance));
            Assert.AreEqual(75, nextBalance);

            Assert.IsFalse(CurrencyRules.TryCommitSpend(plan, new RunId(RUN_ID + 1u),
                100, 4u, out nextBalance));
            Assert.AreEqual(100, nextBalance);

            Assert.IsFalse(CurrencyRules.TryCommitSpend(plan, run, 100, 5u,
                out nextBalance));
            Assert.AreEqual(100, nextBalance);
        }

        [Test]
        public void CreditRejectsNegativeAndOverflowAmounts()
        {
            Assert.IsFalse(CurrencyRules.TryCredit(5, -1, out int nextBalance));
            Assert.AreEqual(5, nextBalance);

            Assert.IsFalse(CurrencyRules.TryCredit(int.MaxValue, 1, out nextBalance));
            Assert.AreEqual(int.MaxValue, nextBalance);
        }

        [Test]
        public void MissingWalletDoesNotSilentlyGrantPaidRevive()
        {
            PartyWalletLocator.ResetStatic();

            Assert.IsFalse(PartyWalletLocator.HasProvider);
            Assert.IsFalse(PartyWalletLocator.Current.TryApplyReviveLoss(
                new ParticipantId(1UL), 1u, 0.2f, out int loss));
            Assert.AreEqual(0, loss);
        }

        private sealed class FixedRandom : Random
        {
            private readonly double _value;

            public FixedRandom(double value)
            {
                _value = value;
            }

            public override double NextDouble()
            {
                return _value;
            }
        }
    }
}
