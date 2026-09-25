namespace Geumeum.Common
{
    /// <summary>
    /// 타격 표현의 중요도 단계(COMBAT_STYLE_DESIGN 5장). 피해 배율이 아니다
    /// 단계가 오를수록 히트스톱·셰이크·플래시·소리가 함께 세진다. 직렬화 값이라 재배열하지 않는다
    /// </summary>
    public enum ImpactTier : byte
    {
        Whiff  = 0, // T0 헛스윙
        Light  = 1, // T1 평타 1·2타, 화살, 방울
        Medium = 2, // T2 3타 마무리, 무기 스킬, 찌르기, 표식 1개 받아베기
        Heavy  = 3, // T3 치명타, 표식 2~3개 받아베기, 정밀 사격, 잔향 폭발
        Kill   = 4, // T4 잡귀 처치
        Party  = 5, // T5 그로기 진입, 꼬리 파괴 - 파티 전원
    }
}
