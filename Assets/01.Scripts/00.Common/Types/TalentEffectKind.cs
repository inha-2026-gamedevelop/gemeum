namespace Geumeum.Common
{
    /// <summary> 특성 노드 값의 해석 방식 </summary>
    public enum TalentEffectKind
    {
        None = 0,

        CriticalChancePercent = 100,
        AttackSpeedPercent = 101,
        StaminaCostPercent = 102,

        ParryGaugeGain = 200,
        ParryWindowSeconds = 201,
        CriticalMultiplierMin = 202,
        ThirdComboDamagePercent = 203,

        ArrowSpeedPercent = 300,
        GuidanceDurationSeconds = 301,
        PierceCount = 302,
        BurstShotCount = 303,
        ArrowHitRadiusPercent = 304,
        RangedDamagePercent = 305,

        HealAmountPercent = 400,
        BuffDurationPercent = 401,
        SpiritCostPercent = 402,
        HealRadiusPercent = 403,
        BellDamagePercent = 404,
        SupportBuffPercent = 405,
    }
}
