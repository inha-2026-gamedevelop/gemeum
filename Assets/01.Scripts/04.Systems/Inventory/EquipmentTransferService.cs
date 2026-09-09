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
    /// <summary> 단방향 장비 양도 제안과 수락 시 두 인벤토리 커밋을 소유한다 </summary>
    [DisallowMultipleComponent]
    public class EquipmentTransferService : NetworkBehaviour
    {
        private readonly struct StreamKey : IEquatable<StreamKey>
        {
            public readonly uint  RunIdValue;
            public readonly ulong ParticipantValue;
            public readonly uint  NetworkObjectGenerationValue;
            public readonly TransactionOperation Operation;

            public StreamKey(in TransactionRequestHeader header, ParticipantId participant)
            {
                RunIdValue                  = header.RunIdValue;
                ParticipantValue            = participant.Value;
                NetworkObjectGenerationValue = header.NetworkObjectGenerationValue;
                Operation                   = header.Operation;
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
            public readonly uint TargetNetId;
            public readonly uint OfferId;
            public readonly int  SenderSlot;
            public readonly uint EquipmentInstanceId;
            public readonly uint SenderRevision;
            public readonly uint RecipientRevision;

            public RequestArguments(uint targetNetId, uint offerId, int senderSlot,
                uint equipmentInstanceId, uint senderRevision, uint recipientRevision)
            {
                TargetNetId          = targetNetId;
                OfferId              = offerId;
                SenderSlot           = senderSlot;
                EquipmentInstanceId  = equipmentInstanceId;
                SenderRevision       = senderRevision;
                RecipientRevision    = recipientRevision;
            }

            public bool Equals(RequestArguments other)
            {
                return ((TargetNetId == other.TargetNetId) && (OfferId == other.OfferId) &&
                        (SenderSlot == other.SenderSlot) &&
                        (EquipmentInstanceId == other.EquipmentInstanceId) &&
                        (SenderRevision == other.SenderRevision) &&
                        (RecipientRevision == other.RecipientRevision));
            }
        }

        private sealed class RequestRecord
        {
            public uint                    RequestId;
            public RequestArguments        Arguments;
            public EquipmentTransferResult Result;
        }

        private sealed class OfferRecord
        {
            public EquipmentTransferOfferSnapshot Snapshot;
            public ParticipantId SenderParticipant;
            public ParticipantId RecipientParticipant;
            public NetworkIdentity SenderIdentity;
            public NetworkIdentity RecipientIdentity;
            public InventoryController SenderInventory;
            public InventoryController RecipientInventory;
            public IDamageable SenderLife;
            public IDamageable RecipientLife;
            public IGrowthParticipant SenderParticipantSource;
            public IGrowthParticipant RecipientParticipantSource;
            public int SenderSlot;
            public uint RunIdValue;
            public uint SceneRevisionValue;
        }

        [SyncVar] private uint _observedRunIdValue;
        [SyncVar] private uint _observedSceneRevisionValue;

        private readonly Dictionary<uint, OfferRecord> _offers =
            new Dictionary<uint, OfferRecord>();
        private readonly Dictionary<ulong, uint> _activeOfferByParticipant =
            new Dictionary<ulong, uint>();
        private readonly Dictionary<StreamKey, RequestRecord> _lastByStream =
            new Dictionary<StreamKey, RequestRecord>();
        private readonly List<uint> _endBuffer = new List<uint>();

        private uint _nextOfferId = 1u;
        private uint _clientNextRequestId = 1u;
        private uint _lastServerRunIdValue;
        private uint _lastServerSceneRevisionValue;
        private bool _isPublishing;

        public RunId ObservedRunId => new RunId(_observedRunIdValue);

        public SceneRevision ObservedSceneRevision =>
            new SceneRevision(_observedSceneRevisionValue);

        /// <summary> 제안 수명 또는 요청 결과가 소유 클라이언트에 도착했다 </summary>
        public event Action<EquipmentTransferResult> TransferChanged;

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

        /// <summary> 대상에게 단방향 장비 양도를 제안한다 </summary>
        public uint RequestOffer(uint recipientNetId, int senderSlot,
            uint equipmentInstanceId, uint expectedSenderRevision)
        {
            TransactionRequestHeader header = CreateClientHeader(
                TransactionOperation.OfferTransfer);
            if (!header.IsValid)
            {
                return 0u;
            }

            CmdRequestOffer(header, recipientNetId, senderSlot, equipmentInstanceId,
                expectedSenderRevision);
            return header.RequestId;
        }

        public uint RequestAccept(uint offerId, uint expectedRecipientRevision)
        {
            return RequestOfferDecision(TransactionOperation.AcceptTransfer,
                offerId, expectedRecipientRevision);
        }

        public uint RequestReject(uint offerId)
        {
            return RequestOfferDecision(TransactionOperation.RejectTransfer, offerId, 0u);
        }

        public uint RequestCancel(uint offerId)
        {
            return RequestOfferDecision(TransactionOperation.CancelTransfer, offerId, 0u);
        }

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
            if (_activeOfferByParticipant.ContainsKey(senderParticipant.Value) ||
                _activeOfferByParticipant.ContainsKey(recipientParticipant.Value))
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

            // 수신자 인벤토리는 owner-only다. 송신자 입력을 받지 않고 서버 현재 판을 캡처한다.
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
            _activeOfferByParticipant.Add(senderParticipant.Value, offerId);
            _activeOfferByParticipant.Add(recipientParticipant.Value, offerId);

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

        private uint RequestOfferDecision(TransactionOperation operation, uint offerId,
            uint expectedRecipientRevision)
        {
            TransactionRequestHeader header = CreateClientHeader(operation);
            if (!header.IsValid)
            {
                return 0u;
            }

            CmdRequestDecision(header, offerId, expectedRecipientRevision);
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
                RunIdValue                  = _observedRunIdValue,
                SceneRevisionValue          = _observedSceneRevisionValue,
                NetworkObjectGenerationValue = NetworkClient.localPlayer.netId,
                RequestId                   = requestId,
                Operation                   = operation,
            };
        }

        private bool TryReplay(in StreamKey stream, uint requestId,
            in RequestArguments arguments, out EquipmentTransferResult result)
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
            in RequestArguments arguments, in EquipmentTransferResult result)
        {
            _lastByStream[stream] = new RequestRecord
            {
                RequestId = requestId,
                Arguments = arguments,
                Result    = result,
            };
        }

        private EquipmentTransferResult MakeFailure(in TransactionRequestHeader header,
            TransactionRejection rejection)
        {
            return MakeFailure(header, rejection, default);
        }

        private EquipmentTransferResult MakeFailure(in TransactionRequestHeader header,
            TransactionRejection rejection, in EquipmentTransferOfferSnapshot offer)
        {
            return new EquipmentTransferResult
            {
                RequestId = header.RequestId,
                Operation = header.Operation,
                Rejection = rejection,
                Offer     = offer,
            };
        }

        private static EquipmentTransferResult MakeSequenceFailure(uint requestId,
            TransactionOperation operation, TransactionRejection rejection)
        {
            return new EquipmentTransferResult
            {
                RequestId = requestId,
                Operation = operation,
                Rejection = rejection,
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
            _activeOfferByParticipant.Remove(offer.SenderParticipant.Value);
            _activeOfferByParticipant.Remove(offer.RecipientParticipant.Value);
            return snapshot;
        }

        [Server]
        private void ServerEndOfferForParticipantIfInvalid(ParticipantId participant)
        {
            if ((!_activeOfferByParticipant.TryGetValue(participant.Value, out uint offerId)) ||
                (!_offers.TryGetValue(offerId, out OfferRecord offer)))
            {
                _activeOfferByParticipant.Remove(participant.Value);
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

        [TargetRpc]
        private void TargetTransferResult(NetworkConnection target,
            EquipmentTransferResult result)
        {
            TransferChanged?.Invoke(result);
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
