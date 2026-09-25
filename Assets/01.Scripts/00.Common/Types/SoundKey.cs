namespace Geumeum.Common
{
    /// <summary> 재생 요청 키. 실제 클립은 SoundCatalogSO가 정한다 </summary>
    public enum SoundKey
    {
        None = 0,

        // P0 - 없으면 보스전이 성립하지 않는 것 (SOUND_DESIGN 2.2)
        BossTellA       = 1, // 예고 템플릿 windup 1.2초
        BossTellB       = 2, // 1.5초
        BossTellC       = 3, // 2.0초
        BossTellD       = 4, // 0.8초
        ParrySuccess    = 5,
        ParryFail       = 6,
        MeleeHit        = 7,
        MeleeWhiff      = 8,
        PlayerHurt      = 9,
        BossGroggyEnter = 10,

        // P1
        MeleeSwing1   = 100,
        MeleeSwing2   = 101,
        MeleeSwing3   = 102,
        MeleeCritical = 103,
        BowFire       = 110,
        ArrowHit      = 111,
        ArrowExpire   = 112,
        Roll          = 120,
        StaminaEmpty  = 121,
        FootstepWet   = 122,
        FootstepDry   = 123,
        BossHurt      = 130,
        BossGroggyEnd = 131,
        BarrierShrink = 140,

        EnemyDeath = 150,

        BellSwing  = 160,
        HealCast   = 161,
        HealResolve = 162,
        MarkApplied = 163,
        ParryStart = 164,

        BossTailBreak = 170,
        BossGateOpen  = 171,
        BossPhaseShift = 172,
        WedgePlace = 180,
        WedgeCrack = 181,
        WedgeBurst = 182,
        EchoFirst = 183,
        EchoSecond = 184,
        EchoThird = 185,
        EchoRecall = 186,
        EchoBurst = 187,

        // 타이밍 섬광(COMBAT_STYLE_DESIGN S4)
        ParryWindowFlash   = 188, // 패링 창이 열리는 순간의 맑은 고음
        UnparryableWarning = 189, // 패링 불가 공격 경고의 낮은 음

        // 극한 회피(COMBAT_STYLE_DESIGN S7). 회피자 화면에서만 울리는 짧은 고음
        PerfectDodge = 190,
    }
}
