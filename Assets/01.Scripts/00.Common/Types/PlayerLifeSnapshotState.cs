namespace Geumeum.Common
{
    /// <summary> 상태 보관용 생명 상태. Combat의 PlayerLifeState를 공용 계층에서 부르기 위한 값이다 </summary>
    public enum PlayerLifeSnapshotState
    {
        Alive              = 0,
        Downed             = 1,
        Spectating         = 2,
        ReviveInvulnerable = 3,

        // 보스 최종 구간 전용. 사망이 아니라 붙잡힌 상태이고 전멸 판정에 넣지 않는다
        Swallowed          = 4,
    }
}
