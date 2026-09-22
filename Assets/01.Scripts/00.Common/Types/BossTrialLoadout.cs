// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 보스 트라이 전실에서 한 사람이 고른 편성. 값은 요청일 뿐이고 예산과 범위 검증은 서버가 한다
    /// 자동과 상세 지정을 한 형태에 담는다 - 상세를 안 쓰면 서버가 등급 범위에서 굴린다
    /// </summary>
    [Serializable]
    public struct BossTrialLoadout
    {
        /// <summary> 시작 레벨. 0 이하면 BossTrialLoadoutSO의 기본값을 쓴다 </summary>
        public int StartLevel;

        /// <summary> 무기를 바꿀지. false면 로비가 배정한 무기를 그대로 쓴다 </summary>
        public bool OverrideWeapon;

        /// <summary> 들고 시작할 무기. OverrideWeapon이 true일 때만 쓴다 </summary>
        public WeaponKind Weapon;

        /// <summary> 받을 장비의 필드 레벨. 음수면 장비를 받지 않는다 </summary>
        public int EquipmentTier;

        /// <summary> 장비 등급, 공격력, 옵션을 직접 정한다. false면 서버가 등급 범위에서 굴린다 </summary>
        public bool UseCustomEquipment;

        public ItemGrade EquipmentGrade;

        /// <summary> 0 이하면 등급 범위에서 서버가 굴린다 </summary>
        public float AttackPower;

        public EquipmentOptionKind Option0Kind;
        public float               Option0Value;
        public EquipmentOptionKind Option1Kind;
        public float               Option1Value;

        public int PotionCount;

        /// <summary> 찍을 특성 노드. 순서대로 적용하고 포인트와 선행은 특성 규칙이 검사한다 </summary>
        public int[] TalentNodeIds;
    }

    /// <summary> 편성 제출의 서버 판정 </summary>
    public enum BossTrialLoadoutResult
    {
        Success        = 0,
        NotTrialMode   = 1, // 정식 런에서는 편성을 받지 않는다
        WrongPhase     = 2, // 전실 밖에서 제출했다
        UnknownPlayer  = 3,
        TierOutOfRange = 4,
        PotionLimit    = 5,
        OverBudget     = 6,
        DataMissing    = 7, // BossTrialLoadoutSO가 없다
        ApplyFailed    = 8,
        LevelOutOfRange = 9,
        OptionInvalid   = 10, // 카탈로그에 없는 옵션이거나 값이 범위 밖이다
        TooManyTalents  = 11,
    }
}
