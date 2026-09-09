namespace Geumeum.Common
{
    /// <summary> 서버 특성 배분과 초기화 결과 </summary>
    public enum TalentAllocationResult
    {
        Success = 0,
        InvalidState = 1,
        InvalidNode = 2,
        WeaponMismatch = 3,
        MissingPrerequisite = 4,
        AlreadyAllocated = 5,
        InsufficientPoints = 6,
        StaleRevision = 7,
        RevisionExhausted = 8,
    }
}
