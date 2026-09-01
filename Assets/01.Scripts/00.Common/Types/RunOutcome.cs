namespace Geumeum.Common
{
    /// <summary> 런이 끝난 원인. Result 진입 이유마다 점수, UI가 다르게 반응할 수 있도록 서버가 하나만 확정한다 </summary>
    public enum RunOutcome
    {
        None        = 0, // 아직 끝나지 않았다
        Victory     = 1, // 보스 처치. 보스 연결 전까지는 어떤 경로도 이 값을 커밋하지 않는다
        TimeExpired = 2,
        PartyWipe   = 3, // 전멸 판정 경로는 아직 없다, 값만 예약한다
        PlayerLeft  = 4,
    }
}
