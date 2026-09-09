// System
using System;

namespace Geumeum.Common
{
    /// <summary> 경제와 장비 양도 요청의 서버 작업 종류 </summary>
    public enum TransactionOperation
    {
        None          = 0,
        Purchase      = 1,
        RerollTicket  = 2,
        UpgradeTicket = 3,
        OfferTransfer = 4,
        AcceptTransfer = 5,
        RejectTransfer = 6,
        CancelTransfer = 7,
        UsePotion     = 8,
    }

    /// <summary> 서버 거래가 원본을 바꾸지 않은 이유 </summary>
    public enum TransactionRejection
    {
        None                 = 0,
        InvalidRequest       = 1,
        DuplicateRequest     = 2,
        StaleRequest         = 3,
        InvalidState         = 4,
        Paused               = 5,
        SceneMismatch        = 6,
        Dead                 = 7,
        InCombat             = 8,
        OutOfRange           = 9,
        ShopClosed           = 10,
        InvalidItem          = 11,
        InsufficientCurrency = 12,
        InventoryFull        = 13,
        StaleWalletRevision  = 14,
        StaleInventoryRevision = 15,
        RevisionExhausted    = 16,
        MaxGrade             = 17,
        OfferConflict        = 18,
        OfferNotFound        = 19,
        NotOfferParticipant  = 20,
        OfferExpired         = 21,
        InternalError        = 22,
        ResourceFull         = 23,
        RecoveryBlocked      = 24,
    }

    /// <summary> 런과 플레이어 바인딩에 묶인 거래 요청 헤더 </summary>
    [Serializable]
    public struct TransactionRequestHeader : IEquatable<TransactionRequestHeader>
    {
        public uint                 RunIdValue;
        public uint                 SceneRevisionValue;
        /// <summary> 요청을 보낸 소유 플레이어 NetworkIdentity의 netId 세대 </summary>
        public uint                 NetworkObjectGenerationValue;
        public uint                 RequestId;
        public TransactionOperation Operation;

        public bool IsValid => ((RunIdValue != 0u) && (SceneRevisionValue != 0u) &&
            (NetworkObjectGenerationValue != 0u) && (RequestId != 0u) &&
            (Operation != TransactionOperation.None));

        public bool Equals(TransactionRequestHeader other)
        {
            return ((RunIdValue == other.RunIdValue) &&
                    (SceneRevisionValue == other.SceneRevisionValue) &&
                    (NetworkObjectGenerationValue == other.NetworkObjectGenerationValue) &&
                    (RequestId == other.RequestId) && (Operation == other.Operation));
        }

        public override bool Equals(object obj)
        {
            return ((obj is TransactionRequestHeader other) && Equals(other));
        }

        public override int GetHashCode()
        {
            return (RunIdValue.GetHashCode() ^ SceneRevisionValue.GetHashCode() ^
                    NetworkObjectGenerationValue.GetHashCode() ^ RequestId.GetHashCode() ^
                    ((int) Operation << 24));
        }
    }

    /// <summary> 구매나 티켓 사용의 권위 결과 </summary>
    [Serializable]
    public struct ShopTransactionResult
    {
        public uint                 RequestId;
        public TransactionOperation Operation;
        public TransactionRejection Rejection;
        public ShopItemKind         ItemKind;
        public uint                 WalletRevision;
        public uint                 InventoryRevision;
        public uint                 ResourceRevision;
        public float                AppliedAmount;
        public WeaponCombatSnapshot Equipment;

        public bool Succeeded => (Rejection == TransactionRejection.None);
    }

    /// <summary> 장비 양도 제안 수명 상태 </summary>
    public enum EquipmentTransferState
    {
        None      = 0,
        Offered   = 1,
        Accepted  = 2,
        Rejected  = 3,
        Expired   = 4,
        Cancelled = 5,
    }

    /// <summary> 참가자 양쪽에 전달하는 장비 양도 제안 </summary>
    [Serializable]
    public struct EquipmentTransferOfferSnapshot
    {
        public uint                   OfferId;
        public EquipmentTransferState State;
        public uint                   SenderNetId;
        public uint                   RecipientNetId;
        public uint                   EquipmentInstanceId;
        public uint                   SenderInventoryRevision;
        public uint                   RecipientInventoryRevision;
        public double                 ExpiresAtGameplayTime;
    }

    /// <summary> 장비 양도 요청의 권위 결과 </summary>
    [Serializable]
    public struct EquipmentTransferResult
    {
        public uint                           RequestId;
        public TransactionOperation           Operation;
        public TransactionRejection           Rejection;
        public uint                           SenderInventoryRevision;
        public uint                           RecipientInventoryRevision;
        public EquipmentTransferOfferSnapshot Offer;

        public bool Succeeded => (Rejection == TransactionRejection.None);
    }
}
