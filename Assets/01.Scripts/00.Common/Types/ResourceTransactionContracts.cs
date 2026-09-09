namespace Geumeum.Common
{
    /// <summary> 거래로 회복할 플레이어 자원 </summary>
    public enum RecoveryResourceKind
    {
        None   = 0,
        Health = 1,
        Spirit = 2,
    }

    /// <summary> 자원 회복 계획을 만들지 못한 이유 </summary>
    public enum ResourceTransactionResult
    {
        Success           = 0,
        InvalidRequest    = 1,
        Dead              = 2,
        ResourceFull      = 3,
        RecoveryBlocked   = 4,
        StaleRevision     = 5,
        RevisionExhausted = 6,
        InvalidState      = 7,
    }

    /// <summary> 검증 뒤 통지 없이 적용할 자원 회복 값 </summary>
    public readonly struct ResourceCommitPlan
    {
        public readonly RecoveryResourceKind ResourceKind;
        public readonly ParticipantId        SourceParticipant;
        public readonly uint                 ExpectedRevision;
        public readonly float                PreviousValue;
        public readonly float                NextValue;
        public readonly float                AppliedAmount;

        public bool IsValid => ((ResourceKind != RecoveryResourceKind.None) &&
            (!float.IsNaN(PreviousValue)) && (!float.IsInfinity(PreviousValue)) &&
            (!float.IsNaN(NextValue)) && (!float.IsInfinity(NextValue)) &&
            (!float.IsNaN(AppliedAmount)) && (!float.IsInfinity(AppliedAmount)) &&
            (NextValue > PreviousValue) && (AppliedAmount > 0f));

        public ResourceCommitPlan(RecoveryResourceKind resourceKind,
            ParticipantId sourceParticipant, uint expectedRevision, float previousValue,
            float nextValue)
        {
            ResourceKind      = resourceKind;
            SourceParticipant = sourceParticipant;
            ExpectedRevision  = expectedRevision;
            PreviousValue     = previousValue;
            NextValue         = nextValue;
            AppliedAmount     = nextValue - previousValue;
        }
    }
}
