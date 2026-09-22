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
    public partial class EquipmentTransferService
    {
        [Server]
        private void ServerAcceptBotOffer(uint offerId)
        {
            if (!_offers.TryGetValue(offerId, out OfferRecord offer) ||
                offer.RecipientParticipant.Kind != ParticipantKind.Bot || offer.RecipientIdentity == null ||
                offer.RecipientIdentity.connectionToClient != null) { return; }
            var slots = new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
            if (!offer.SenderInventory.ServerCopySlots(slots)) { return; }
            if (!offer.RecipientIdentity.TryGetComponent(out Geumeum.Combat.PlayerWeapon weapon) ||
                slots[offer.SenderSlot].Equipment.WeaponKind != weapon.Weapon) { return; }
            var header = new TransactionRequestHeader
            { RunIdValue = offer.RunIdValue, SceneRevisionValue = offer.SceneRevisionValue,
                NetworkObjectGenerationValue = offer.RecipientIdentity.netId,
                RequestId = offerId, Operation = TransactionOperation.AcceptTransfer };
            var stream = new StreamKey(header, offer.RecipientParticipant);
            var arguments = new RequestArguments(0u, offerId, -1, 0u, 0u, offer.RecipientInventory.Revision);
            var result = ServerAcceptOffer(header, offerId, offer.RecipientInventory.Revision,
                offer.RecipientIdentity, offer.RecipientParticipant, offer.RecipientInventory, stream, arguments, out bool remembered);
            if (!remembered) { Remember(stream, header.RequestId, arguments, result); }
            SendResult(offer.SenderIdentity.connectionToClient, result);
        }
        public override void OnStartServer()
        {
            ClearServerState();
            ServerRefreshContext();
        }

        public override void OnStopServer()
        {
            ClearServerState();
        }

        [ServerCallback]
        private void Update()
        {
            ServerRefreshContext();
            ServerEndInvalidOffers();
        }

        [Server]
        private EquipmentTransferResult ServerCreateOffer(
            in TransactionRequestHeader header, uint recipientNetId, int senderSlot,
            uint equipmentInstanceId, uint expectedSenderRevision,
            NetworkIdentity senderIdentity,
            ParticipantId senderParticipant, InventoryController senderInventory)
        {
            if (!TransactionServiceUtility.TryGetPlayer(recipientNetId,
                    out NetworkIdentity recipientIdentity,
                    out ParticipantId recipientParticipant,
                    out InventoryController recipientInventory))
            {
                return MakeFailure(header, TransactionRejection.InvalidRequest);
            }

            IDamageable senderLife = senderIdentity.GetComponent<IDamageable>();
            IDamageable recipientLife = recipientIdentity.GetComponent<IDamageable>();
            IGrowthParticipant senderParticipantSource = senderLife as IGrowthParticipant;
            IGrowthParticipant recipientParticipantSource = recipientLife as IGrowthParticipant;
            if ((senderParticipantSource == null) || (recipientParticipantSource == null) ||
                (senderParticipantSource.Participant != senderParticipant) ||
                (recipientParticipantSource.Participant != recipientParticipant))
            {
                return MakeFailure(header, TransactionRejection.InvalidState);
            }

            TransactionRejection state = TransactionServiceUtility.EvaluatePlayers(
                senderIdentity, senderLife, recipientIdentity, recipientLife);
            if (state != TransactionRejection.None)
            {
                return MakeFailure(header, state);
            }

            if (senderInventory.IsTransactionLocked || recipientInventory.IsTransactionLocked)
            {
                return MakeFailure(header, TransactionRejection.InvalidState);
            }

            ServerEndOfferForParticipantIfInvalid(senderParticipant);
            ServerEndOfferForParticipantIfInvalid(recipientParticipant);
            if (_activeOfferByParticipant.ContainsKey(senderParticipant) ||
                _activeOfferByParticipant.ContainsKey(recipientParticipant))
            {
                return MakeFailure(header, TransactionRejection.OfferConflict);
            }

            InventoryResult takeResult = senderInventory.ServerPrepareTakeEquipment(senderSlot,
                equipmentInstanceId, expectedSenderRevision,
                out WeaponCombatSnapshot equipment, out _);
            if (takeResult != InventoryResult.Success)
            {
                return MakeFailure(header, TransactionRequestRules.FromInventory(takeResult));
            }

            // 수신자 인벤토리는 owner-only다. 송신자 입력을 받지 않고 서버 현재 판을 캡처한다
            uint recipientRevision = recipientInventory.Revision;
            InventoryResult storeResult = recipientInventory.ServerPrepareStoreEquipment(equipment,
                recipientRevision, out _);
            if (storeResult != InventoryResult.Success)
            {
                return MakeFailure(header, TransactionRequestRules.FromInventory(storeResult));
            }

            float duration = GameDB.Combat.EquipmentTransferOfferDuration;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || (duration <= 0f))
            {
                return MakeFailure(header, TransactionRejection.InvalidState);
            }

            uint offerId = TakeOfferId();
            if (offerId == 0u)
            {
                return MakeFailure(header, TransactionRejection.RevisionExhausted);
            }

            var snapshot = new EquipmentTransferOfferSnapshot
            {
                OfferId                    = offerId,
                State                      = EquipmentTransferState.Offered,
                SenderNetId                = senderIdentity.netId,
                RecipientNetId             = recipientIdentity.netId,
                EquipmentInstanceId        = equipmentInstanceId,
                SenderInventoryRevision    = expectedSenderRevision,
                RecipientInventoryRevision = recipientRevision,
                ExpiresAtGameplayTime       =
                    RunTimeLocator.Current.GameplayElapsedSeconds + duration,
            };

            _offers.Add(offerId, new OfferRecord
            {
                Snapshot             = snapshot,
                SenderParticipant    = senderParticipant,
                RecipientParticipant = recipientParticipant,
                SenderIdentity       = senderIdentity,
                RecipientIdentity    = recipientIdentity,
                SenderInventory      = senderInventory,
                RecipientInventory   = recipientInventory,
                SenderLife           = senderLife,
                RecipientLife        = recipientLife,
                SenderParticipantSource = senderParticipantSource,
                RecipientParticipantSource = recipientParticipantSource,
                SenderSlot           = senderSlot,
                RunIdValue           = header.RunIdValue,
                SceneRevisionValue   = header.SceneRevisionValue,
            });
            _activeOfferByParticipant.Add(senderParticipant, offerId);
            _activeOfferByParticipant.Add(recipientParticipant, offerId);

            return new EquipmentTransferResult
            {
                RequestId                  = header.RequestId,
                Operation                  = header.Operation,
                Rejection                  = TransactionRejection.None,
                SenderInventoryRevision    = senderInventory.Revision,
                RecipientInventoryRevision = recipientInventory.Revision,
                Offer                      = snapshot,
            };
        }

        [Server]
        private EquipmentTransferResult ServerAcceptOffer(
            in TransactionRequestHeader header, uint offerId,
            uint expectedRecipientRevision, NetworkIdentity requester,
            ParticipantId participant, InventoryController requesterInventory,
            in StreamKey stream, in RequestArguments arguments, out bool remembered)
        {
            remembered = false;

            if ((!_offers.TryGetValue(offerId, out OfferRecord offer)) ||
                (offer.Snapshot.State != EquipmentTransferState.Offered))
            {
                return MakeFailure(header, TransactionRejection.OfferNotFound);
            }

            if ((participant != offer.RecipientParticipant) ||
                (requester.netId != offer.Snapshot.RecipientNetId))
            {
                return MakeFailure(header, TransactionRejection.NotOfferParticipant, offer.Snapshot);
            }


            if (requesterInventory.Revision != expectedRecipientRevision)
            {
                return MakeFailure(header, TransactionRejection.StaleInventoryRevision,
                    offer.Snapshot);
            }

            if (RunTimeLocator.Current.GameplayElapsedSeconds >=
                offer.Snapshot.ExpiresAtGameplayTime)
            {
                EquipmentTransferOfferSnapshot expired = EndOffer(offer,
                    EquipmentTransferState.Expired);
                return MakeFailure(header, TransactionRejection.OfferExpired, expired);
            }

            NetworkIdentity senderIdentity = offer.SenderIdentity;
            NetworkIdentity recipientIdentity = offer.RecipientIdentity;
            InventoryController senderInventory = offer.SenderInventory;
            InventoryController recipientInventory = offer.RecipientInventory;
            if ((senderIdentity == null) || (recipientIdentity == null) ||
                (senderInventory == null) || (recipientInventory == null) ||
                (!NetworkServer.spawned.TryGetValue(offer.Snapshot.SenderNetId,
                    out NetworkIdentity currentSender)) ||
                (!NetworkServer.spawned.TryGetValue(offer.Snapshot.RecipientNetId,
                    out NetworkIdentity currentRecipient)) ||
                (currentSender != senderIdentity) || (currentRecipient != recipientIdentity) ||
                (requesterInventory != recipientInventory) ||
                (offer.SenderParticipantSource == null) ||
                (offer.RecipientParticipantSource == null) ||
                (offer.SenderParticipantSource.Participant != offer.SenderParticipant) ||
                (offer.RecipientParticipantSource.Participant != offer.RecipientParticipant))
            {
                EquipmentTransferOfferSnapshot cancelled = EndOffer(offer,
                    EquipmentTransferState.Cancelled);
                return MakeFailure(header, TransactionRejection.InvalidState, cancelled);
            }

            TransactionRejection state = TransactionServiceUtility.EvaluatePlayers(
                senderIdentity, offer.SenderLife, recipientIdentity, offer.RecipientLife);
            if (state != TransactionRejection.None)
            {
                return MakeFailure(header, state, offer.Snapshot);
            }

            if ((senderInventory.Revision != offer.Snapshot.SenderInventoryRevision) ||
                (recipientInventory.Revision != offer.Snapshot.RecipientInventoryRevision))
            {
                EquipmentTransferOfferSnapshot cancelled = EndOffer(offer,
                    EquipmentTransferState.Cancelled);
                return MakeFailure(header, TransactionRejection.StaleInventoryRevision,
                    cancelled);
            }

            InventoryResult takeResult = senderInventory.ServerPrepareTakeEquipment(
                offer.SenderSlot, offer.Snapshot.EquipmentInstanceId,
                offer.Snapshot.SenderInventoryRevision, out WeaponCombatSnapshot equipment,
                out InventoryCommitPlan senderPlan);
            if (takeResult != InventoryResult.Success)
            {
                EquipmentTransferOfferSnapshot cancelled = EndOffer(offer,
                    EquipmentTransferState.Cancelled);
                return MakeFailure(header, TransactionRequestRules.FromInventory(takeResult),
                    cancelled);
            }

            InventoryResult storeResult = recipientInventory.ServerPrepareStoreEquipment(equipment,
                offer.Snapshot.RecipientInventoryRevision, out InventoryCommitPlan recipientPlan);
            if (storeResult != InventoryResult.Success)
            {
                EquipmentTransferOfferSnapshot cancelled = EndOffer(offer,
                    EquipmentTransferState.Cancelled);
                return MakeFailure(header, TransactionRequestRules.FromInventory(storeResult),
                    cancelled);
            }

            if (!TryAcquireBoth(senderInventory, senderPlan, recipientInventory,
                    recipientPlan))
            {
                return MakeFailure(header, TransactionRejection.StaleInventoryRevision,
                    offer.Snapshot);
            }

            _isPublishing = true;
            try
            {
                senderInventory.ServerApplyTransaction(senderPlan);
                recipientInventory.ServerApplyTransaction(recipientPlan);
                EquipmentTransferOfferSnapshot accepted = EndOffer(offer,
                    EquipmentTransferState.Accepted);

                var success = new EquipmentTransferResult
                {
                    RequestId                  = header.RequestId,
                    Operation                  = header.Operation,
                    Rejection                  = TransactionRejection.None,
                    SenderInventoryRevision    = senderInventory.Revision,
                    RecipientInventoryRevision = recipientInventory.Revision,
                    Offer                      = accepted,
                };
                Remember(stream, header.RequestId, arguments, success);
                remembered = true;

                TryPublishInventory(senderInventory, senderPlan, "보낸 사람");
                TryPublishInventory(recipientInventory, recipientPlan, "받는 사람");

                return success;
            }
            finally
            {
                senderInventory.ServerReleaseTransaction();
                recipientInventory.ServerReleaseTransaction();
                _isPublishing = false;
            }
        }

        [Server]
        private EquipmentTransferResult ServerCloseOffer(
            in TransactionRequestHeader header, uint offerId, NetworkIdentity requester,
            ParticipantId participant, EquipmentTransferState requestedState)
        {
            if ((!_offers.TryGetValue(offerId, out OfferRecord offer)) ||
                (offer.Snapshot.State != EquipmentTransferState.Offered))
            {
                return MakeFailure(header, TransactionRejection.OfferNotFound);
            }

            bool allowed = ((requestedState == EquipmentTransferState.Rejected) &&
                    (participant == offer.RecipientParticipant) &&
                    (requester.netId == offer.Snapshot.RecipientNetId)) ||
                ((requestedState == EquipmentTransferState.Cancelled) &&
                    (participant == offer.SenderParticipant) &&
                    (requester.netId == offer.Snapshot.SenderNetId));
            if (!allowed)
            {
                return MakeFailure(header, TransactionRejection.NotOfferParticipant,
                    offer.Snapshot);
            }

            EquipmentTransferOfferSnapshot ended = EndOffer(offer, requestedState);
            return new EquipmentTransferResult
            {
                RequestId = header.RequestId,
                Operation = header.Operation,
                Rejection = TransactionRejection.None,
                Offer     = ended,
            };
        }

        [Server]
        private void TryPublishInventory(InventoryController inventory,
            in InventoryCommitPlan plan, string role)
        {
            try
            {
                inventory.ServerPublishTransaction(plan);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EquipmentTransferService] {role} 인벤토리 공개 실패: {e}",
                    this);
            }
        }

        [Server]
        private bool TryAcquireBoth(InventoryController sender,
            in InventoryCommitPlan senderPlan, InventoryController recipient,
            in InventoryCommitPlan recipientPlan)
        {
            InventoryController first = sender.netId < recipient.netId ? sender : recipient;
            InventoryController second = first == sender ? recipient : sender;
            InventoryCommitPlan firstPlan = first == sender ? senderPlan : recipientPlan;
            InventoryCommitPlan secondPlan = first == sender ? recipientPlan : senderPlan;

            if (!first.ServerTryAcquireTransaction(firstPlan))
            {
                return false;
            }

            if (!second.ServerTryAcquireTransaction(secondPlan))
            {
                first.ServerReleaseTransaction();
                return false;
            }

            return true;
        }

        [Server]
        private EquipmentTransferOfferSnapshot EndOffer(OfferRecord offer,
            EquipmentTransferState state)
        {
            EquipmentTransferOfferSnapshot snapshot = offer.Snapshot;
            snapshot.State = state;
            offer.Snapshot = snapshot;

            _offers.Remove(snapshot.OfferId);
            _activeOfferByParticipant.Remove(offer.SenderParticipant);
            _activeOfferByParticipant.Remove(offer.RecipientParticipant);
            return snapshot;
        }

        [Server]
        private void ServerEndOfferForParticipantIfInvalid(ParticipantId participant)
        {
            if ((!_activeOfferByParticipant.TryGetValue(participant, out uint offerId)) ||
                (!_offers.TryGetValue(offerId, out OfferRecord offer)))
            {
                _activeOfferByParticipant.Remove(participant);
                return;
            }

            if (IsOfferInvalid(offer, out EquipmentTransferState state))
            {
                EquipmentTransferOfferSnapshot ended = EndOffer(offer, state);
                NotifyBoth(new EquipmentTransferResult { Offer = ended });
            }
        }

        [Server]
        private void ServerEndInvalidOffers()
        {
            _endBuffer.Clear();
            foreach (KeyValuePair<uint, OfferRecord> pair in _offers)
            {
                if (IsOfferInvalid(pair.Value, out _))
                {
                    _endBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _endBuffer.Count; ++i)
            {
                if ((!_offers.TryGetValue(_endBuffer[i], out OfferRecord offer)) ||
                    (!IsOfferInvalid(offer, out EquipmentTransferState state)))
                {
                    continue;
                }

                EquipmentTransferOfferSnapshot ended = EndOffer(offer, state);
                NotifyBoth(new EquipmentTransferResult { Offer = ended });
            }
        }

        [Server]
        private bool IsOfferInvalid(OfferRecord offer, out EquipmentTransferState state)
        {
            state = EquipmentTransferState.Cancelled;

            if (RunTimeLocator.Current.GameplayElapsedSeconds >=
                offer.Snapshot.ExpiresAtGameplayTime)
            {
                state = EquipmentTransferState.Expired;
                return true;
            }

            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (director == null) ||
                (run.RunId.Value != offer.RunIdValue) ||
                (director.Revision.Value != offer.SceneRevisionValue))
            {
                return true;
            }

            if ((offer.SenderIdentity == null) || (offer.RecipientIdentity == null) ||
                (offer.SenderInventory == null) || (offer.RecipientInventory == null) ||
                ((offer.SenderLife as UnityEngine.Object) == null) ||
                ((offer.RecipientLife as UnityEngine.Object) == null) ||
                ((offer.SenderParticipantSource as UnityEngine.Object) == null) ||
                ((offer.RecipientParticipantSource as UnityEngine.Object) == null) ||
                (offer.SenderIdentity.connectionToClient == null) ||
                (offer.RecipientIdentity.connectionToClient == null) ||
                (!NetworkServer.spawned.TryGetValue(offer.Snapshot.SenderNetId,
                    out NetworkIdentity currentSender)) ||
                (!NetworkServer.spawned.TryGetValue(offer.Snapshot.RecipientNetId,
                    out NetworkIdentity currentRecipient)) ||
                (currentSender != offer.SenderIdentity) ||
                (currentRecipient != offer.RecipientIdentity) ||
                (offer.SenderParticipantSource.Participant != offer.SenderParticipant) ||
                (offer.RecipientParticipantSource.Participant != offer.RecipientParticipant) ||
                (offer.SenderInventory.Revision != offer.Snapshot.SenderInventoryRevision) ||
                (offer.RecipientInventory.Revision != offer.Snapshot.RecipientInventoryRevision))
            {
                return true;
            }

            return false;
        }

        [Server]
        private void SendCounterpartyResult(uint requesterNetId,
            in EquipmentTransferResult result)
        {
            uint counterpartyNetId = requesterNetId == result.Offer.SenderNetId
                ? result.Offer.RecipientNetId : result.Offer.SenderNetId;
            if ((counterpartyNetId != 0u) &&
                NetworkServer.spawned.TryGetValue(counterpartyNetId,
                    out NetworkIdentity identity) &&
                (identity.connectionToClient != null))
            {
                SendResult(identity.connectionToClient, result);
            }
        }

        [Server]
        private void NotifyBoth(in EquipmentTransferResult result)
        {
            if (NetworkServer.spawned.TryGetValue(result.Offer.SenderNetId,
                    out NetworkIdentity sender) && (sender.connectionToClient != null))
            {
                SendResult(sender.connectionToClient, result);
            }

            if (NetworkServer.spawned.TryGetValue(result.Offer.RecipientNetId,
                    out NetworkIdentity recipient) && (recipient.connectionToClient != null))
            {
                SendResult(recipient.connectionToClient, result);
            }
        }

        [Server]
        private void SendResult(NetworkConnectionToClient target,
            in EquipmentTransferResult result)
        {
            if (target != null)
            {
                TargetTransferResult(target, result);
            }
        }

        [Server]
        private void ServerRefreshContext()
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            uint runIdValue = run != null ? run.RunId.Value : 0u;
            uint sceneRevisionValue = director != null ? director.Revision.Value : 0u;

            if ((_lastServerRunIdValue != 0u) &&
                (_lastServerRunIdValue != runIdValue))
            {
                CancelAllOffers();
                _lastByStream.Clear();
            }
            else if ((_lastServerSceneRevisionValue != 0u) &&
                (_lastServerSceneRevisionValue != sceneRevisionValue))
            {
                CancelAllOffers();
            }

            _lastServerRunIdValue           = runIdValue;
            _lastServerSceneRevisionValue   = sceneRevisionValue;
            _observedRunIdValue             = runIdValue;
            _observedSceneRevisionValue     = sceneRevisionValue;
        }

        [Server]
        private void CancelAllOffers()
        {
            _endBuffer.Clear();
            foreach (uint offerId in _offers.Keys)
            {
                _endBuffer.Add(offerId);
            }

            for (int i = 0; i < _endBuffer.Count; ++i)
            {
                if (_offers.TryGetValue(_endBuffer[i], out OfferRecord offer))
                {
                    EquipmentTransferOfferSnapshot ended = EndOffer(offer,
                        EquipmentTransferState.Cancelled);
                    NotifyBoth(new EquipmentTransferResult { Offer = ended });
                }
            }
        }

        private uint TakeOfferId()
        {
            uint result = _nextOfferId;
            if (_nextOfferId == uint.MaxValue)
            {
                _nextOfferId = 0u;
            }
            else
            {
                ++_nextOfferId;
            }

            return result;
        }

        private void ClearServerState()
        {
            _offers.Clear();
            _activeOfferByParticipant.Clear();
            _lastByStream.Clear();
            _endBuffer.Clear();
            _nextOfferId = 1u;
            _isPublishing = false;
        }
    }
}
