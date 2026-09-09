namespace Geumeum.Common
{
    /// <summary> 정밀 사격의 서버 홀드 상태 </summary>
    public enum PrecisionShotState
    {
        Idle     = 0,
        Holding  = 1,
        Ready    = 2,
        Released = 3,
        Recovery = 4,
    }
}
