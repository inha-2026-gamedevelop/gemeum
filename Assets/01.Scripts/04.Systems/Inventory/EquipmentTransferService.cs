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
    public partial class EquipmentTransferService : NetworkBehaviour
    {
        private readonly struct StreamKey : IEquatable<StreamKey>
        {
            public readonly uint  RunIdValue;
            public readonly ParticipantId ParticipantValue;
            public readonly uint  NetworkObjectGenerationValue;
            public readonly TransactionOperation Operation;

            public StreamKey(in TransactionRequestHeader header, ParticipantId participant)
            {
                RunIdValue                  = header.RunIdValue;
                ParticipantValue            = participant;
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

        private readonly Dictionary<uint, OfferRecord> _offers =
            new Dictionary<uint, OfferRecord>();
        private readonly Dictionary<ParticipantId, uint> _activeOfferByParticipant =
            new Dictionary<ParticipantId, uint>();
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

        [TargetRpc]
        private void TargetTransferResult(NetworkConnection target,
            EquipmentTransferResult result)
        {
            TransferChanged?.Invoke(result);
        }
    }
}
