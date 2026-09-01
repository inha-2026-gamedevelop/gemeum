namespace Geumeum.Combat
{
    /// <summary> 패링 요청의 결과. bool 하나로는 후딜을 재생할 실패와 예측을 취소할 거부를 구분할 수 없다 </summary>
    public enum ParryResultCode : byte
    {
        Success                  = 0,
        MissNoWindow             = 1,
        MissTiming               = 2,
        RejectedInvalidRequest   = 10,
        RejectedDuplicate        = 11,
        RejectedState            = 12,
        RejectedWeapon           = 13,
        RejectedBusy             = 14,
        RejectedStamina          = 15,
        RejectedWindowMismatch   = 16,
        RejectedResolutionClosed = 17,
        RejectedWindowConsumed   = 18,
        RejectedDependency       = 19,
    }
}
