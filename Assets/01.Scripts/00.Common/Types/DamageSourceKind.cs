namespace Geumeum.Common
{
    /// <summary>
    /// 피해를 만든 행동의 종류. 표현 단계(ImpactTier)를 고르는 데만 쓰고 판정에는 쓰지 않는다
    /// 직렬화·네트워크 값이라 기존 값을 재배열하지 않는다
    /// </summary>
    public enum DamageSourceKind : byte
    {
        Unknown       = 0,
        Melee         = 1,  // 근접 평타 1·2타
        MeleeFinisher = 2,  // 근접 평타 마지막 단계
        Arrow         = 3,  // 활 평타
        PrecisionShot = 4,  // 신궁 정밀 사격
        Bell          = 5,  // 방울 평타
        SwordSkill    = 6,  // 차사 R
        CounterSlash  = 7,  // 차사 표식 R 받아베기. Detail = 소비한 표식 수
        BowStab       = 8,  // 신궁 R 찌르기
        WedgeBurst    = 9,  // 신궁 쐐기 파열
        EchoBurst     = 10, // 무녀 잔향 폭발. Detail = 잔향 단계
        BellField     = 11, // 무녀 진 설치·폭발
        Derived       = 12, // 그 밖에 소유자가 있는 파생 피해
    }
}
