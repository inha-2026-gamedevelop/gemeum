// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class EquipmentTransferService
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
        private void CmdRequestOffer(TransactionRequestHeader header, uint recipientNetId,
            int senderSlot, uint equipmentInstanceId, uint expectedSenderRevision,
            NetworkConnectionToClient sender = null)
        {
            RequestArguments arguments = new RequestArguments(recipientNetId, 0u,
                senderSlot, equipmentInstanceId, expectedSenderRevision, 0u);

            if (!TransactionServiceUtility.TryGetPlayer(sender, out NetworkIdentity senderIdentity,
                    out ParticipantId senderParticipant, out InventoryController senderInventory))
            {
                SendResult(sender, MakeFailure(header, TransactionRejection.InvalidRequest));
                return;
            }

            TransactionRejection headerResult = TransactionServiceUtility.ValidateHeader(sender,
                header, TransactionOperation.OfferTransfer);
            if (headerResult != TransactionRejection.None)
            {
                SendResult(sender, MakeFailure(header, headerResult));
                return;
            }

            StreamKey stream = new StreamKey(header, senderParticipant);
            if (TryReplay(stream, header.RequestId, arguments, out EquipmentTransferResult replay))
            {
                SendResult(sender, replay);
                return;
            }

            if (_isPublishing)
            {
                SendResult(sender, MakeFailure(header, TransactionRejection.InvalidState));
                return;
            }

            EquipmentTransferResult result = ServerCreateOffer(header, recipientNetId,
                senderSlot, equipmentInstanceId, expectedSenderRevision, senderIdentity,
                senderParticipant, senderInventory);
            Remember(stream, header.RequestId, arguments, result);
            SendResult(sender, result);

            if (result.Succeeded &&
                NetworkServer.spawned.TryGetValue(result.Offer.RecipientNetId,
                    out NetworkIdentity recipientIdentity) &&
                (recipientIdentity.connectionToClient != null))
            {
                SendResult(recipientIdentity.connectionToClient, result);
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestDecision(TransactionRequestHeader header, uint offerId,
            uint expectedRecipientRevision, NetworkConnectionToClient sender = null)
        {
            RequestArguments arguments = new RequestArguments(0u, offerId, -1,
                0u, 0u, expectedRecipientRevision);

            if (!TransactionServiceUtility.TryGetPlayer(sender, out NetworkIdentity requester,
                    out ParticipantId participant, out InventoryController requesterInventory))
            {
                SendResult(sender, MakeFailure(header, TransactionRejection.InvalidRequest));
                return;
            }

            TransactionRejection headerResult = TransactionServiceUtility.ValidateHeader(sender,
                header, header.Operation);
            if ((headerResult == TransactionRejection.None) &&
                (header.Operation != TransactionOperation.AcceptTransfer) &&
                (header.Operation != TransactionOperation.RejectTransfer) &&
                (header.Operation != TransactionOperation.CancelTransfer))
            {
                headerResult = TransactionRejection.InvalidRequest;
            }

            if (headerResult != TransactionRejection.None)
            {
                SendResult(sender, MakeFailure(header, headerResult));
                return;
            }

            StreamKey stream = new StreamKey(header, participant);
            if (TryReplay(stream, header.RequestId, arguments, out EquipmentTransferResult replay))
            {
                SendResult(sender, replay);
                return;
            }

            if (_isPublishing)
            {
                SendResult(sender, MakeFailure(header, TransactionRejection.InvalidState));
                return;
            }

            bool remembered;
            EquipmentTransferResult result;
            if (header.Operation == TransactionOperation.AcceptTransfer)
            {
                result = ServerAcceptOffer(header, offerId, expectedRecipientRevision,
                    requester, participant, requesterInventory, stream, arguments,
                    out remembered);
            }
            else
            {
                result = ServerCloseOffer(header, offerId, requester, participant,
                    header.Operation == TransactionOperation.RejectTransfer
                        ? EquipmentTransferState.Rejected
                        : EquipmentTransferState.Cancelled);
                remembered = false;
            }

            if (!remembered)
            {
                Remember(stream, header.RequestId, arguments, result);
            }

            SendResult(sender, result);
            SendCounterpartyResult(requester.netId, result);
        }
    }
}
