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
        SpiritCostPercent = 402,

        // 은퇴한 구 방울 노드 효과. 직렬화 값만 예약해 두고 새 노드에 다시 쓰지 않는다
        // 401은 장비 옵션 BuffDuration으로만 남기고 특성 쪽 공급은 없앴다
        BuffDurationPercent = 401,
        HealRadiusPercent = 403,
        BellDamagePercent = 404,
        SupportBuffPercent = 405,

        // 공명진 트리(방울 13노드). 값 해석은 TalentEffectRules와 CombatStatCalculator가 정한다
        BasicStaminaReduction = 210,
        CounterDurationBonus = 211,
        ParryStaminaBonus = 212,
        CounterRecoveryReduction = 213,
        CounterThirdCombo = 214,
        SecondComboDamagePercent = 215,
        ComboWindowBonus = 216,
        ThirdHitSkillCooldown = 217,
        SkillSecondCombo = 218,
        RollBasicStaminaReduction = 219,
        MissSkillRecoveryReduction = 220,
        RollSkillDamagePercent = 221,
        SideStepSkill = 222,
        BowBasicStaminaReduction = 310,
        BowSkillStaminaReduction = 311,
        WedgeDurationBonus = 312,
        WedgeFirstHitCooldown = 313,
        WedgeNormalBurstPercent = 314,
        PrecisionStaminaReduction = 315,
        WedgePrecisionBurstPercent = 316,
        PrecisionPierceBonus = 317,
        PrecisionFollowThrough = 318,
        BowRollBasicReduction = 319,
        BowComboWindowBonus = 320,
        WedgeRollReduction = 321,
        RollShotRecoveryReduction = 322,

        BellFirstHitSpiritGain = 406,
        EchoRecallSpiritCost = 407,
        EchoDurationSeconds = 408,
        EchoFieldDamagePercent = 409,
        FieldEchoExtendSeconds = 410,
        HealSpiritCost = 411,
        FieldHealEchoDiscount = 412,
        FieldHealAfterglowPercent = 413,
        SkillSpiritCost = 414,
        FieldRadiusBonus = 415,
        FieldTailSeconds = 416,
        FieldWideningRadiusBonus = 417,
    }
}
