namespace Geumeum.Common
{
    /// <summary>
    /// 런 흐름의 씬. 파일 이름이 아니라 역할을 가리킨다
    /// 직렬화되므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    public enum RunSceneId
    {
        None         = 0,
        Boot         = 1, // 프리워밍, GameDB 부트와 영속 객체 준비. 오프라인
        Title        = 2, // 게임 시작 화면. 오프라인
        Lobby        = 3, // 편성과 준비
        Field        = 4, // 개활 파밍, 결계와 잡귀
        BossEntrance = 5, // 전실, 시계 밖. 전원 로드 확인
        Boss         = 6, // 보스 아레나
        Result       = 7, // 정산. 참가자 전투 상태를 복구하지 않는다
    }
}
