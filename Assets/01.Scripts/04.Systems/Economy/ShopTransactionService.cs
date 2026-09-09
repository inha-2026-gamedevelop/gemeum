// System
using System;
using System.Collections.Generic;

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
    /// <summary> 구매와 티켓 사용을 서버 원본 하나의 커밋 경계로 조정한다 </summary>
    [DisallowMultipleComponent]
    public class ShopTransactionService : NetworkBehaviour
    {
        private readonly struct StreamKey : IEquatable<StreamKey>
        {
            public readonly uint  RunIdValue;
            public readonly ulong ParticipantValue;
            public readonly uint  NetworkObjectGenerationValue;
            public readonly TransactionOperation Operation;

            public StreamKey(in TransactionRequestHeader header, ParticipantId participant)
            {
                RunIdValue             = header.RunIdValue;
                ParticipantValue       = participant.Value;
                NetworkObjectGenerationValue = header.NetworkObjectGenerationValue;
                Operation              = header.Operation;
            }

            public bool Equals(StreamKey other)
            {
                return ((RunIdValue == other.RunIdValue) &&
                        (ParticipantValue == other.ParticipantValue) &&
                        (NetworkObjectGenerationValue == other.NetworkObjectGenerationValue) &&
                        (Operation == other.Operation));
            }

            public override bool Equals(object obj)
            {
                return ((obj is StreamKey other) && Equals(other));
            }

            public override int GetHashCode()
            {
                return (RunIdValue.GetHashCode() ^ ParticipantValue.GetHashCode() ^
                        NetworkObjectGenerationValue.GetHashCode() ^ ((int) Operation << 24));
            }
        }

        private readonly struct RequestArguments : IEquatable<RequestArguments>
        {
            public readonly uint         ShopNetId;
            public readonly ShopItemKind ItemKind;
            public readonly int          TicketSlot;
            public readonly int          EquipmentSlot;
            public readonly uint         EquipmentInstanceId;
            public readonly uint         WalletRevision;
            public readonly uint         InventoryRevision;
            public readonly bool         TargetIsEquipped;

            public RequestArguments(uint shopNetId, ShopItemKind itemKind, int ticketSlot,
                int equipmentSlot, uint equipmentInstanceId, uint walletRevision,
                uint inventoryRevision, bool targetIsEquipped = false)
            {
                ShopNetId             = shopNetId;
                ItemKind              = itemKind;
                TicketSlot            = ticketSlot;
                EquipmentSlot         = equipmentSlot;
                EquipmentInstanceId   = equipmentInstanceId;
                WalletRevision        = walletRevision;
                InventoryRevision     = inventoryRevision;
                TargetIsEquipped      = targetIsEquipped;
            }

            public bool Equals(RequestArguments other)
            {
                return ((ShopNetId == other.ShopNetId) && (ItemKind == other.ItemKind) &&
                        (TicketSlot == other.TicketSlot) &&
                        (EquipmentSlot == other.EquipmentSlot) &&
                        (EquipmentInstanceId == other.EquipmentInstanceId) &&
                        (WalletRevision == other.WalletRevision) &&
                        (InventoryRevision == other.InventoryRevision) &&
                        (TargetIsEquipped == other.TargetIsEquipped));
            }
        }

        private sealed class RequestRecord
        {
            public uint                  RequestId;
            public RequestArguments      Arguments;
            public ShopTransactionResult Result;
        }

        [SerializeField] private CurrencyManager _wallet;

        [SyncVar] private uint _observedRunIdValue;
        [SyncVar] private uint _observedSceneRevisionValue;

        private readonly Dictionary<StreamKey, RequestRecord> _lastByStream =
            new Dictionary<StreamKey, RequestRecord>();

        private EquipmentGenerator _equipmentGenerator;
        private uint                _clientNextRequestId = 1u;
        private uint                _lastServerRunIdValue;
        private bool                _isCommitting;

        public RunId ObservedRunId => new RunId(_observedRunIdValue);

        public SceneRevision ObservedSceneRevision =>
            new SceneRevision(_observedSceneRevisionValue);

        /// <summary> 구매나 티켓 사용의 서버 결과 </summary>
        public event Action<ShopTransactionResult> TransactionCompleted;

        private void Awake()
        {
            if (_wallet == null)
            {
                _wallet = GetComponent<CurrencyManager>();
            }
        }

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

        /// <summary> 객주 상품 하나를 구매한다. 반환값은 로컬 요청 ID이며 0이면 보내지 못했다 </summary>
        public uint RequestPurchase(uint shopNetId, ShopItemKind itemKind,
            uint expectedWalletRevision, uint expectedInventoryRevision)
        {
            TransactionRequestHeader header = CreateClientHeader(TransactionOperation.Purchase);
            if (!header.IsValid)
            {
                return 0u;
            }

            CmdRequestPurchase(header, shopNetId, itemKind, expectedWalletRevision,
                expectedInventoryRevision);
            return header.RequestId;
        }

        /// <summary> 물약 한 개를 사용해 서버가 확정한 만큼 자원을 회복한다 </summary>
        public uint RequestUsePotion(int slot, ShopItemKind itemKind,
            uint expectedInventoryRevision)
        {
            TransactionRequestHeader header = CreateClientHeader(TransactionOperation.UsePotion);
            if (!header.IsValid)
            {
                return 0u;
            }

            CmdRequestPotion(header, slot, itemKind, expectedInventoryRevision);
            return header.RequestId;
        }

        /// <summary> 재조정권 한 장을 써서 같은 ID와 등급의 장비를 다시 굴린다 </summary>
        public uint RequestRerollTicket(int ticketSlot, int equipmentSlot,
            uint equipmentInstanceId, uint expectedInventoryRevision)
        {
            return RequestTicket(TransactionOperation.RerollTicket, ticketSlot,
                equipmentSlot, equipmentInstanceId, expectedInventoryRevision, false);
        }

        /// <summary> 상승권 한 장을 써서 같은 ID의 장비를 한 등급 올린다 </summary>
        public uint RequestUpgradeTicket(int ticketSlot, int equipmentSlot,
            uint equipmentInstanceId, uint expectedInventoryRevision)
        {
            return RequestTicket(TransactionOperation.UpgradeTicket, ticketSlot,
                equipmentSlot, equipmentInstanceId, expectedInventoryRevision, false);
        }

        /// <summary> 재조정권 한 장을 써서 장착 중인 실제 장비를 다시 굴린다 </summary>
        public uint RequestRerollEquippedTicket(int ticketSlot,
            uint equipmentInstanceId, uint expectedInventoryRevision)
        {
            return RequestTicket(TransactionOperation.RerollTicket, ticketSlot,
                -1, equipmentInstanceId, expectedInventoryRevision, true);
        }

        /// <summary> 상승권 한 장을 써서 장착 중인 실제 장비를 한 등급 올린다 </summary>
        public uint RequestUpgradeEquippedTicket(int ticketSlot,
            uint equipmentInstanceId, uint expectedInventoryRevision)
        {
            return RequestTicket(TransactionOperation.UpgradeTicket, ticketSlot,
                -1, equipmentInstanceId, expectedInventoryRevision, true);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestPurchase(TransactionRequestHeader header, uint shopNetId,
            ShopItemKind itemKind, uint expectedWalletRevision, uint expectedInventoryRevision,
            NetworkConnectionToClient sender = null)
        {
            RequestArguments arguments = new RequestArguments(shopNetId, itemKind, -1, -1,
                0u, expectedWalletRevision, expectedInventoryRevision);

            if (!TransactionServiceUtility.TryGetPlayer(sender, out NetworkIdentity player,
                    out ParticipantId participant, out InventoryController inventory))
            {
                SendResult(sender, MakeResult(header, itemKind,
                    TransactionRejection.InvalidRequest, default));
                return;
            }

            TransactionRejection headerResult = TransactionServiceUtility.ValidateHeader(
                sender, header, TransactionOperation.Purchase);
            if (headerResult != TransactionRejection.None)
            {
                SendResult(sender, MakeResult(header, itemKind, headerResult, default));
                return;
            }

            StreamKey stream = new StreamKey(header, participant);
            if (TryReplay(stream, header.RequestId, arguments, out ShopTransactionResult replay))
            {
                SendResult(sender, replay);
                return;
            }

            if (_isCommitting || inventory.IsTransactionLocked ||
                ((_wallet != null) && _wallet.IsTransactionLocked))
            {
                SendResult(sender, MakeResult(header, itemKind,
                    TransactionRejection.InvalidState, default));
                return;
            }

            ShopTransactionResult result = ServerPurchase(header, shopNetId, itemKind,
                expectedWalletRevision, expectedInventoryRevision, player, inventory,
                stream, arguments, out bool remembered);
            if (!remembered)
            {
                result.InventoryRevision = inventory.Revision;
                Remember(stream, header.RequestId, arguments, result);
            }

            SendResult(sender, result);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestPotion(TransactionRequestHeader header, int slot,
            ShopItemKind itemKind, uint expectedInventoryRevision,
            NetworkConnectionToClient sender = null)
        {
            RequestArguments arguments = new RequestArguments(0u, itemKind, slot, -1,
                0u, 0u, expectedInventoryRevision);

            if (!TransactionServiceUtility.TryGetPlayer(sender, out NetworkIdentity player,
                    out ParticipantId participant, out InventoryController inventory))
            {
                SendResult(sender, MakeResult(header, itemKind,
                    TransactionRejection.InvalidRequest, default));
                return;
            }

            TransactionRejection headerResult = TransactionServiceUtility.ValidateHeader(
                sender, header, TransactionOperation.UsePotion);
            if (headerResult != TransactionRejection.None)
            {
                SendResult(sender, MakeResult(header, itemKind, headerResult, default));
                return;
            }

            StreamKey stream = new StreamKey(header, participant);
            if (TryReplay(stream, header.RequestId, arguments, out ShopTransactionResult replay))
            {
                SendResult(sender, replay);
                return;
            }

            var resourceTarget = player.GetComponent<IResourceTransactionTarget>();
            if ((resourceTarget == null) || _isCommitting || inventory.IsTransactionLocked ||
                resourceTarget.IsResourceTransactionLocked)
            {
                SendResult(sender, MakeResult(header, itemKind,
                    TransactionRejection.InvalidState, default));
                return;
            }

            ShopTransactionResult result = ServerUsePotion(header, slot, itemKind,
                expectedInventoryRevision, player, participant, inventory, resourceTarget,
                stream, arguments, out bool remembered);
            if (!remembered)
            {
                result.InventoryRevision = inventory.Revision;
                result.ResourceRevision  = resourceTarget.ResourceRevision;
                Remember(stream, header.RequestId, arguments, result);
            }

            SendResult(sender, result);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestTicket(TransactionRequestHeader header, int ticketSlot,
            int equipmentSlot, uint equipmentInstanceId, uint expectedInventoryRevision,
            bool targetIsEquipped,
            NetworkConnectionToClient sender = null)
        {
            ShopItemKind ticketKind = header.Operation == TransactionOperation.RerollTicket
                ? ShopItemKind.RerollTicket : ShopItemKind.UpgradeTicket;
            RequestArguments arguments = new RequestArguments(0u, ticketKind, ticketSlot,
                equipmentSlot, equipmentInstanceId, 0u, expectedInventoryRevision,
                targetIsEquipped);

            if (!TransactionServiceUtility.TryGetPlayer(sender, out NetworkIdentity player,
                    out ParticipantId participant, out InventoryController inventory))
            {
                SendResult(sender, MakeResult(header, ticketKind,
                    TransactionRejection.InvalidRequest, default));
                return;
            }

            TransactionRejection headerResult = TransactionServiceUtility.ValidateHeader(
                sender, header, header.Operation);
            if ((headerResult == TransactionRejection.None) &&
                (header.Operation != TransactionOperation.RerollTicket) &&
                (header.Operation != TransactionOperation.UpgradeTicket))
            {
                headerResult = TransactionRejection.InvalidRequest;
            }

            if (headerResult != TransactionRejection.None)
            {
                SendResult(sender, MakeResult(header, ticketKind, headerResult, default));
                return;
            }

            StreamKey stream = new StreamKey(header, participant);
            if (TryReplay(stream, header.RequestId, arguments, out ShopTransactionResult replay))
            {
                SendResult(sender, replay);
                return;
            }

            if (_isCommitting || inventory.IsTransactionLocked)
            {
                SendResult(sender, MakeResult(header, ticketKind,
                    TransactionRejection.InvalidState, default));
                return;
            }

            ShopTransactionResult result = ServerUseTicket(header, ticketSlot,
                ticketKind, equipmentSlot, equipmentInstanceId,
                expectedInventoryRevision, targetIsEquipped, player, inventory,
                stream, arguments, out bool remembered);
            if (!remembered)
            {
                result.InventoryRevision = inventory.Revision;
                Remember(stream, header.RequestId, arguments, result);
            }

            SendResult(sender, result);
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

        private uint RequestTicket(TransactionOperation operation, int ticketSlot,
            int equipmentSlot, uint equipmentInstanceId, uint expectedInventoryRevision,
            bool targetIsEquipped)
        {
            TransactionRequestHeader header = CreateClientHeader(operation);
            if (!header.IsValid)
            {
                return 0u;
            }

            CmdRequestTicket(header, ticketSlot, equipmentSlot,
                equipmentInstanceId, expectedInventoryRevision, targetIsEquipped);
            return header.RequestId;
        }

        private TransactionRequestHeader CreateClientHeader(TransactionOperation operation)
        {
            if ((!NetworkClient.active) || (NetworkClient.localPlayer == null) ||
                (_clientNextRequestId == 0u))
            {
                return default;
            }

            uint requestId = _clientNextRequestId;
            if (_clientNextRequestId == uint.MaxValue)
            {
                _clientNextRequestId = 0u;
            }
            else
            {
                ++_clientNextRequestId;
            }

            return new TransactionRequestHeader
            {
                RunIdValue             = _observedRunIdValue,
                SceneRevisionValue     = _observedSceneRevisionValue,
                NetworkObjectGenerationValue = NetworkClient.localPlayer.netId,
                RequestId              = requestId,
                Operation              = operation,
            };
        }

        private bool TryReplay(in StreamKey stream, uint requestId,
            in RequestArguments arguments, out ShopTransactionResult result)
        {
            result = default;
            if (!_lastByStream.TryGetValue(stream, out RequestRecord record))
            {
                return false;
            }

            if (requestId == record.RequestId)
            {
                result = record.Arguments.Equals(arguments)
                    ? record.Result
                    : MakeSequenceFailure(requestId, stream.Operation,
                        TransactionRejection.DuplicateRequest);
                return true;
            }

            if (!TransactionRequestRules.IsNewer(requestId, record.RequestId))
            {
                result = MakeSequenceFailure(requestId, stream.Operation,
                    TransactionRejection.StaleRequest);
                return true;
            }

            return false;
        }

        private void Remember(in StreamKey stream, uint requestId,
            in RequestArguments arguments, in ShopTransactionResult result)
        {
            _lastByStream[stream] = new RequestRecord
            {
                RequestId = requestId,
                Arguments = arguments,
                Result    = result,
            };
        }

        private ShopTransactionResult MakeResult(in TransactionRequestHeader header,
            ShopItemKind itemKind, TransactionRejection rejection,
            in WeaponCombatSnapshot equipment)
        {
            return new ShopTransactionResult
            {
                RequestId        = header.RequestId,
                Operation        = header.Operation,
                Rejection        = rejection,
                ItemKind         = itemKind,
                WalletRevision   = _wallet != null ? _wallet.Revision : 0u,
                InventoryRevision = 0u,
                Equipment        = equipment,
            };
        }

        private ShopTransactionResult MakeSequenceFailure(uint requestId,
            TransactionOperation operation, TransactionRejection rejection)
        {
            return new ShopTransactionResult
            {
                RequestId      = requestId,
                Operation      = operation,
                Rejection      = rejection,
                WalletRevision = _wallet != null ? _wallet.Revision : 0u,
            };
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

        [TargetRpc]
        private void TargetTransactionResult(NetworkConnection target,
            ShopTransactionResult result)
        {
            TransactionCompleted?.Invoke(result);
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
    }
}
