namespace Geumeum.Common
{
    /// <summary>
    /// 장비 특수옵션 종류. EquipmentInstance가 이 ID와 굴려진 값만 직렬화하고,
    /// 표시 정보는 클라이언트가 같은 GameDB에서 조회한다
    /// </summary>
    public enum EquipmentOptionKind
    {
        None = 0,

        // 공용 (3종 무기 모두)
        CriticalChance        = 100, // 치명타 확률 증가 (%)
        AttackSpeed           = 101, // 공격 속도 증가 (%)
        StaminaCostReduction  = 102, // 스태미나 소모 감소 (%)

        // 검 (차사) 전용
        ParryGaugeGain        = 200, // 패링 게이지 축적량 증가 (절대값)
        CounterDuration       = 203, // 반격 표식 유지시간 (초)
        ParryWindow           = 201, // 패링 판정 창 증가 (초)
        CriticalMultiplierMin = 202, // 치명의 일격 최소 배율 상승 (절대값)

        // 활 (신궁) 전용
        ArrowSpeed            = 300, // 화살 비행 속도 증가 (%)
        GuidanceDuration      = 301, // 유도 지속 시간 증가 (초)
        PierceCount           = 302, // 관통 대상 수 증가 (절대값)

        // 방울 (무녀) 전용
        HealAmount            = 400, // 치유량 증가 (%)
        BuffDuration          = 401, // 버프 지속 시간 증가 (%)
        SpiritCostReduction   = 402, // 신력 소모 감소 (%)
    }
}
