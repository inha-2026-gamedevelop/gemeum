namespace Geumeum.Common
{
    /// <summary>
    /// BGM 트랙. 트랙 하나가 곡 하나에 대응한다.
    /// 직렬화되므로 기존 값을 재배열하지 않고 뒤에만 추가한다
    /// </summary>
    public enum BgmTrack
    {
        None = 0,

        /// <summary> 개활. 런의 65%를 차지한다, 결계 단계별 스템 레이어링 대상 </summary>
        Field = 1,

        /// <summary> 보스 1페이즈 "객주의 가락", 실내악 </summary>
        Boss1 = 2,

        /// <summary> 보스 2페이즈 "아홉의 가락", 오케스트라와 목소리 </summary>
        Boss2 = 3,

        /// <summary> 처치 큐. 무음 3초 뒤에 오는 절 종 </summary>
        KillCue = 4,
    }
}
