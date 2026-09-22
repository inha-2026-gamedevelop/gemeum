// System
using System;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;
using UnityEngine.TestTools;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems.Tests
{
    /// <summary> 실제 거래 서비스가 여러 서버 원본과 중복 요청 경계를 함께 지키는지 검증한다 </summary>
    public sealed class TransactionServiceTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const uint RUN_ID = 7u;
        private const uint SCENE_REVISION = 3u;
        private const uint SHOP_NET_ID = 30u;

        private TransactionFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new TransactionFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void DevEquippedChangeInvalidatesCombatStatsAndPreservesIdentity()
        {
            _fixture.Sender.Status.ServerApplyGrowth(new GrowthState(1, 0, 100f, 1u, 0));
            var item = MakeWeapon(27u);
            Assert.AreEqual(InventoryResult.Success, _fixture.Sender.Inventory.ServerTryStoreEquipment(item, _fixture.Sender.Inventory.Revision));
            Assert.AreEqual(InventoryResult.Success, _fixture.Sender.Inventory.ServerTryEquip(0, item.InstanceId, _fixture.Sender.Inventory.Revision));
            Assert.IsTrue(_fixture.Sender.Inventory.TryBuildLoadout(_fixture.Sender.Participant, out var before));
            uint revision = _fixture.Sender.Inventory.LoadoutRevision;
            item.AttackPower = 42f;
            Assert.AreEqual(InventoryResult.Success, _fixture.Sender.Inventory.ServerDevReplaceEquipped(item));
            Assert.AreEqual(revision + 1u, _fixture.Sender.Inventory.LoadoutRevision);
            Assert.IsTrue(_fixture.Sender.Inventory.TryBuildLoadout(_fixture.Sender.Participant, out var after));
            Assert.AreEqual(42f, after.MeleeAttackPower);
            Assert.AreNotEqual(before.Revision, after.Revision);
            Assert.AreEqual(27u, _fixture.Sender.Inventory.EquippedWeapon.InstanceId);
            item.InstanceId = 99u;
            Assert.AreEqual(InventoryResult.InvalidItem, _fixture.Sender.Inventory.ServerDevReplaceEquipped(item));
            Assert.AreEqual(revision + 1u, _fixture.Sender.Inventory.LoadoutRevision);
        }

        [Test]
        public void PurchaseCommitsOnceAndFailureLeavesBothSourcesUnchanged()
        {
            ShopTransactionResult first = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.HealthPotion, 1u, 1u, 0u, out object stream,
                out object arguments);

            Assert.IsTrue(first.Succeeded);
            Assert.AreEqual(440, _fixture.Wallet.Balance);
            Assert.AreEqual(2u, _fixture.Wallet.Revision);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(ShopItemKind.HealthPotion,
                _fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).ItemId);

            ShopTransactionResult replay = _fixture.ReplayShop(stream, 1u, arguments);
            Assert.IsTrue(replay.Succeeded);
            Assert.AreEqual(440, _fixture.Wallet.Balance);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);

            object changedArguments = _fixture.MakeShopArguments(SHOP_NET_ID,
                ShopItemKind.SpiritPotion, -1, -1, 0u, 1u, 0u, false);
            ShopTransactionResult changedReplay = _fixture.ReplayShop(stream, 1u,
                changedArguments);
            Assert.AreEqual(TransactionRejection.DuplicateRequest,
                changedReplay.Rejection);
            Assert.AreEqual(440, _fixture.Wallet.Balance);

            ShopTransactionResult stale = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.SpiritPotion, 2u, 1u,
                _fixture.Sender.Inventory.Revision, out _, out _);
            Assert.AreEqual(TransactionRejection.StaleWalletRevision, stale.Rejection);
            Assert.AreEqual(440, _fixture.Wallet.Balance);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void OwnerlessPotionEntryReplaysWithoutDoubleConsumption()
        {
            _fixture.Sender.SetResources(25f, 100f, 80f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));
            Assert.IsNull(_fixture.Sender.Identity.connectionToClient);
            var header = new TransactionRequestHeader
            {
                RunIdValue = RUN_ID,
                SceneRevisionValue = SCENE_REVISION,
                NetworkObjectGenerationValue = _fixture.Sender.Identity.netId,
                RequestId = 1u,
                Operation = TransactionOperation.UsePotion,
            };

            ShopTransactionResult first = _fixture.ShopService.ServerRequestPotion(
                _fixture.Sender.Identity.netId, header, 0, ShopItemKind.HealthPotion, 1u);
            ShopTransactionResult replay = _fixture.ShopService.ServerRequestPotion(
                _fixture.Sender.Identity.netId, header, 0, ShopItemKind.HealthPotion, 1u);
            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(replay.Succeeded);
            Assert.AreEqual(65f, _fixture.Sender.Status.Health);
            Assert.AreEqual(2u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void OwnerlessPotionEntryRejectsAnotherActorsBinding()
        {
            _fixture.Sender.SetResources(25f, 100f, 80f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));
            var header = new TransactionRequestHeader
            {
                RunIdValue = RUN_ID,
                SceneRevisionValue = SCENE_REVISION,
                NetworkObjectGenerationValue = _fixture.Recipient.Identity.netId,
                RequestId = 1u,
                Operation = TransactionOperation.UsePotion,
            };
            ShopTransactionResult result = _fixture.ShopService.ServerRequestPotion(
                _fixture.Sender.Identity.netId, header, 0, ShopItemKind.HealthPotion, 1u);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(25f, _fixture.Sender.Status.Health);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void HealthAndSpiritPotionsCommitActualRecoveryOnce()
        {
            _fixture.Sender.SetResources(25f, 100f, 80f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.SpiritPotion, 1, 3, 3, 1u));

            ShopTransactionResult health = _fixture.UsePotion(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 1u, 2u, out object healthStream,
                out object healthArguments);

            Assert.IsTrue(health.Succeeded);
            Assert.AreEqual(40f, health.AppliedAmount);
            Assert.AreEqual(65f, _fixture.Sender.Status.Health);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1u, health.ResourceRevision);
            Assert.IsTrue(_fixture.ReplayShop(healthStream, 1u, healthArguments).Succeeded);
            Assert.AreEqual(65f, _fixture.Sender.Status.Health);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.Revision);

            ShopTransactionResult spirit = _fixture.UsePotion(_fixture.Sender, 1,
                ShopItemKind.SpiritPotion, 2u, 3u, out object spiritStream,
                out object spiritArguments);

            Assert.IsTrue(spirit.Succeeded);
            Assert.AreEqual(20f, spirit.AppliedAmount);
            Assert.AreEqual(100f, _fixture.Sender.Status.Spirit);
            Assert.AreEqual(4u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(2u, spirit.ResourceRevision);
            Assert.IsTrue(_fixture.ReplayShop(spiritStream, 2u, spiritArguments).Succeeded);
            Assert.AreEqual(100f, _fixture.Sender.Status.Spirit);
            Assert.AreEqual(4u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void FullOrDeadPotionTargetLeavesInventoryUnchanged()
        {
            _fixture.Sender.SetResources(100f, 100f, 100f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));

            ShopTransactionResult full = _fixture.UsePotion(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 1u, 1u, out _, out _);

            Assert.AreEqual(TransactionRejection.ResourceFull, full.Rejection);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1,
                _fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).Count);

            _fixture.Sender.SetResources(10f, 100f, 10f);
            var barrier = new TestBarrierState(0f);
            IBarrierState previousBarrier = BarrierStateLocator.Current;
            BarrierStateLocator.Register(barrier);
            try
            {
                ShopTransactionResult blocked = _fixture.UsePotion(_fixture.Sender, 0,
                    ShopItemKind.HealthPotion, 2u, 1u, out _, out _);
                Assert.AreEqual(TransactionRejection.RecoveryBlocked, blocked.Rejection);
                Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            }
            finally
            {
                BarrierStateLocator.Unregister(barrier);
                BarrierStateLocator.Register(previousBarrier);
            }

            Set(_fixture.Sender.Status, "_lifeState", PlayerLifeState.Downed);
            ShopTransactionResult dead = _fixture.UsePotion(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 3u, 1u, out _, out _);

            Assert.AreEqual(TransactionRejection.Dead, dead.Rejection);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1,
                _fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).Count);
        }

        [Test]
        public void PotionRejectsPausedSceneRunAndReconnectHeadersWithoutConsumption()
        {
            _fixture.Sender.SetResources(10f, 100f, 10f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));

            Set(_fixture.Run, "_pauseReasons", RunPauseReason.ParticipantMissing);
            ShopTransactionResult paused = _fixture.UsePotionWithHeader(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 1u, 1u, RUN_ID, SCENE_REVISION,
                _fixture.Sender.Identity.netId, out _, out _);
            Assert.AreEqual(TransactionRejection.Paused, paused.Rejection);

            Set(_fixture.Run, "_pauseReasons", RunPauseReason.None);
            Set(_fixture.Director, "_state", RunTransitionState.Loading);
            ShopTransactionResult transitioning = _fixture.UsePotionWithHeader(
                _fixture.Sender, 0, ShopItemKind.HealthPotion, 2u, 1u,
                RUN_ID, SCENE_REVISION, _fixture.Sender.Identity.netId,
                out _, out _);
            Assert.AreEqual(TransactionRejection.Paused, transitioning.Rejection);

            Set(_fixture.Director, "_state", RunTransitionState.Idle);
            ShopTransactionResult wrongScene = _fixture.UsePotionWithHeader(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 3u, 1u, RUN_ID, SCENE_REVISION + 1u,
                _fixture.Sender.Identity.netId, out _, out _);
            Assert.AreEqual(TransactionRejection.SceneMismatch, wrongScene.Rejection);

            ShopTransactionResult oldRun = _fixture.UsePotionWithHeader(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 4u, 1u, RUN_ID + 1u, SCENE_REVISION,
                _fixture.Sender.Identity.netId, out _, out _);
            Assert.AreEqual(TransactionRejection.StaleRequest, oldRun.Rejection);

            ShopTransactionResult oldBinding = _fixture.UsePotionWithHeader(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 5u, 1u, RUN_ID, SCENE_REVISION,
                _fixture.Sender.Identity.netId - 1u, out _, out _);
            Assert.AreEqual(TransactionRejection.StaleRequest, oldBinding.Rejection);
            Assert.AreEqual(10f, _fixture.Sender.Status.Health);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1,
                _fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).Count);
        }

        [Test]
        public void ThrowingResourceObserversSeeFinalRawStateAndReleaseBothLocks()
        {
            _fixture.Sender.SetResources(20f, 100f, 20f);
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.SpiritPotion, 1, 3, 3, 1u));

            bool healthTrailingRan = false;
            float observedHealth = 0f;
            uint observedHealthInventoryRevision = 0u;
            uint observedHealthResourceRevision = 0u;
            ShopTransactionResult healthReplay = default;
            object healthStream = null;
            object healthArguments = null;
            Action<float, float> throwingHealth = (current, maximum) =>
                throw new InvalidOperationException("health observer test");
            Action<float, float> trailingHealth = (current, maximum) =>
            {
                healthTrailingRan = true;
                observedHealth = _fixture.Sender.Status.Health;
                observedHealthInventoryRevision = _fixture.Sender.Inventory.Revision;
                observedHealthResourceRevision = _fixture.Sender.Status.ResourceRevision;
                healthReplay = _fixture.ReplayShop(healthStream, 1u, healthArguments);
            };
            _fixture.Sender.Status.HealthChanged += throwingHealth;
            _fixture.Sender.Status.HealthChanged += trailingHealth;
            LogAssert.Expect(LogType.Error,
                new Regex("\\[PlayerStatus\\] 체력 observer 실패"));

            ShopTransactionResult health = _fixture.UsePotion(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 1u, 2u, out healthStream,
                out healthArguments);

            _fixture.Sender.Status.HealthChanged -= throwingHealth;
            _fixture.Sender.Status.HealthChanged -= trailingHealth;
            Assert.IsTrue(health.Succeeded);
            Assert.IsTrue(healthTrailingRan);
            Assert.IsTrue(healthReplay.Succeeded);
            Assert.AreEqual(60f, observedHealth);
            Assert.AreEqual(3u, observedHealthInventoryRevision);
            Assert.AreEqual(1u, observedHealthResourceRevision);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1u, _fixture.Sender.Status.ResourceRevision);

            bool spiritTrailingRan = false;
            float observedSpirit = 0f;
            uint observedSpiritInventoryRevision = 0u;
            uint observedSpiritResourceRevision = 0u;
            Action<float, float> throwingSpirit = (current, maximum) =>
                throw new InvalidOperationException("spirit observer test");
            Action<float, float> trailingSpirit = (current, maximum) =>
            {
                spiritTrailingRan = true;
                observedSpirit = _fixture.Sender.Status.Spirit;
                observedSpiritInventoryRevision = _fixture.Sender.Inventory.Revision;
                observedSpiritResourceRevision = _fixture.Sender.Status.ResourceRevision;
            };
            _fixture.Sender.Status.SpiritChanged += throwingSpirit;
            _fixture.Sender.Status.SpiritChanged += trailingSpirit;
            LogAssert.Expect(LogType.Error,
                new Regex("\\[PlayerStatus\\] 신력 observer 실패"));

            ShopTransactionResult spirit = _fixture.UsePotion(_fixture.Sender, 1,
                ShopItemKind.SpiritPotion, 2u, 3u, out _, out _);

            _fixture.Sender.Status.SpiritChanged -= throwingSpirit;
            _fixture.Sender.Status.SpiritChanged -= trailingSpirit;
            Assert.IsTrue(spirit.Succeeded);
            Assert.IsTrue(spiritTrailingRan);
            Assert.AreEqual(55f, observedSpirit);
            Assert.AreEqual(4u, observedSpiritInventoryRevision);
            Assert.AreEqual(2u, observedSpiritResourceRevision);
            Assert.AreEqual(4u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(2u, _fixture.Sender.Status.ResourceRevision);
            Assert.IsFalse(Get<bool>(_fixture.Sender.Inventory, "_transactionLocked"));
            Assert.IsFalse(_fixture.Sender.Status.IsResourceTransactionLocked);

            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 4u));
            ShopTransactionResult next = _fixture.UsePotion(_fixture.Sender, 0,
                ShopItemKind.HealthPotion, 3u, 5u, out _, out _);
            Assert.IsTrue(next.Succeeded);
            Assert.AreEqual(6u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void StoredTicketPreservesEquipmentIdAndReplayConsumesNothing()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.RerollTicket, 1, -1, 1, 0u));
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(
                    MakeWeapon(41u, ItemGrade.Talisman), 1u));

            ShopTransactionResult result = _fixture.UseTicket(_fixture.Sender,
                TransactionOperation.RerollTicket, 0, 1, 41u, 2u, false,
                1u, out object stream, out object arguments);

            Assert.IsTrue(result.Succeeded);
            RunParticipantSnapshot state = _fixture.Capture(_fixture.Sender);
            Assert.IsTrue(state.Inventory.GetSlot(0).IsEmpty);
            Assert.AreEqual(41u, state.Inventory.GetSlot(1).Equipment.InstanceId);
            Assert.AreEqual(ItemGrade.Talisman,
                state.Inventory.GetSlot(1).Equipment.Grade);
            Assert.AreEqual(3u, state.Inventory.Revision);

            ShopTransactionResult replay = _fixture.ReplayShop(stream, 1u, arguments);
            Assert.IsTrue(replay.Succeeded);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.Revision);
            Assert.IsTrue(_fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).IsEmpty);
        }

        [Test]
        public void EquippedTicketUpdatesLoadoutWithoutChangingEquipmentId()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.UpgradeTicket, 1, -1, 1, 0u));
            WeaponCombatSnapshot equipped = MakeWeapon(52u, ItemGrade.Common);
            Set(_fixture.Sender.Inventory, "_equippedWeapon", equipped);
            Set(_fixture.Sender.Inventory, "_hasEquippedWeapon", true);
            Set(_fixture.Sender.Inventory, "_serverEquippedWeapon", equipped);
            Set(_fixture.Sender.Inventory, "_serverHasEquippedWeapon", true);
            Set(_fixture.Sender.Inventory, "_loadoutRevision", 4u);

            ShopTransactionResult result = _fixture.UseTicket(_fixture.Sender,
                TransactionOperation.UpgradeTicket, 0, -1, 52u, 1u, true,
                1u, out object stream, out object arguments);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(52u, _fixture.Sender.Inventory.EquippedWeapon.InstanceId);
            Assert.AreEqual(ItemGrade.Talisman,
                _fixture.Sender.Inventory.EquippedWeapon.Grade);
            Assert.AreEqual(5u, _fixture.Sender.Inventory.LoadoutRevision);
            Assert.IsTrue(_fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).IsEmpty);

            Assert.IsTrue(_fixture.ReplayShop(stream, 1u, arguments).Succeeded);
            Assert.AreEqual(2u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(5u, _fixture.Sender.Inventory.LoadoutRevision);
        }

        [Test]
        public void TransferAcceptPublishesAfterBothRawSourcesAndBlocksCrossServiceReentry()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(MakeWeapon(63u), 0u));
            EquipmentTransferResult offered = _fixture.Offer(1u, 0, 63u, 1u);
            Assert.IsTrue(offered.Succeeded);

            uint senderRevisionInCallback = 0u;
            uint recipientRevisionInCallback = 0u;
            uint recipientEquipmentInCallback = 0u;
            ShopTransactionResult reentryResult = default;
            int walletBefore = _fixture.Wallet.Balance;
            _fixture.Sender.Slots.Callback += (operation, index, oldValue, newValue) =>
            {
                senderRevisionInCallback = _fixture.Sender.Inventory.Revision;
                recipientRevisionInCallback = _fixture.Recipient.Inventory.Revision;
                recipientEquipmentInCallback = _fixture.Capture(_fixture.Recipient)
                    .Inventory.GetSlot(0).Equipment.InstanceId;
                reentryResult = _fixture.Purchase(_fixture.Recipient,
                    ShopItemKind.HealthPotion, 91u, _fixture.Wallet.Revision,
                    _fixture.Recipient.Inventory.Revision, out _, out _);
            };

            EquipmentTransferResult accepted = _fixture.Accept(offered.Offer, 2u,
                offered.Offer.RecipientInventoryRevision,
                out object stream, out object arguments);

            Assert.IsTrue(accepted.Succeeded);
            Assert.AreEqual(2u, senderRevisionInCallback);
            Assert.AreEqual(1u, recipientRevisionInCallback);
            Assert.AreEqual(63u, recipientEquipmentInCallback);
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                reentryResult.Rejection);
            Assert.AreEqual(walletBefore, _fixture.Wallet.Balance);
            Assert.IsTrue(_fixture.Capture(_fixture.Sender).Inventory.GetSlot(0).IsEmpty);
            Assert.AreEqual(63u, _fixture.Capture(_fixture.Recipient)
                .Inventory.GetSlot(0).Equipment.InstanceId);

            EquipmentTransferResult replay = _fixture.ReplayTransfer(stream, 2u,
                arguments);
            Assert.IsTrue(replay.Succeeded);
            Assert.AreEqual(2u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(1u, _fixture.Recipient.Inventory.Revision);
        }

        [Test]
        public void TransferRejectsDistanceAndExpiresWithoutMovingEquipment()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(MakeWeapon(74u), 0u));
            _fixture.Recipient.Identity.transform.position = Vector3.right * 6f;

            EquipmentTransferResult distant = _fixture.Offer(1u, 0, 74u, 1u);
            Assert.AreEqual(TransactionRejection.OutOfRange, distant.Rejection);
            Assert.AreEqual(1u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(0u, _fixture.Recipient.Inventory.Revision);

            _fixture.Recipient.Identity.transform.position = Vector3.right;
            EquipmentTransferResult offered = _fixture.Offer(2u, 0, 74u, 1u);
            Assert.IsTrue(offered.Succeeded);
            _fixture.RunTime.GameplayElapsedSeconds =
                offered.Offer.ExpiresAtGameplayTime;

            EquipmentTransferResult expired = _fixture.Accept(offered.Offer, 3u,
                offered.Offer.RecipientInventoryRevision,
                out _, out _);
            Assert.AreEqual(TransactionRejection.OfferExpired, expired.Rejection);
            Assert.AreEqual(EquipmentTransferState.Expired, expired.Offer.State);
            Assert.AreEqual(74u, _fixture.Capture(_fixture.Sender)
                .Inventory.GetSlot(0).Equipment.InstanceId);
            Assert.IsTrue(_fixture.Capture(_fixture.Recipient).Inventory.GetSlot(0).IsEmpty);
        }

        [Test]
        public void OfferCapturesPrivateRecipientRevisionAndRejectsStaleAccepts()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(MakeWeapon(76u), 0u));
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Recipient.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.HealthPotion, 1, 3, 3, 0u));

            EquipmentTransferResult offered = _fixture.Offer(1u, 0, 76u, 1u);
            Assert.IsTrue(offered.Succeeded,
                "송신자가 볼 수 없는 수신자 판은 서버가 직접 캡처해야 한다");
            Assert.AreEqual(1u, offered.Offer.RecipientInventoryRevision);

            EquipmentTransferResult staleRequest = _fixture.Accept(offered.Offer, 2u, 0u,
                out _, out _);
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                staleRequest.Rejection);

            Assert.AreEqual(InventoryResult.Success,
                _fixture.Recipient.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.SpiritPotion, 1, 3, 3, 1u));
            EquipmentTransferResult changedAfterOffer = _fixture.Accept(offered.Offer,
                3u, 2u, out _, out _);
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                changedAfterOffer.Rejection);
            Assert.AreEqual(EquipmentTransferState.Cancelled,
                changedAfterOffer.Offer.State);
            Assert.AreEqual(76u, _fixture.Sender.Slots[0].Equipment.InstanceId);
            Assert.AreEqual(2u, _fixture.Recipient.Inventory.Revision);
        }

        [Test]
        public void ThrowingWalletObserverDoesNotSkipInventoryOrNextPurchase()
        {
            bool trailingObserverRan = false;
            Action<int> throwingObserver = _ =>
                throw new InvalidOperationException("wallet observer test");
            Action<int> trailingObserver = _ => trailingObserverRan = true;
            _fixture.Wallet.BalanceChanged += throwingObserver;
            _fixture.Wallet.BalanceChanged += trailingObserver;
            LogAssert.Expect(LogType.Error,
                new Regex("\\[CurrencyManager\\] 잔액 observer 실패"));

            ShopTransactionResult first = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.HealthPotion, 1u, 1u, 0u, out _, out _);

            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(trailingObserverRan,
                "앞 observer 예외 뒤의 구독자도 같은 확정 잔액을 받아야 한다");
            Assert.AreEqual(ShopItemKind.HealthPotion,
                _fixture.Sender.Slots[0].ItemId);
            Assert.AreEqual(1u, Get<uint>(_fixture.Sender.Inventory, "_revision"));

            _fixture.Wallet.BalanceChanged -= throwingObserver;
            ShopTransactionResult second = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.SpiritPotion, 2u, 2u, 1u, out _, out _);
            Assert.IsTrue(second.Succeeded);
            Assert.AreEqual(390, _fixture.Wallet.Balance);
            Assert.AreEqual(3u, _fixture.Wallet.Revision);
            Assert.AreEqual(2u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void ThrowingEquippedObserverStillPublishesTicketAndAllowsNextTicket()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.UpgradeTicket, 1, -1, 1, 0u));
            WeaponCombatSnapshot equipped = MakeWeapon(82u, ItemGrade.Common);
            Set(_fixture.Sender.Inventory, "_equippedWeapon", equipped);
            Set(_fixture.Sender.Inventory, "_hasEquippedWeapon", true);
            Set(_fixture.Sender.Inventory, "_serverEquippedWeapon", equipped);
            Set(_fixture.Sender.Inventory, "_serverHasEquippedWeapon", true);
            Set(_fixture.Sender.Inventory, "_loadoutRevision", 4u);

            bool trailingObserverRan = false;
            Action<WeaponCombatSnapshot> throwingObserver = _ =>
                throw new InvalidOperationException("equipped observer test");
            _fixture.Sender.Inventory.EquippedWeaponChanged += throwingObserver;
            _fixture.Sender.Inventory.EquippedWeaponChanged += _ =>
                trailingObserverRan = true;
            LogAssert.Expect(LogType.Error,
                new Regex("\\[InventoryController\\] 장착 장비 observer 실패"));

            ShopTransactionResult first = _fixture.UseTicket(_fixture.Sender,
                TransactionOperation.UpgradeTicket, 0, -1, 82u, 1u, true,
                1u, out _, out _);

            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(trailingObserverRan);
            Assert.IsTrue(_fixture.Sender.Slots[0].IsEmpty);
            Assert.AreEqual(2u, Get<uint>(_fixture.Sender.Inventory, "_revision"));
            _fixture.Sender.Inventory.EquippedWeaponChanged -= throwingObserver;

            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreConsumable(
                    ShopItemKind.RerollTicket, 1, -1, 1, 2u));
            ShopTransactionResult second = _fixture.UseTicket(_fixture.Sender,
                TransactionOperation.RerollTicket, 0, -1, 82u, 3u, true,
                2u, out _, out _);
            Assert.IsTrue(second.Succeeded);
            Assert.AreEqual(4u, _fixture.Sender.Inventory.Revision);
            Assert.AreEqual(6u, _fixture.Sender.Inventory.LoadoutRevision);
        }

        [Test]
        public void ThrowingSlotObserverCompletesBothTransferPublishesAndAllowsPurchase()
        {
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(MakeWeapon(93u), 0u));
            EquipmentTransferResult offered = _fixture.Offer(1u, 0, 93u, 1u);
            Assert.IsTrue(offered.Succeeded);

            bool threw = false;
            _fixture.Sender.Slots.Callback += (operation, index, oldValue, newValue) =>
            {
                if (!threw)
                {
                    threw = true;
                    throw new InvalidOperationException("slot observer test");
                }
            };
            LogAssert.Expect(LogType.Error,
                new Regex("\\[InventoryController\\] 0번 슬롯 observer 실패"));

            EquipmentTransferResult accepted = _fixture.Accept(offered.Offer, 2u,
                offered.Offer.RecipientInventoryRevision,
                out _, out _);

            Assert.IsTrue(accepted.Succeeded);
            Assert.IsTrue(_fixture.Sender.Slots[0].IsEmpty);
            Assert.AreEqual(93u,
                _fixture.Recipient.Slots[0].Equipment.InstanceId);
            Assert.AreEqual(2u, Get<uint>(_fixture.Sender.Inventory, "_revision"));
            Assert.AreEqual(1u, Get<uint>(_fixture.Recipient.Inventory, "_revision"));

            ShopTransactionResult next = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.HealthPotion, 3u, 1u, 2u, out _, out _);
            Assert.IsTrue(next.Succeeded);
            Assert.AreEqual(440, _fixture.Wallet.Balance);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.Revision);
        }

        [Test]
        public void EquipAndUnequipFinalizeRawStateAndReplayBeforeWeaponObservers()
        {
            WeaponCombatSnapshot equipment = MakeWeapon(105u);
            equipment.WeaponKind = WeaponKind.Bow;
            Assert.AreEqual(InventoryResult.Success,
                _fixture.Sender.Inventory.ServerTryStoreEquipment(equipment, 0u));

            InventoryResult equipReplay = default;
            ShopTransactionResult equipReentry = default;
            uint equipRevisionInObserver = 0u;
            uint equippedIdInObserver = 0u;
            bool trailingEquipObserverRan = false;
            Action<WeaponKind> throwingObserver = _ =>
                throw new InvalidOperationException("weapon observer test");
            Action<WeaponKind> equipObserver = weaponKind =>
            {
                trailingEquipObserverRan = true;
                RunParticipantSnapshot state = _fixture.Capture(_fixture.Sender);
                equipRevisionInObserver = state.Inventory.Revision;
                equippedIdInObserver = state.EquippedWeapon.InstanceId;
                equipReplay = _fixture.Sender.Inventory.ServerTryEquip(
                    _fixture.Sender.Identity.netId, 0, 105u, 1u, 1u, out _);
                equipReentry = _fixture.Purchase(_fixture.Sender,
                    ShopItemKind.HealthPotion, 81u, _fixture.Wallet.Revision,
                    state.Inventory.Revision, out _, out _);
            };
            _fixture.Sender.Weapon.WeaponChanged += throwingObserver;
            _fixture.Sender.Weapon.WeaponChanged += equipObserver;
            LogAssert.Expect(LogType.Error,
                new Regex("\\[PlayerWeapon\\].*observer"));

            InventoryResult equipped = _fixture.Sender.Inventory.ServerTryEquip(
                _fixture.Sender.Identity.netId, 0, 105u, 1u, 1u,
                out WeaponCombatSnapshot previousWeapon);

            _fixture.Sender.Weapon.WeaponChanged -= throwingObserver;
            _fixture.Sender.Weapon.WeaponChanged -= equipObserver;
            Assert.AreEqual(InventoryResult.Success, equipped);
            Assert.IsTrue(trailingEquipObserverRan);
            Assert.AreEqual(InventoryResult.Success, equipReplay,
                "observer reentry must replay the already-finalized equip result");
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                equipReentry.Rejection);
            Assert.AreEqual(0u, previousWeapon.InstanceId);
            Assert.AreEqual(2u, equipRevisionInObserver);
            Assert.AreEqual(105u, equippedIdInObserver);
            Assert.AreEqual(2u, _fixture.Sender.Inventory.LoadoutRevision);
            Assert.AreEqual(WeaponKind.Bow, _fixture.Sender.Weapon.Weapon);
            Assert.IsTrue(_fixture.Sender.Slots[0].IsEmpty);
            Assert.AreEqual(500, _fixture.Wallet.Balance);

            ShopTransactionResult nextAfterEquip = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.HealthPotion, 82u, 1u, 2u, out _, out _);
            Assert.IsTrue(nextAfterEquip.Succeeded);

            InventoryResult unequipReplay = default;
            ShopTransactionResult unequipReentry = default;
            uint unequipRevisionInObserver = 0u;
            uint equippedIdAfterUnequipInObserver = uint.MaxValue;
            Action<WeaponKind> unequipObserver = weaponKind =>
            {
                RunParticipantSnapshot state = _fixture.Capture(_fixture.Sender);
                unequipRevisionInObserver = state.Inventory.Revision;
                equippedIdAfterUnequipInObserver = state.EquippedWeapon.InstanceId;
                unequipReplay = _fixture.Sender.Inventory.ServerTryUnequip(
                    _fixture.Sender.Identity.netId, 3u, 2u, out _);
                unequipReentry = _fixture.Purchase(_fixture.Sender,
                    ShopItemKind.SpiritPotion, 83u, _fixture.Wallet.Revision,
                    state.Inventory.Revision, out _, out _);
            };
            _fixture.Sender.Weapon.WeaponChanged += unequipObserver;

            InventoryResult unequipped = _fixture.Sender.Inventory.ServerTryUnequip(
                _fixture.Sender.Identity.netId, 3u, 2u,
                out WeaponCombatSnapshot removedWeapon);

            _fixture.Sender.Weapon.WeaponChanged -= unequipObserver;
            Assert.AreEqual(InventoryResult.Success, unequipped);
            Assert.AreEqual(InventoryResult.Success, unequipReplay,
                "observer reentry must replay the already-finalized unequip result");
            Assert.AreEqual(TransactionRejection.StaleInventoryRevision,
                unequipReentry.Rejection);
            Assert.AreEqual(105u, removedWeapon.InstanceId);
            Assert.AreEqual(4u, unequipRevisionInObserver);
            Assert.AreEqual(0u, equippedIdAfterUnequipInObserver);
            Assert.AreEqual(3u, _fixture.Sender.Inventory.LoadoutRevision);
            Assert.AreEqual(105u, _fixture.Sender.Slots[1].Equipment.InstanceId);

            ShopTransactionResult nextAfterUnequip = _fixture.Purchase(_fixture.Sender,
                ShopItemKind.SpiritPotion, 84u, 2u, 4u, out _, out _);
            Assert.IsTrue(nextAfterUnequip.Succeeded);
            Assert.AreEqual(390, _fixture.Wallet.Balance);
            Assert.AreEqual(5u, _fixture.Sender.Inventory.Revision);
        }

        private static WeaponCombatSnapshot MakeWeapon(uint instanceId,
            ItemGrade grade = ItemGrade.Common)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId  = instanceId,
                WeaponKind  = WeaponKind.Sword,
                Grade       = grade,
                AttackPower = 100f,
            };
        }

        private static FieldInfo Field(object target, string name)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    PRIVATE_INSTANCE | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, name);
        }

        private static void Set(object target, string name, object value)
        {
            Field(target, name).SetValue(target, value);
        }

        private static T Get<T>(object target, string name)
        {
            return (T) Field(target, name).GetValue(target);
        }

        private sealed class TransactionFixture : IDisposable
        {
            private readonly bool _wasServerActive;
            private readonly LocalConnectionToClient _previousLocalConnection;
            private readonly RunManager _previousRun;
            private readonly RunSceneDirector _previousDirector;
            private readonly IRunTimeSource _previousRunTime;
            private readonly GameObject _runObject;
            private readonly GameObject _directorObject;
            private readonly GameObject _sessionObject;
            private readonly GameObject _shopObject;
            private readonly GameDatabaseSO _database;
            private readonly CombatConfigSO _combat;
            private readonly ArrowConfigSO _arrow;
            private readonly TalentTreeSO _talentTree;
            private readonly LevelCurveSO _levelCurve;
            private readonly ShopSO _shop;

            public readonly TestRunTimeSource RunTime;
            public readonly RunManager Run;
            public readonly RunSceneDirector Director;
            public readonly PlayerFixture Sender;
            public readonly PlayerFixture Recipient;
            public readonly CurrencyManager Wallet;
            public readonly ShopTransactionService ShopService;
            public readonly EquipmentTransferService TransferService;

            public TransactionFixture()
            {
                _wasServerActive = NetworkServer.active;
                _previousLocalConnection = NetworkServer.localConnection;
                _previousRun = RunManager.ServerInstance;
                _previousDirector = RunSceneDirector.ServerInstance;
                _previousRunTime = RunTimeLocator.Current;
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, true);
                SetStaticProperty(typeof(NetworkServer),
                    nameof(NetworkServer.localConnection), new LocalConnectionToClient());
                CombatClock.ClearAll();

                _combat = ScriptableObject.CreateInstance<CombatConfigSO>();
                Set(_combat, "_interactRange", 5f);
                Set(_combat, "_interactOutOfCombatSeconds", 0f);
                Set(_combat, "_equipmentTransferOfferDuration", 30f);
                Set(_combat, "_spiritMax", 100f);
                _arrow = ScriptableObject.CreateInstance<ArrowConfigSO>();
                _talentTree = ScriptableObject.CreateInstance<TalentTreeSO>();
                _levelCurve = ScriptableObject.CreateInstance<LevelCurveSO>();
                Set(_levelCurve, "_levels", new[]
                {
                    new LevelCurveSO.LevelEntry
                    {
                        Level              = Constants.Combat.START_LEVEL,
                        MaxHealth          = 100f,
                        RequiredExperience = 0,
                    },
                });
                _shop = ScriptableObject.CreateInstance<ShopSO>();
                Set(_shop, "_openPhase", RunPhase.Shrink2);
                Set(_shop, "_items", new[]
                {
                    new ShopSO.ItemEntry
                    {
                        Kind = ShopItemKind.HealthPotion,
                        Price = 60,
                        EffectValue = 40f,
                        CarryLimit = 3,
                        MaxStack = 3,
                    },
                    new ShopSO.ItemEntry
                    {
                        Kind = ShopItemKind.SpiritPotion,
                        Price = 50,
                        EffectValue = 35f,
                        CarryLimit = 3,
                        MaxStack = 3,
                    },
                });
                _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
                Set(_database, "_combat", _combat);
                Set(_database, "_arrow", _arrow);
                Set(_database, "_talent", _talentTree);
                Set(_database, "_levelCurve", _levelCurve);
                Set(_database, "_shop", _shop);
                Set(_database, "_equipment", AssetDatabase.LoadAssetAtPath<EquipmentSO>(
                    "Assets/09.Data/Equipment/EquipmentSO.asset"));
                Set(_database, "_dropTable", AssetDatabase.LoadAssetAtPath<DropTableSO>(
                    "Assets/09.Data/DropTable/DropTableSO.asset"));
                GameDB.Initialize(_database);

                _runObject = new GameObject("TransactionTestRun",
                    typeof(NetworkIdentity), typeof(RunManager));
                Run = _runObject.GetComponent<RunManager>();
                Set(Run, "_runIdValue", RUN_ID);
                Set(Run, "_phase", RunPhase.Shrink2);
                SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), Run);

                _directorObject = new GameObject("TransactionTestDirector",
                    typeof(NetworkIdentity), typeof(RunSceneDirector));
                Director = _directorObject.GetComponent<RunSceneDirector>();
                Set(Director, "_revisionValue", SCENE_REVISION);
                Set(Director, "_state", RunTransitionState.Idle);
                SetStaticProperty(typeof(RunSceneDirector),
                    nameof(RunSceneDirector.ServerInstance), Director);

                RunTime = new TestRunTimeSource { GameplayElapsedSeconds = 10d };
                RunTimeLocator.Register(RunTime);

                Sender = new PlayerFixture("TransactionTestSender", 11u,
                    new ParticipantId(101UL));
                Recipient = new PlayerFixture("TransactionTestRecipient", 12u,
                    new ParticipantId(102UL));
                Recipient.Identity.transform.position = Vector3.right;

                _shopObject = new GameObject("TransactionTestShop",
                    typeof(NetworkIdentity), typeof(ShopInteractionPoint));
                InitializeIdentity(_shopObject.GetComponent<NetworkIdentity>(), SHOP_NET_ID);
                Set(_shopObject.GetComponent<ShopInteractionPoint>(), "_isOpen", true);
                NetworkServer.spawned.Add(SHOP_NET_ID,
                    _shopObject.GetComponent<NetworkIdentity>());

                _sessionObject = new GameObject("TransactionTestSession");
                NetworkIdentity sessionIdentity = _sessionObject.AddComponent<NetworkIdentity>();
                Wallet = _sessionObject.AddComponent<CurrencyManager>();
                ShopService = _sessionObject.AddComponent<ShopTransactionService>();
                TransferService = _sessionObject.AddComponent<EquipmentTransferService>();
                InitializeIdentity(sessionIdentity, 20u);
                Set(Wallet, "_serverBalance", 500);
                Set(Wallet, "_serverBalanceRevision", 1u);
                Set(Wallet, "_balance", 500);
                Set(Wallet, "_balanceRevision", 1u);
                Set(ShopService, "_wallet", Wallet);
                Set(ShopService, "_equipmentGenerator", new EquipmentGenerator(
                    GameDB.Equipment, GameDB.DropTable, new System.Random(1234)));
            }

            public ShopTransactionResult Purchase(PlayerFixture player,
                ShopItemKind itemKind, uint requestId, uint walletRevision,
                uint inventoryRevision, out object stream, out object arguments)
            {
                TransactionRequestHeader header = Header(TransactionOperation.Purchase,
                    requestId, player.Identity.netId);
                stream = MakeNested(typeof(ShopTransactionService), "StreamKey",
                    header, player.Participant);
                arguments = MakeShopArguments(SHOP_NET_ID, itemKind, -1, -1, 0u,
                    walletRevision, inventoryRevision, false);
                object[] call =
                {
                    header, SHOP_NET_ID, itemKind, walletRevision, inventoryRevision,
                    player.Identity, player.Inventory, stream, arguments, false,
                };
                return (ShopTransactionResult) Invoke(ShopService, "ServerPurchase", call);
            }

            public ShopTransactionResult UseTicket(PlayerFixture player,
                TransactionOperation operation, int ticketSlot, int equipmentSlot,
                uint equipmentInstanceId, uint inventoryRevision, bool equipped,
                uint requestId, out object stream, out object arguments)
            {
                ShopItemKind itemKind = operation == TransactionOperation.RerollTicket
                    ? ShopItemKind.RerollTicket : ShopItemKind.UpgradeTicket;
                TransactionRequestHeader header = Header(operation, requestId,
                    player.Identity.netId);
                stream = MakeNested(typeof(ShopTransactionService), "StreamKey",
                    header, player.Participant);
                arguments = MakeShopArguments(0u, itemKind, ticketSlot,
                    equipmentSlot, equipmentInstanceId, 0u, inventoryRevision, equipped);
                object[] call =
                {
                    header, ticketSlot, itemKind, equipmentSlot, equipmentInstanceId,
                    inventoryRevision, equipped, player.Identity, player.Inventory,
                    stream, arguments, false,
                };
                return (ShopTransactionResult) Invoke(ShopService, "ServerUseTicket", call);
            }

            public ShopTransactionResult UsePotion(PlayerFixture player, int slot,
                ShopItemKind itemKind, uint requestId, uint inventoryRevision,
                out object stream, out object arguments)
            {
                return UsePotionWithHeader(player, slot, itemKind, requestId,
                    inventoryRevision, RUN_ID, SCENE_REVISION, player.Identity.netId,
                    out stream, out arguments);
            }

            public ShopTransactionResult UsePotionWithHeader(PlayerFixture player, int slot,
                ShopItemKind itemKind, uint requestId, uint inventoryRevision,
                uint runIdValue, uint sceneRevisionValue, uint bindingGeneration,
                out object stream, out object arguments)
            {
                TransactionRequestHeader header = Header(TransactionOperation.UsePotion,
                    requestId, bindingGeneration);
                header.RunIdValue         = runIdValue;
                header.SceneRevisionValue = sceneRevisionValue;
                stream = MakeNested(typeof(ShopTransactionService), "StreamKey",
                    header, player.Participant);
                arguments = MakeShopArguments(0u, itemKind, slot, -1, 0u,
                    0u, inventoryRevision, false);
                object[] call =
                {
                    header, slot, itemKind, inventoryRevision, player.Identity,
                    player.Participant, player.Inventory, player.Status, stream,
                    arguments, false,
                };
                return (ShopTransactionResult) Invoke(ShopService, "ServerUsePotion", call);
            }

            public ShopTransactionResult ReplayShop(object stream, uint requestId,
                object arguments)
            {
                object[] call = { stream, requestId, arguments, default(ShopTransactionResult) };
                Assert.IsTrue((bool) Invoke(ShopService, "TryReplay", call));
                return (ShopTransactionResult) call[3];
            }

            public object MakeShopArguments(uint shopNetId, ShopItemKind itemKind,
                int ticketSlot, int equipmentSlot, uint equipmentInstanceId,
                uint walletRevision, uint inventoryRevision, bool equipped)
            {
                return MakeNested(typeof(ShopTransactionService), "RequestArguments",
                    shopNetId, itemKind, ticketSlot, equipmentSlot, equipmentInstanceId,
                    walletRevision, inventoryRevision, equipped);
            }

            public EquipmentTransferResult Offer(uint requestId, int senderSlot,
                uint equipmentInstanceId, uint senderRevision)
            {
                TransactionRequestHeader header = Header(TransactionOperation.OfferTransfer,
                    requestId, Sender.Identity.netId);
                return (EquipmentTransferResult) Invoke(TransferService, "ServerCreateOffer",
                    header, Recipient.Identity.netId, senderSlot, equipmentInstanceId,
                    senderRevision, Sender.Identity, Sender.Participant, Sender.Inventory);
            }

            public EquipmentTransferResult Accept(EquipmentTransferOfferSnapshot offer,
                uint requestId, uint expectedRecipientRevision,
                out object stream, out object arguments)
            {
                TransactionRequestHeader header = Header(TransactionOperation.AcceptTransfer,
                    requestId, Recipient.Identity.netId);
                stream = MakeNested(typeof(EquipmentTransferService), "StreamKey",
                    header, Recipient.Participant);
                arguments = MakeNested(typeof(EquipmentTransferService), "RequestArguments",
                    0u, offer.OfferId, -1, 0u, 0u,
                    expectedRecipientRevision);
                object[] call =
                {
                    header, offer.OfferId, expectedRecipientRevision,
                    Recipient.Identity, Recipient.Participant, Recipient.Inventory,
                    stream, arguments, false,
                };
                return (EquipmentTransferResult) Invoke(TransferService,
                    "ServerAcceptOffer", call);
            }

            public EquipmentTransferResult ReplayTransfer(object stream, uint requestId,
                object arguments)
            {
                object[] call =
                {
                    stream, requestId, arguments, default(EquipmentTransferResult),
                };
                Assert.IsTrue((bool) Invoke(TransferService, "TryReplay", call));
                return (EquipmentTransferResult) call[3];
            }

            public RunParticipantSnapshot Capture(PlayerFixture player)
            {
                var snapshot = new RunParticipantSnapshot();
                player.Inventory.Capture(snapshot);
                return snapshot;
            }

            public void Dispose()
            {
                NetworkServer.spawned.Remove(SHOP_NET_ID);
                Recipient.Dispose();
                Sender.Dispose();
                UnityEngine.Object.DestroyImmediate(_sessionObject);
                UnityEngine.Object.DestroyImmediate(_shopObject);
                UnityEngine.Object.DestroyImmediate(_directorObject);
                UnityEngine.Object.DestroyImmediate(_runObject);

                SetStaticProperty(typeof(RunSceneDirector),
                    nameof(RunSceneDirector.ServerInstance), _previousDirector);
                SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance),
                    _previousRun);
                RunTimeLocator.Unregister(RunTime);
                if (_previousRunTime != null)
                {
                    RunTimeLocator.Register(_previousRunTime);
                }

                CombatClock.ClearAll();
                GameDB.ResetStatic();
                UnityEngine.Object.DestroyImmediate(_database);
                UnityEngine.Object.DestroyImmediate(_shop);
                UnityEngine.Object.DestroyImmediate(_levelCurve);
                UnityEngine.Object.DestroyImmediate(_talentTree);
                UnityEngine.Object.DestroyImmediate(_arrow);
                UnityEngine.Object.DestroyImmediate(_combat);
                SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection),
                    _previousLocalConnection);
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, _wasServerActive);
            }

            private static TransactionRequestHeader Header(TransactionOperation operation,
                uint requestId, uint netId)
            {
                return new TransactionRequestHeader
                {
                    RunIdValue                   = RUN_ID,
                    SceneRevisionValue           = SCENE_REVISION,
                    NetworkObjectGenerationValue = netId,
                    RequestId                    = requestId,
                    Operation                    = operation,
                };
            }

            private static object MakeNested(Type owner, string name, params object[] args)
            {
                Type nested = owner.GetNestedType(name, BindingFlags.NonPublic);
                ConstructorInfo[] constructors = nested.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < constructors.Length; ++i)
                {
                    if (constructors[i].GetParameters().Length == args.Length)
                    {
                        return constructors[i].Invoke(args);
                    }
                }

                throw new MissingMethodException(owner.FullName, name);
            }

            private static object Invoke(object target, string name, params object[] args)
            {
                MethodInfo method = target.GetType().GetMethod(name, PRIVATE_INSTANCE);
                if (method == null)
                {
                    throw new MissingMethodException(target.GetType().FullName, name);
                }

                return method.Invoke(target, args);
            }

            private static void SetStaticProperty(Type type, string name, object value)
            {
                type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, value);
            }

            internal static void InitializeIdentity(NetworkIdentity identity, uint netId)
            {
                Invoke(identity, "InitializeNetworkBehaviours");
                identity.GetType().GetProperty(nameof(NetworkIdentity.isServer))
                    .SetValue(identity, true);
                identity.GetType().GetProperty(nameof(NetworkIdentity.netId))
                    .SetValue(identity, netId);
            }
        }

        private sealed class PlayerFixture : IDisposable
        {
            private readonly GameObject _object;

            public readonly NetworkIdentity Identity;
            public readonly ParticipantId Participant;
            public readonly PlayerStatus Status;
            public readonly InventoryController Inventory;
            public readonly PlayerWeapon Weapon;
            public readonly TalentController Talent;
            public readonly SupportBuffController Buff;

            public SyncList<InventorySlotSnapshot> Slots =>
                (SyncList<InventorySlotSnapshot>) Field(Inventory, "_slots").GetValue(Inventory);

            public PlayerFixture(string name, uint netId, ParticipantId participant)
            {
                Participant = participant;
                _object = new GameObject(name);
                Identity = _object.AddComponent<NetworkIdentity>();
                Status = _object.AddComponent<PlayerStatus>();
                Weapon = _object.AddComponent<PlayerWeapon>();
                Talent = _object.AddComponent<TalentController>();
                Buff = _object.AddComponent<SupportBuffController>();
                Inventory = _object.AddComponent<InventoryController>();
                typeof(PlayerWeapon).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(Weapon, null);
                typeof(TalentController).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(Talent, null);
                typeof(SupportBuffController).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(Buff, null);
                typeof(InventoryController).GetMethod("Awake", PRIVATE_INSTANCE)
                    .Invoke(Inventory, null);
                Set(Status, "_participant", participant);
                Set(Status, "_lifeState", PlayerLifeState.Alive);
                Set(Weapon, "_weapon", WeaponKind.Sword);
                Set(Weapon, "_hasWeapon", true);
                TransactionFixture.InitializeIdentity(Identity, netId);
                Buff.OnStartServer();
                Inventory.OnStartServer();
                Talent.OnStartServer();
                NetworkServer.spawned.Add(netId, Identity);
            }

            public void SetResources(float health, float maxHealth, float spirit)
            {
                Set(Status, "_health", health);
                Set(Status, "_maxHealth", maxHealth);
                Set(Status, "_spirit", spirit);
                Set(Status, "_serverHealth", health);
                Set(Status, "_serverSpirit", spirit);
                Set(Status, "_serverResourceRevision", 0u);
                Set(Status, "_resourceRevision", 0u);
                Set(Status, "_resourceStateInitialized", true);
                Set(Status, "_resourceTransactionLocked", false);
                Set(Status, "_lifeState", PlayerLifeState.Alive);
            }

            public void Dispose()
            {
                NetworkServer.spawned.Remove(Identity.netId);
                UnityEngine.Object.DestroyImmediate(_object);
            }
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0d;
            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool IsPaused => false;
        }

        private sealed class TestBarrierState : IBarrierState
        {
            private readonly float _healingMultiplier;

            public bool IsActive => true;
            public Vector3 Center => Vector3.zero;
            public float Radius => 1f;

            public TestBarrierState(float healingMultiplier)
            {
                _healingMultiplier = healingMultiplier;
            }

            public bool IsOutside(Vector3 worldPosition) => true;
            public float GetHealingMultiplier(Vector3 worldPosition) => _healingMultiplier;
        }
    }
}
