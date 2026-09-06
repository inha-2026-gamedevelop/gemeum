namespace Geumeum.Common
{
    /// <summary>
    /// 상태 보관용 생명 상태. Combat의 PlayerLifeState를 공용 계층에서 부르기 위한 값이다.
    ///
    /// Common이 Combat의 enum을 직접 참조하면 의존 방향이 뒤집힌다.
    /// 값은 Combat 쪽과 같게 맞춰 두고, 변환은 Combat의 어댑터가 한다.
    /// 직렬화되므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    public enum PlayerLifeSnapshotState
    {
        Alive              = 0,
        Downed             = 1,
        Spectating         = 2,
        ReviveInvulnerable = 3,

        // 보스 최종 구간 전용. 사망이 아니라 붙잡힌 상태이고 전멸 판정에 넣지 않는다.
        // Combat에는 아직 대응 값이 없으므로 변환에서 별도로 다룬다
        Swallowed          = 4,
    }
}
