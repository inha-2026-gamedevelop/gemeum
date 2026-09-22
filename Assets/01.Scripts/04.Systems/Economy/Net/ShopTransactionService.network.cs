// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class ShopTransactionService
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private uint _observedRunIdValue;
        [SyncVar] private uint _observedSceneRevisionValue;

        /****************************************
        *               Commands
        ****************************************/

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

            SendResult(sender, ServerRequestPotion(player.netId, header, slot,
                itemKind, expectedInventoryRevision));
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
    }
}
