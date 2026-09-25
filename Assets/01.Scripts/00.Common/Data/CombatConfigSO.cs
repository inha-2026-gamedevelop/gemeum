// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Geumeum.Data
{
    /// <summary> 레벨과 무관한 플레이어 전투 수치. 최대 HP는 LevelCurveSO가 단독으로 소유한다 </summary>
    [CreateAssetMenu(fileName = "CombatConfigSO", menuName = "Geumeum/CombatConfigSO")]
    public partial class CombatConfigSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("스태미나")]
        [SerializeField] private float _staminaMax            = 100f;
        [SerializeField] private float _staminaRegenPerSecond = 35f;
        [SerializeField] private float _staminaRegenDelay     = 0.45f; // 행동 후 회복 시작까지(초)

        [Header("일반 피격")]
        [Min(0f)] [SerializeField] private float _playerHitReactionDuration = 0.24f;
        [Min(0f)] [SerializeField] private float _playerHitReactionResistanceDuration = 0.35f;

        [Header("신력")]
        [SerializeField] private float _spiritMax = 80f; // 자연 회복 없음, 물약으로만 회복

        [Header("유효 전투 수치 제한")]
        [SerializeField] private float _minimumResourceCost = 1f;
        [SerializeField] private float _maximumAttackSpeedMultiplier = 1.5f;

        [Header("이동")]
        [SerializeField] private float _walkSpeed = 2.5f; // m/s
        [SerializeField] private float _runSpeed  = 5.5f; // m/s

        [Header("구르기")]
        [SerializeField] private float _rollStaminaCost        = 25f;
        [SerializeField] private float _rollInvincibleDuration = 0.45f; // 초
        [SerializeField] private float _rollTotalDuration      = 0.9f;  // 전체 동작(초)
        [SerializeField] private float _rollDistance           = 3f;    // 이동 거리(m), 게임 디자인 문서에 값이 없어 프로토타입 시작값으로 추가

        [Header("극한 회피 (COMBAT_STYLE_DESIGN S7)")]
        [Tooltip("구르기 무적 시작부터 이 시간 안에 무적으로 거부된 공격만 극한 회피로 인정한다(초). 판정 값이라 서버만 읽는다")]
        [Min(0f)] [SerializeField] private float _perfectDodgeWindowSeconds = 0.15f;

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

        [Header("근접 공격")]
        [FormerlySerializedAs("_attackStartupDuration")]
        [SerializeField] private float _meleeStartupDuration  = 0.12f; // 선딜(초)
        [FormerlySerializedAs("_attackRecoveryDuration")]
        [SerializeField] private float _meleeRecoveryDuration = 0.28f; // 후딜(초), 이 동안 다음 공격 불가
        [FormerlySerializedAs("_attackStaminaCost")]
        [SerializeField] private float _meleeStaminaCost      = 6f;  // 스태미나 비용
        [FormerlySerializedAs("_attackRange")]
        [SerializeField] private float _meleeRange            = 2.2f;  // 판정 거리(m)
        [FormerlySerializedAs("_attackAngle")]
        [SerializeField] private float _meleeAngle            = 90f;   // 정면 기준 좌우 합산 각도(도)
        [FormerlySerializedAs("_attackBasePower")]
        [SerializeField] private float _meleeBasePower        = 10f;   // 장비 보정 전 기본 공격력
        [SerializeField] private float _meleeComboWindowDuration = 0.45f; // 콤보 재입력 유예(초)

        [Header("원거리 공격")]
        [SerializeField] private float _rangedStartupDuration  = 0.25f; // 선딜(초), 활을 당기는 시간이라 근접보다 길다
        [SerializeField] private float _rangedRecoveryDuration = 0.35f; // 후딜(초)
        [SerializeField] private float _rangedStaminaCost      = 5f;    // 스태미나 비용
        [SerializeField] private float _rangedBasePower        = 8f;    // 장비 보정 전 기본 공격력
        [SerializeField] private float _rangedMuzzleHeight     = 1.2f;  // 발사 지점 높이(m), 발밑에서 나가지 않게 한다
        [SerializeField] private float _rangedMuzzleForward    = 0.6f;  // 발사 지점 전방 오프셋(m), 자기 몸에 걸리지 않게 한다

        [Header("정밀 사격")]
        [SerializeField] private float _precisionShotHoldDuration = 0.6f;
        [SerializeField] private float _precisionShotMoveSpeedMultiplier = 0.4f;
        [SerializeField] private float _precisionShotStaminaCost = 15f;

        [Header("신궁 쐐기")]
        [Min(0.01f)] [SerializeField] private float _wedgeDuration = 8f;
        [Min(0.01f)] [SerializeField] private float _wedgeRadius = 0.30f;
        [Min(1)] [SerializeField] private int _wedgeHits = 2;
        [Min(0f)] [SerializeField] private float _wedgeBurstMultiplier = 2f;

        [Header("무녀 되울림")]
        [Min(0.01f)] [SerializeField] private float _echoDuration = 8f;
        [Min(0f)] [SerializeField] private float _echoChargeInterval = 0.25f;
        [Min(0.01f)] [SerializeField] private float _echoRecallRange = 8f;
        [Min(0f)] [SerializeField] private float _echoSpiritCost = 10f;
        [Min(0.01f)] [SerializeField] private float _echoStartupDuration = 0.18f;
        [Min(0f)] [SerializeField] private float _echoRecoveryDuration = 0.30f;
        [Min(0f)] [SerializeField] private float _echoCooldown = 1.5f;
        [SerializeField] private Vector3 _echoDamageMultipliers = new Vector3(0.6f, 1f, 1.5f);

        public float WedgeDuration => _wedgeDuration;
        public float WedgeRadius => _wedgeRadius;
        public int WedgeHits => _wedgeHits;
        public float WedgeBurstMultiplier => _wedgeBurstMultiplier;
        public float EchoDuration => _echoDuration;
        public float EchoChargeInterval => _echoChargeInterval;
        public float EchoRecallRange => _echoRecallRange;
        public float EchoSpiritCost => _echoSpiritCost;
        public float EchoStartupDuration => _echoStartupDuration;
        public float EchoRecoveryDuration => _echoRecoveryDuration;
        public float EchoCooldown => _echoCooldown;
        public float EchoDamageMultiplier(int count) => _echoDamageMultipliers[Mathf.Clamp(count - 1, 0, 2)];

        [Header("신궁 연사")]
        [SerializeField] private float _burstShotInterval = 0.12f;

        [Header("방울 공격")]
        [SerializeField] private float _bellStartupDuration   = 0.20f; // 선딜(초)
        [SerializeField] private float _bellRecoveryDuration  = 0.35f; // 후딜(초)
        [SerializeField] private float _bellStaminaCost       = 6f;    // 스태미나 비용
        [SerializeField] private float _bellRange             = 3.5f;  // 판정 반경(m), 자기 주위 전부
        [SerializeField] private float _bellAngle             = 120f;  // 판정 각도(도), 등 뒤도 맞는다
        [SerializeField] private float _bellBasePower         = 6f;    // 장비 보정 전 기본 공격력, 근접의 0.6배
        [SerializeField] private float _spiritGainPerHit      = 5f;    // 한 명 맞힐 때 얻는 신력
        [SerializeField] private float _spiritGainMaxPerSwing = 15f;   // 한 번 휘둘러 얻을 수 있는 상한

        [Header("표식")]
        [SerializeField] private float _markDuration  = 8f; // 유지 시간(초). 같은 대상 재부여 쿨다운도 이 값을 쓴다
        [SerializeField] private int   _markSlotCount = 2;  // 한 사람이 동시에 유지하는 수. 넘으면 가장 오래된 것이 밀린다

        [Header("범위 치유")]
        [SerializeField] private float _healSpiritCost        = 25f;   // 신력 비용
        [SerializeField] private float _healCastDuration      = 0.4f;  // 시전 시간(초), 이 동안 움직이면 끊긴다
        [SerializeField] private float _healRecoveryDuration  = 0.4f;  // 후딜(초)
        [SerializeField] private float _healRadius            = 6f;    // 회복 반경(m)
        [SerializeField] private float _healAmount            = 40f;   // 대상 한 명이 받는 회복량
        [SerializeField] private float _healCooldownDuration  = 4f;    // 쿨다운(초)

        [Header("락온")]
        [SerializeField] private float _lockOnAcquireRange = 25f; // 걸 수 있는 거리(m), 서버도 이 값으로 검증한다
        [SerializeField] private float _lockOnHoldRange    = 30f; // 유지 거리(m), 경계에서 붙었다 떨어지지 않게 더 넓다
        [SerializeField] private float _lockOnAcquireAngle = 35f; // 화면 중심 기준 탐색 반각(도)
        [SerializeField] private float _lockOnHoldAngle    = 55f; // 유지 반각(도), 짧은 회피나 카메라 흔들림에 안 풀리게 한다
        [SerializeField] private float _lockOnBlockedGrace = 0.5f;  // 시야가 막힌 채 이 시간을 넘기면 다음 대상으로(초)
        [SerializeField] private float _lockOnSwitchCooldown = 1f;  // 자동 재전환 금지(초), 바위 뒤를 들락날락할 때 대상이 계속 바뀌는 것을 막는다
        [SerializeField] private float _lockOnTurnDegreesPerSecond = 1080f; // 락온 중 모델 회전 속도(도/초), 비락온은 720

        // 부활 수치는 이 SO가 소유한다 (2026-09-07 확정). 재화 차감 비율은 CurrencyManager가 생기면 여기 값을 읽는다
        [Header("부활")]
        [SerializeField] private float _spectateDuration         = 30f;  // 사망 후 관전(초)
        [SerializeField] private float _reviveHealthRatio        = 0.5f; // 최대 HP 대비 회복 비율
        [SerializeField] private float _reviveInvincibleDuration = 2f;   // 남은 잡귀에게 즉사하는 것을 막는다(초)
        [SerializeField] private float _reviveCurrencyLossRatio  = 0.2f; // 팀 공용 노잣돈 차감 비율(자동 부활)

        [Header("협동 부활")]
        [SerializeField] private float _rescueRadius            = 3f;   // 쓰러진 팀원에게 다가가야 하는 거리(m)
        [SerializeField] private float _rescueDuration          = 3f;   // 상호작용 유지 시간(초)
        [SerializeField] private float _rescueCurrencyLossRatio = 0.1f; // 구조 부활의 노잣돈 차감(자동의 절반)
        [SerializeField] private float _reviveBarrierInsetRatio = 0.9f; // 결계 밖에서 죽으면 반경의 이 비율 지점으로 당긴다

        [Header("상호작용 (게임 디자인 6.5)")]
        // 줍기, 양도, 객주가 같은 상호작용 거리를 사용한다
        [SerializeField] private float _interactRange = 3f; // 상호작용 최대 거리(m)
        // 피해를 주거나 받은 뒤 이만큼 지나야 상호작용이 열린다. 협동 부활은 이 조건에서 제외한다
        [SerializeField] private float _interactOutOfCombatSeconds = 5f;
        [SerializeField] private float _equipmentTransferOfferDuration = 30f;

        /****************************************
        *              Properties
        ****************************************/

        public float StaminaMax            => _staminaMax;
        public float StaminaRegenPerSecond => _staminaRegenPerSecond;
        public float StaminaRegenDelay     => _staminaRegenDelay;

        public float SpiritMax => _spiritMax;
        public float PlayerHitReactionDuration => _playerHitReactionDuration;
        public float PlayerHitReactionResistanceDuration => _playerHitReactionResistanceDuration;

        /// <summary> 임시 밸런스 1. 에셋의 0은 이전 데이터 호환을 위해 하한 없음으로 해석한다 </summary>
        public float MinimumResourceCost => _minimumResourceCost;

        /// <summary> 임시 밸런스 1.5. 에셋의 0은 이전 데이터 호환을 위해 상한 없음으로 해석한다 </summary>
        public float MaximumAttackSpeedMultiplier => _maximumAttackSpeedMultiplier;

        public float WalkSpeed => _walkSpeed;
        public float RunSpeed  => _runSpeed;

        public float RollStaminaCost        => _rollStaminaCost;
        public float RollInvincibleDuration => _rollInvincibleDuration;
        public float RollTotalDuration      => _rollTotalDuration;
        public float RollDistance           => _rollDistance;

        /// <summary> 극한 회피로 인정하는 구르기 무적 앞쪽 구간(초). 구르기 무적 길이를 넘으면 무적 길이까지만 의미가 있다 </summary>
        public float PerfectDodgeWindowSeconds => Mathf.Max(0f, _perfectDodgeWindowSeconds);

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
        public float MeleeComboWindowDuration => _meleeComboWindowDuration;

        public float RangedStartupDuration  => _rangedStartupDuration;
        public float RangedRecoveryDuration => _rangedRecoveryDuration;
        public float RangedStaminaCost      => _rangedStaminaCost;
        public float RangedBasePower        => _rangedBasePower;
        public float RangedMuzzleHeight     => _rangedMuzzleHeight;
        public float RangedMuzzleForward    => _rangedMuzzleForward;

        public float PrecisionShotHoldDuration => _precisionShotHoldDuration;
        public float PrecisionShotMoveSpeedMultiplier => _precisionShotMoveSpeedMultiplier;
        public float PrecisionShotStaminaCost => _precisionShotStaminaCost;

        public float BurstShotInterval => _burstShotInterval;

        public float BellStartupDuration   => _bellStartupDuration;
        public float BellRecoveryDuration  => _bellRecoveryDuration;
        public float BellStaminaCost       => _bellStaminaCost;
        public float BellRange             => _bellRange;
        public float BellAngle             => _bellAngle;
        public float BellBasePower         => _bellBasePower;
        public float SpiritGainPerHit      => _spiritGainPerHit;
        public float SpiritGainMaxPerSwing => _spiritGainMaxPerSwing;

        public float MarkDuration  => _markDuration;
        public int   MarkSlotCount => _markSlotCount;

        public float HealSpiritCost       => _healSpiritCost;
        public float HealCastDuration     => _healCastDuration;
        public float HealRecoveryDuration => _healRecoveryDuration;
        public float HealRadius           => _healRadius;
        public float HealAmount           => _healAmount;
        public float HealCooldownDuration => _healCooldownDuration;

        public float LockOnAcquireRange          => _lockOnAcquireRange;
        public float LockOnHoldRange             => _lockOnHoldRange;
        public float LockOnAcquireAngle          => _lockOnAcquireAngle;
        public float LockOnHoldAngle             => _lockOnHoldAngle;
        public float LockOnBlockedGrace          => _lockOnBlockedGrace;
        public float LockOnSwitchCooldown        => _lockOnSwitchCooldown;
        public float LockOnTurnDegreesPerSecond  => _lockOnTurnDegreesPerSecond;

        public float SpectateDuration         => _spectateDuration;
        public float ReviveHealthRatio        => _reviveHealthRatio;
        public float ReviveInvincibleDuration => _reviveInvincibleDuration;
        public float ReviveCurrencyLossRatio  => _reviveCurrencyLossRatio;

        public float RescueRadius            => _rescueRadius;
        public float RescueDuration          => _rescueDuration;
        public float RescueCurrencyLossRatio => _rescueCurrencyLossRatio;
        public float ReviveBarrierInsetRatio => _reviveBarrierInsetRatio;

        public float InteractRange              => _interactRange;
        public float InteractOutOfCombatSeconds => _interactOutOfCombatSeconds;
        public float EquipmentTransferOfferDuration => _equipmentTransferOfferDuration;
    }
}
