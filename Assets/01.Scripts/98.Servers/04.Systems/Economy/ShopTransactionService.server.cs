// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    public partial class ShopTransactionService
    {
        public override void OnStartServer()
        {
            _lastByStream.Clear();
            _isCommitting = false;
            ServerRefreshContext();

            if (GameDB.IsInitialized && (GameDB.Equipment != null) &&
                (GameDB.DropTable != null))
            {
                _equipmentGenerator = new EquipmentGenerator(GameDB.Equipment,
                    GameDB.DropTable, new System.Random());
            }
            else
            {
                _equipmentGenerator = null;
                Debug.LogError("[ShopTransactionService] 장비 생성 데이터가 없다", this);
            }
        }

        public override void OnStopServer()
        {
            _lastByStream.Clear();
            _equipmentGenerator = null;
            _isCommitting = false;
        }

        [ServerCallback]
        private void Update()
        {
            ServerRefreshContext();
        }

        [Server]
        private ShopTransactionResult ServerPurchase(in TransactionRequestHeader header,
            uint shopNetId, ShopItemKind itemKind, uint expectedWalletRevision,
            uint expectedInventoryRevision, NetworkIdentity player,
            InventoryController inventory, in StreamKey stream,
            in RequestArguments arguments, out bool remembered)
        {
            remembered = false;

            if ((_wallet == null) || (!GameDB.IsInitialized) ||
                (!NetworkServer.spawned.TryGetValue(shopNetId, out NetworkIdentity shopIdentity)))
            {
                return MakeResult(header, itemKind, TransactionRejection.InvalidState, default);
            }

            var shop = shopIdentity.GetComponent<ShopInteractionPoint>();
            if (shop == null)
            {
                return MakeResult(header, itemKind, TransactionRejection.InvalidRequest, default);
            }

            TransactionRejection interaction = TransactionRequestRules.FromInteraction(
                shop.ServerEvaluate(player.netId));
            if (interaction != TransactionRejection.None)
            {
                return MakeResult(header, itemKind, interaction, default);
            }

            if (!GameDB.Shop.TryGetItem(itemKind, out ShopSO.ItemEntry item) ||
                (item.Price <= 0) || (item.MaxStack <= 0))
            {
                return MakeResult(header, itemKind, TransactionRejection.InvalidItem, default);
            }

            if (_wallet.Revision != expectedWalletRevision)
            {
                return MakeResult(header, itemKind,
                    TransactionRejection.StaleWalletRevision, default);
            }

            if (_wallet.Balance < item.Price)
            {
                return MakeResult(header, itemKind,
                    TransactionRejection.InsufficientCurrency, default);
            }

            if (!_wallet.TryPrepareSpend(item.Price, out PartyWalletSpendPlan walletPlan))
            {
                TransactionRejection rejection = _wallet.Revision == uint.MaxValue
                    ? TransactionRejection.RevisionExhausted
                    : TransactionRejection.InvalidState;
                return MakeResult(header, itemKind, rejection, default);
            }

            InventoryResult inventoryResult = inventory.ServerPrepareStoreConsumable(
                itemKind, 1, item.CarryLimit, item.MaxStack, expectedInventoryRevision,
                out InventoryCommitPlan inventoryPlan);
            if (inventoryResult != InventoryResult.Success)
            {
                return MakeResult(header, itemKind,
                    TransactionRequestRules.FromInventory(inventoryResult), default);
            }

            TransactionRejection recheck = TransactionRequestRules.FromInteraction(
                shop.ServerEvaluate(player.netId));
            if (recheck != TransactionRejection.None)
            {
                return MakeResult(header, itemKind, recheck, default);
            }

            if (!_wallet.ServerTryAcquireSpend(walletPlan))
            {
                return MakeResult(header, itemKind,
                    TransactionRejection.StaleWalletRevision, default);
            }

            if (!inventory.ServerTryAcquireTransaction(inventoryPlan))
            {
                _wallet.ServerReleaseTransaction();
                return MakeResult(header, itemKind,
                    TransactionRejection.StaleInventoryRevision, default);
            }

            _isCommitting = true;
            try
            {
                _wallet.ServerApplySpend(walletPlan);
                inventory.ServerApplyTransaction(inventoryPlan);

                ShopTransactionResult success = MakeResult(header, itemKind,
                    TransactionRejection.None, default);
                success.WalletRevision    = _wallet.Revision;
                success.InventoryRevision = inventory.Revision;
                Remember(stream, header.RequestId, arguments, success);
                remembered = true;

                TryPublishWallet("구매");
                TryPublishInventory(inventory, inventoryPlan, "구매");

                return success;
            }
            finally
            {
                inventory.ServerReleaseTransaction();
                _wallet.ServerReleaseTransaction();
                _isCommitting = false;
            }
        }

        [Server]
        private ShopTransactionResult ServerUsePotion(in TransactionRequestHeader header,
            int slot, ShopItemKind itemKind, uint expectedInventoryRevision,
            NetworkIdentity player, ParticipantId participant,
            InventoryController inventory, IResourceTransactionTarget resourceTarget,
            in StreamKey stream, in RequestArguments arguments, out bool remembered)
        {
            remembered = false;

            TransactionRejection state = ValidatePotionContext(header, player,
                resourceTarget);
            if (state != TransactionRejection.None)
            {
                return MakeResult(header, itemKind, state, default);
            }

            if ((!GameDB.IsInitialized) || (GameDB.Shop == null) ||
                (!TryGetPotionResource(itemKind, out RecoveryResourceKind resourceKind)) ||
                (!GameDB.Shop.TryGetItem(itemKind, out ShopSO.ItemEntry item)) ||
                float.IsNaN(item.EffectValue) || float.IsInfinity(item.EffectValue) ||
                (item.EffectValue <= Constants.Combat.HEALTH_EPSILON))
            {
                return MakeResult(header, itemKind,
                    TransactionRejection.InvalidItem, default);
            }

            ResourceTransactionResult resourceResult = resourceTarget.ServerPrepareRecovery(
                resourceKind, participant, item.EffectValue, out ResourceCommitPlan resourcePlan);
            if (resourceResult != ResourceTransactionResult.Success)
            {
                return MakeResult(header, itemKind, FromResource(resourceResult), default);
            }

            InventoryResult inventoryResult = inventory.ServerPrepareConsume(slot, itemKind, 1,
                expectedInventoryRevision, out InventoryCommitPlan inventoryPlan);
            if (inventoryResult != InventoryResult.Success)
            {
                return MakeResult(header, itemKind,
                    TransactionRequestRules.FromInventory(inventoryResult), default);
            }

            state = ValidatePotionContext(header, player, resourceTarget);
            if (state != TransactionRejection.None)
            {
                return MakeResult(header, itemKind, state, default);
            }

            if (!inventory.ServerTryAcquireTransaction(inventoryPlan))
            {
                return MakeResult(header, itemKind,
                    TransactionRejection.StaleInventoryRevision, default);
            }

            if (!resourceTarget.ServerTryAcquireResourceTransaction(resourcePlan))
            {
                inventory.ServerReleaseTransaction();
                return MakeResult(header, itemKind,
                    TransactionRejection.InvalidState, default);
            }

            _isCommitting = true;
            try
            {
                inventory.ServerApplyTransaction(inventoryPlan);
                resourceTarget.ServerApplyResourceTransaction(resourcePlan);

                ShopTransactionResult success = MakeResult(header, itemKind,
                    TransactionRejection.None, default);
                success.InventoryRevision = inventory.Revision;
                success.ResourceRevision  = resourceTarget.ResourceRevision;
                success.AppliedAmount     = resourcePlan.AppliedAmount;
                Remember(stream, header.RequestId, arguments, success);
                remembered = true;

                TryPublishResource(resourceTarget, resourcePlan, "물약");
                TryPublishInventory(inventory, inventoryPlan, "물약");

                return success;
            }
            finally
            {
                resourceTarget.ServerReleaseResourceTransaction();
                inventory.ServerReleaseTransaction();
                _isCommitting = false;
            }
        }

        [Server]
        private ShopTransactionResult ServerUseTicket(in TransactionRequestHeader header,
            int ticketSlot, ShopItemKind ticketKind, int equipmentSlot,
            uint equipmentInstanceId, uint expectedInventoryRevision,
            bool targetIsEquipped, NetworkIdentity player, InventoryController inventory,
            in StreamKey stream, in RequestArguments arguments, out bool remembered)
        {
            remembered = false;

            if (_equipmentGenerator == null)
            {
                return MakeResult(header, ticketKind, TransactionRejection.InvalidState, default);
            }

            TransactionRejection state = TransactionServiceUtility.EvaluatePlayer(player);
            if (state != TransactionRejection.None)
            {
                return MakeResult(header, ticketKind, state, default);
            }

            WeaponCombatSnapshot current;
            InventoryResult queryResult = targetIsEquipped
                ? inventory.ServerTryGetEquippedEquipment(equipmentInstanceId,
                    expectedInventoryRevision, out current)
                : inventory.ServerTryGetEquipment(equipmentSlot, equipmentInstanceId,
                    expectedInventoryRevision, out current);
            if (queryResult != InventoryResult.Success)
            {
                return MakeResult(header, ticketKind,
                    TransactionRequestRules.FromInventory(queryResult), default);
            }

            if ((ticketKind == ShopItemKind.UpgradeTicket) &&
                (current.Grade == ItemGrade.Divine))
            {
                return MakeResult(header, ticketKind, TransactionRejection.MaxGrade, current);
            }

            WeaponCombatSnapshot replacement;
            try
            {
                replacement = ticketKind == ShopItemKind.RerollTicket
                    ? _equipmentGenerator.Reroll(current)
                    : _equipmentGenerator.Upgrade(current);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShopTransactionService] 장비 서비스 롤 실패: {e.Message}", this);
                return MakeResult(header, ticketKind, TransactionRejection.InternalError, current);
            }

            InventoryCommitPlan inventoryPlan;
            EquippedEquipmentWrite equippedPlan = default;
            InventoryResult prepareResult = targetIsEquipped
                ? inventory.ServerPrepareEquippedTicketUse(ticketSlot, ticketKind,
                    equipmentInstanceId, replacement, expectedInventoryRevision,
                    out inventoryPlan, out equippedPlan)
                : inventory.ServerPrepareTicketUse(ticketSlot, ticketKind, equipmentSlot,
                    equipmentInstanceId, replacement, expectedInventoryRevision,
                    out inventoryPlan);
            if (prepareResult != InventoryResult.Success)
            {
                return MakeResult(header, ticketKind,
                    TransactionRequestRules.FromInventory(prepareResult), current);
            }

            state = TransactionServiceUtility.EvaluatePlayer(player);
            if (state != TransactionRejection.None)
            {
                return MakeResult(header, ticketKind, state, current);
            }

            if (targetIsEquipped &&
                (!inventory.ServerCanApplyEquippedTransaction(equippedPlan)))
            {
                return MakeResult(header, ticketKind,
                    TransactionRejection.StaleInventoryRevision, current);
            }

            if (!inventory.ServerTryAcquireTransaction(inventoryPlan))
            {
                return MakeResult(header, ticketKind,
                    TransactionRejection.StaleInventoryRevision, current);
            }

            _isCommitting = true;
            try
            {
                inventory.ServerApplyTransaction(inventoryPlan);

                ShopTransactionResult success = MakeResult(header, ticketKind,
                    TransactionRejection.None, replacement);
                success.InventoryRevision = inventory.Revision;
                success.WalletRevision    = _wallet != null ? _wallet.Revision : 0u;
                // 장착 SyncVar hook도 재진입할 수 있으므로 결과와 순서를 먼저 확정한다.
                Remember(stream, header.RequestId, arguments, success);
                remembered = true;

                if (targetIsEquipped)
                {
                    inventory.ServerApplyEquippedTransaction(equippedPlan);
                    inventory.ServerPublishEquippedTransaction(equippedPlan);
                }

                TryPublishInventory(inventory, inventoryPlan, "티켓");

                return success;
            }
            finally
            {
                inventory.ServerReleaseTransaction();
                _isCommitting = false;
            }
        }

        [Server]
        private void TryPublishWallet(string operation)
        {
            try
            {
                _wallet.ServerPublishTransaction();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShopTransactionService] {operation} 지갑 공개 실패: {e}", this);
            }
        }

        [Server]
        private void TryPublishInventory(InventoryController inventory,
            in InventoryCommitPlan plan, string operation)
        {
            try
            {
                inventory.ServerPublishTransaction(plan);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShopTransactionService] {operation} 인벤토리 공개 실패: {e}",
                    this);
            }
        }

        [Server]
        private void TryPublishResource(IResourceTransactionTarget target,
            in ResourceCommitPlan plan, string operation)
        {
            try
            {
                target.ServerPublishResourceTransaction(plan);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShopTransactionService] {operation} 자원 공개 실패: {e}", this);
            }
        }

        [Server]
        private void SendResult(NetworkConnectionToClient target,
            in ShopTransactionResult result)
        {
            if (target != null)
            {
                TargetTransactionResult(target, result);
            }
        }

        [Server]
        private void ServerRefreshContext()
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            uint runIdValue = run != null ? run.RunId.Value : 0u;

            if ((_lastServerRunIdValue != 0u) &&
                (_lastServerRunIdValue != runIdValue))
            {
                _lastByStream.Clear();
            }

            _lastServerRunIdValue       = runIdValue;
            _observedRunIdValue         = runIdValue;
            _observedSceneRevisionValue = director != null ? director.Revision.Value : 0u;
        }

        private static bool TryGetPotionResource(ShopItemKind itemKind,
            out RecoveryResourceKind resourceKind)
        {
            switch (itemKind)
            {
                case ShopItemKind.HealthPotion:
                    resourceKind = RecoveryResourceKind.Health;
                    return true;
                case ShopItemKind.SpiritPotion:
                    resourceKind = RecoveryResourceKind.Spirit;
                    return true;
                default:
                    resourceKind = RecoveryResourceKind.None;
                    return false;
            }
        }

        private static TransactionRejection FromResource(ResourceTransactionResult result)
        {
            switch (result)
            {
                case ResourceTransactionResult.Success:
                    return TransactionRejection.None;
                case ResourceTransactionResult.Dead:
                    return TransactionRejection.Dead;
                case ResourceTransactionResult.ResourceFull:
                    return TransactionRejection.ResourceFull;
                case ResourceTransactionResult.RecoveryBlocked:
                    return TransactionRejection.RecoveryBlocked;
                case ResourceTransactionResult.RevisionExhausted:
                    return TransactionRejection.RevisionExhausted;
                case ResourceTransactionResult.StaleRevision:
                case ResourceTransactionResult.InvalidState:
                    return TransactionRejection.InvalidState;
                default:
                    return TransactionRejection.InvalidRequest;
            }
        }

        private static TransactionRejection ValidatePotionContext(
            in TransactionRequestHeader header, NetworkIdentity player,
            IResourceTransactionTarget resourceTarget)
        {
            if ((player == null) || (resourceTarget == null))
            {
                return TransactionRejection.InvalidRequest;
            }

            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (director == null) || (!run.RunId.IsValid) ||
                (!director.Revision.IsValid))
            {
                return TransactionRejection.InvalidState;
            }

            TransactionRejection headerResult = TransactionRequestRules.ValidateHeader(header,
                TransactionOperation.UsePotion, run.RunId, director.Revision, player.netId,
                run.IsPaused, RunSceneDirector.IsServerTransitioning);
            if (headerResult != TransactionRejection.None)
            {
                return headerResult;
            }

            if ((run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result))
            {
                return TransactionRejection.InvalidState;
            }

            var life = player.GetComponent<IDamageable>();
            if (life == null)
            {
                return TransactionRejection.InvalidRequest;
            }

            return life.IsDead ? TransactionRejection.Dead : TransactionRejection.None;
        }
    }
}
