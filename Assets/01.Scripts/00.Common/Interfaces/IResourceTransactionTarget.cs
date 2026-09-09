namespace Geumeum.Common
{
    /// <summary> 인벤토리 소비와 한 커밋 경계에서 자원을 회복하는 서버 대상 </summary>
    public interface IResourceTransactionTarget
    {
        /// <summary> 서버 자원 원본의 낙관적 동시성 revision </summary>
        uint ResourceRevision { get; }

        /// <summary> 커밋과 통지가 끝날 때까지 다른 자원 변경을 막는가 </summary>
        bool IsResourceTransactionLocked { get; }

        /// <summary> 현재 원본과 회복 정책으로 실제 적용량이 고정된 계획을 만든다 </summary>
        ResourceTransactionResult ServerPrepareRecovery(RecoveryResourceKind resourceKind,
            ParticipantId sourceParticipant, float amount, out ResourceCommitPlan plan);

        /// <summary> 계획이 여전히 유효하면 통지가 끝날 때까지 자원을 잠근다 </summary>
        bool ServerTryAcquireResourceTransaction(in ResourceCommitPlan plan);

        /// <summary> 잠근 계획을 서버 원본에만 적용한다 </summary>
        void ServerApplyResourceTransaction(in ResourceCommitPlan plan);

        /// <summary> 결과 캐시와 모든 원본이 확정된 뒤 자원 표시를 공개한다 </summary>
        void ServerPublishResourceTransaction(in ResourceCommitPlan plan);

        /// <summary> 자원 공개가 끝난 뒤 거래 잠금을 푼다 </summary>
        void ServerReleaseResourceTransaction();
    }
}
