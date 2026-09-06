namespace Geumeum.Common
{
    /// <summary>
    /// 런의 종류. 리더보드 등록 가능 여부가 여기서 갈린다.
    /// 직렬화되므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    public enum RunMode
    {
        Standard  = 0, // 정식 런. 승리만 등록 대상이다
        BossTrial = 1, // 보스 트라이. 점수는 보여주되 등록하지 않는다
        Dev       = 2, // 개발 씬에서 연 런. 등록하지 않는다
    }
}
