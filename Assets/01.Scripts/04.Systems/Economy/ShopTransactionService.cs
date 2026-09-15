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
    public partial class ShopTransactionService : NetworkBehaviour
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

        [TargetRpc]
        private void TargetTransactionResult(NetworkConnection target,
            ShopTransactionResult result)
        {
            TransactionCompleted?.Invoke(result);
        }
    }
}
