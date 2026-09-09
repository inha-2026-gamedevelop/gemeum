// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems.Tests
{
    public sealed class TransactionRequestRulesTests
    {
        [Test]
        public void HeaderIsBoundToRunScenePlayerAndOperation()
        {
            var header = new TransactionRequestHeader
            {
                RunIdValue                   = 7u,
                SceneRevisionValue           = 9u,
                NetworkObjectGenerationValue = 11u,
                RequestId                    = 13u,
                Operation                    = TransactionOperation.Purchase,
            };

            Assert.AreEqual(TransactionRejection.None,
                TransactionRequestRules.ValidateHeader(header,
                    TransactionOperation.Purchase, new RunId(7u),
                    new SceneRevision(9u), 11u, false, false));
            Assert.AreEqual(TransactionRejection.StaleRequest,
                TransactionRequestRules.ValidateHeader(header,
                    TransactionOperation.Purchase, new RunId(8u),
                    new SceneRevision(9u), 11u, false, false));
            Assert.AreEqual(TransactionRejection.SceneMismatch,
                TransactionRequestRules.ValidateHeader(header,
                    TransactionOperation.Purchase, new RunId(7u),
                    new SceneRevision(10u), 11u, false, false));
            Assert.AreEqual(TransactionRejection.StaleRequest,
                TransactionRequestRules.ValidateHeader(header,
                    TransactionOperation.Purchase, new RunId(7u),
                    new SceneRevision(9u), 12u, false, false));
            Assert.AreEqual(TransactionRejection.InvalidRequest,
                TransactionRequestRules.ValidateHeader(header,
                    TransactionOperation.RerollTicket, new RunId(7u),
                    new SceneRevision(9u), 11u, false, false));
        }

        [Test]
        public void RequestSequenceRejectsReplayAndOldValuesWithoutWrappingAtMaximum()
        {
            Assert.IsTrue(TransactionRequestRules.IsNewer(11u, 10u));
            Assert.IsFalse(TransactionRequestRules.IsNewer(10u, 10u));
            Assert.IsFalse(TransactionRequestRules.IsNewer(9u, 10u));
            Assert.IsFalse(TransactionRequestRules.IsNewer(0u, uint.MaxValue));
            Assert.IsFalse(TransactionRequestRules.IsNewer(1u, uint.MaxValue));
        }

        [Test]
        public void RuleFailuresMapToStableTransactionRejections()
        {
            Assert.AreEqual(TransactionRejection.InventoryFull,
                TransactionRequestRules.FromInventory(InventoryResult.StackFull));
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                TransactionRequestRules.FromInventory(InventoryResult.ItemChanged));
            Assert.AreEqual(TransactionRejection.RevisionExhausted,
                TransactionRequestRules.FromInventory(InventoryResult.RevisionExhausted));
            Assert.AreEqual(TransactionRejection.InCombat,
                TransactionRequestRules.FromInteraction(InteractionResult.InCombat));
            Assert.AreEqual(TransactionRejection.ShopClosed,
                TransactionRequestRules.FromInteraction(InteractionResult.Unavailable));
        }
    }
}
