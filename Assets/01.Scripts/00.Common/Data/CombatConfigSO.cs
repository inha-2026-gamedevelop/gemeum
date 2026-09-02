// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Geumeum.Data
{
    /// <summary> 레벨과 무관한 플레이어 전투 수치. 최대 HP는 LevelCurveSO가 단독으로 소유한다 </summary>
    [CreateAssetMenu(fileName = "CombatConfigSO", menuName = "Geumeum/CombatConfigSO")]
    public class CombatConfigSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("스태미나")]
        [SerializeField] private float _staminaMax            = 100f;
        [SerializeField] private float _staminaRegenPerSecond = 25f;
        [SerializeField] private float _staminaRegenDelay     = 0.6f; // 행동 후 회복 시작까지(초)

        [Header("신력")]
        [SerializeField] private float _spiritMax = 80f; // 자연 회복 없음, 물약으로만 회복

        [Header("이동")]
        [SerializeField] private float _walkSpeed = 2.5f; // m/s
        [SerializeField] private float _runSpeed  = 5.5f; // m/s

        [Header("구르기")]
        [SerializeField] private float _rollStaminaCost        = 25f;
        [SerializeField] private float _rollInvincibleDuration = 0.45f; // 초
        [SerializeField] private float _rollTotalDuration      = 0.9f;  // 전체 동작(초)
        [SerializeField] private float _rollDistance           = 3f;    // 이동 거리(m), 게임 디자인 문서에 값이 없어 프로토타입 시작값으로 추가

        [Header("패링")]
        [SerializeField] private float _parryStartupDuration    = 0.05f; // 선딜(초)
        [SerializeField] private float _parryWindowDuration     = 0.32f; // 기본 판정 창(초)
        [SerializeField] private float _parryWindowDurationMax  = 0.48f; // 특성, 장비로 확대되는 상한(초)
        [SerializeField] private float _parryRecoveryDuration   = 0.35f; // 실패 시 후딜(초)
        [SerializeField] private float _parryStaminaCost        = 15f;
        [SerializeField] private float _parryInvincibleDuration = 0.3f;  // 성공 시 무적(초)

        [Header("패링 게이지")]
        [SerializeField] private float _parryGaugeMax            = 100f; // 도달하면 다음 공격이 치명의 일격
        [SerializeField] private float _parryGaugeGainPerSuccess = 35f;
        [SerializeField] private float _criticalMultiplierMin    = 2.0f;
        [SerializeField] private float _criticalMultiplierMax    = 3.0f;

        // 게임 디자인 문서에 공격 수치표가 아직 없어 프로토타입 시작값으로 넣는다.
        // 밸런싱은 여기 값만 고치면 되고 코드를 다시 빌드하지 않는다.
        // FormerlySerializedAs는 근접, 원거리를 나누기 전 _attack* 이름으로 저장된 에셋 값을 잇는다
        [Header("근접 공격")]
        [FormerlySerializedAs("_attackStartupDuration")]
        [SerializeField] private float _meleeStartupDuration  = 0.12f; // 선딜(초)
        [FormerlySerializedAs("_attackRecoveryDuration")]
        [SerializeField] private float _meleeRecoveryDuration = 0.28f; // 후딜(초), 이 동안 다음 공격 불가
        [FormerlySerializedAs("_attackStaminaCost")]
        [SerializeField] private float _meleeStaminaCost      = 12f;
        [FormerlySerializedAs("_attackRange")]
        [SerializeField] private float _meleeRange            = 2.2f;  // 판정 거리(m)
        [FormerlySerializedAs("_attackAngle")]
        [SerializeField] private float _meleeAngle            = 90f;   // 정면 기준 좌우 합산 각도(도)
        [FormerlySerializedAs("_attackBasePower")]
        [SerializeField] private float _meleeBasePower        = 10f;   // 장비 보정 전 기본 공격력

        // 화살 비행, 유도, 명중 보조 수치는 ArrowConfigSO가 소유한다. 여기는 쏘는 쪽 수치만 둔다
        [Header("원거리 공격")]
        [SerializeField] private float _rangedStartupDuration  = 0.25f; // 선딜(초), 활을 당기는 시간이라 근접보다 길다
        [SerializeField] private float _rangedRecoveryDuration = 0.35f; // 후딜(초)
        [SerializeField] private float _rangedStaminaCost      = 8f;    // 거리를 두고 싸우는 대신 근접보다 싸다
        [SerializeField] private float _rangedBasePower        = 8f;    // 장비 보정 전 기본 공격력
        [SerializeField] private float _rangedMuzzleHeight     = 1.2f;  // 발사 지점 높이(m), 발밑에서 나가지 않게 한다
        [SerializeField] private float _rangedMuzzleForward    = 0.6f;  // 발사 지점 전방 오프셋(m), 자기 몸에 걸리지 않게 한다

        // TODO(민성): 부활 수치를 어느 SO가 소유할지 팀에서 확정할 것
        [Header("부활")]
        [SerializeField] private float _spectateDuration         = 30f;  // 사망 후 관전(초)
        [SerializeField] private float _reviveHealthRatio        = 0.5f; // 최대 HP 대비 회복 비율
        [SerializeField] private float _reviveInvincibleDuration = 2f;   // 남은 잡귀에게 즉사하는 것을 막는다(초)
        [SerializeField] private float _reviveCurrencyLossRatio  = 0.2f; // 팀 공용 노잣돈 차감 비율

        /****************************************
        *              Properties
        ****************************************/

        public float StaminaMax            => _staminaMax;
        public float StaminaRegenPerSecond => _staminaRegenPerSecond;
        public float StaminaRegenDelay     => _staminaRegenDelay;

        public float SpiritMax => _spiritMax;

        public float WalkSpeed => _walkSpeed;
        public float RunSpeed  => _runSpeed;

        public float RollStaminaCost        => _rollStaminaCost;
        public float RollInvincibleDuration => _rollInvincibleDuration;
        public float RollTotalDuration      => _rollTotalDuration;
        public float RollDistance           => _rollDistance;

        public float ParryStartupDuration    => _parryStartupDuration;
        public float ParryWindowDuration     => _parryWindowDuration;
        public float ParryWindowDurationMax  => _parryWindowDurationMax;
        public float ParryRecoveryDuration   => _parryRecoveryDuration;
        public float ParryStaminaCost        => _parryStaminaCost;
        public float ParryInvincibleDuration => _parryInvincibleDuration;

        public float ParryGaugeMax            => _parryGaugeMax;
        public float ParryGaugeGainPerSuccess => _parryGaugeGainPerSuccess;
        public float CriticalMultiplierMin    => _criticalMultiplierMin;
        public float CriticalMultiplierMax    => _criticalMultiplierMax;

        public float MeleeStartupDuration  => _meleeStartupDuration;
        public float MeleeRecoveryDuration => _meleeRecoveryDuration;
        public float MeleeStaminaCost      => _meleeStaminaCost;
        public float MeleeRange            => _meleeRange;
        public float MeleeAngle            => _meleeAngle;
        public float MeleeBasePower        => _meleeBasePower;

        public float RangedStartupDuration  => _rangedStartupDuration;
        public float RangedRecoveryDuration => _rangedRecoveryDuration;
        public float RangedStaminaCost      => _rangedStaminaCost;
        public float RangedBasePower        => _rangedBasePower;
        public float RangedMuzzleHeight     => _rangedMuzzleHeight;
        public float RangedMuzzleForward    => _rangedMuzzleForward;

        public float SpectateDuration         => _spectateDuration;
        public float ReviveHealthRatio        => _reviveHealthRatio;
        public float ReviveInvincibleDuration => _reviveInvincibleDuration;
        public float ReviveCurrencyLossRatio  => _reviveCurrencyLossRatio;
    }
}
