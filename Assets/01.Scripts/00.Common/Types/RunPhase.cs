namespace Geumeum.Common
{
    /// <summary>
    /// 런 상태 머신의 단계. RunManager가 서버에서만 전환하고 SyncVar로 배포한다.
    /// SyncVar로 직렬화되므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    public enum RunPhase
    {
        Lobby   = 0, // 3명 준비 대기, 런 시계 정지
        Farming = 1, // 개활 파밍, FieldLevel 0
        Shrink1 = 2, // 1차 축소, FieldLevel 1
        Shrink2 = 3, // 2차 축소, 객주 개방, FieldLevel 2
        Shrink3 = 4, // 3차 축소, 신규 스폰 중단, FieldLevel 3
        Boss    = 5, // 보스전, 결계 피해 종료
        Result  = 6, // 정산, 모든 입력, 드랍, 구매 요청 거부
    }
}
