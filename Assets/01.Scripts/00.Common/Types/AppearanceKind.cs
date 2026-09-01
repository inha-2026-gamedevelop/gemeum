namespace Geumeum.Common
{
    /// <summary>
    /// 플레이어 외형. 로비에서 중복 없이 고르며, 무기(직업)와는 무관하다.
    /// 셋 다 차사이고 검, 활, 방울은 런 시작 시 따로 배분된다
    /// </summary>
    public enum AppearanceKind
    {
        Jeonrip = 0, // 전립, 다부진 체형
        Gat     = 1, // 좁은 챙 갓, 표준 체형
        Dugeon  = 2, // 두건, 가는 체형
    }
}
