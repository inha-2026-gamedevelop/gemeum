// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 타격 강도 사다리·데미지 숫자·타이밍 섬광·극한 회피 표현 수치(COMBAT_STYLE_DESIGN 5·6·9장)
    /// 전부 각자 화면의 표현 값이다. 판정 값(패링 창, 극한 회피 구간)은 CombatConfigSO에 둔다
    /// </summary>
    public partial class PresentationConfigSO
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 단계 하나의 표현 채널. 셰이크 모양은 CameraConfigSO가 따로 가진다 </summary>
        [Serializable]
        public struct ImpactTierSpec
        {
            [Tooltip("내 캐릭터 애니메이터 멈춤(초)")]
            public float AttackerHitStopSeconds;

            [Tooltip("맞은 잡귀 애니메이터 멈춤(초). 내 화면에서만. T4 값은 지금 쓰이지 않는다 - 잡귀는 처치 즉시 서버가 파괴하고 보스는 T4가 아니다")]
            public float TargetHitStopSeconds;

            [Tooltip("맞은 보스 멈춤(초). 첫 도입은 0 - 예고·공격 중에는 어떤 값이어도 멈추지 않는다")]
            public float BossTargetHitStopSeconds;

            [Tooltip("맞은 방향으로 상체가 젖혀지는 각도(도). 동료 화면의 관찰 움찔도 같은 값을 쓴다. T4 값은 같은 이유로 쓰이지 않는다")]
            public float FlinchDegrees;

            [Tooltip("움찔이 돌아오기까지(초)")]
            public float FlinchSeconds;

            [Tooltip("화면 가장자리 플래시 길이(초). 0이면 없음")]
            public float ScreenFlashSeconds;

            [Tooltip("화면 가장자리 플래시 최대 불투명도")]
            [Range(0f, 1f)]
            public float ScreenFlashAlpha;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("타격 강도 사다리 - ImpactTier 순서(T0~T5)")]
        [SerializeField] private ImpactTierSpec[] _impactTiers =
        {
            new ImpactTierSpec(), // T0 헛스윙
            new ImpactTierSpec { AttackerHitStopSeconds = 0.04f, TargetHitStopSeconds = 0.04f, FlinchDegrees = 4f,  FlinchSeconds = 0.12f },
            new ImpactTierSpec { AttackerHitStopSeconds = 0.06f, TargetHitStopSeconds = 0.06f, FlinchDegrees = 7f,  FlinchSeconds = 0.15f },
            new ImpactTierSpec { AttackerHitStopSeconds = 0.08f, TargetHitStopSeconds = 0.08f, FlinchDegrees = 10f, FlinchSeconds = 0.18f, ScreenFlashSeconds = 0.06f, ScreenFlashAlpha = 0.25f },
            // T4의 대상 멈춤, 움찔은 지금 쓰이지 않는다. 잡귀는 처치 즉시 서버가 파괴해 결과가 도착할 때 대상이 없고,
            // 보스는 처치 강조(T4) 대상이 아니다. 클라이언트에 남는 사망 표현(시체, 디졸브)이 생기면 그때 이 값이 살아난다
            new ImpactTierSpec { AttackerHitStopSeconds = 0.10f, TargetHitStopSeconds = 0.10f, FlinchDegrees = 10f, FlinchSeconds = 0.18f, ScreenFlashSeconds = 0.06f, ScreenFlashAlpha = 0.25f },
            new ImpactTierSpec { ScreenFlashSeconds = 0.10f, ScreenFlashAlpha = 0.35f },
        };

        [Tooltip("차사(검) 멈춤 배수")]
        [SerializeField, Min(0f)] private float _swordHitStopScale = 1f;

        [Tooltip("신궁 원거리 공격의 내 캐릭터 멈춤 배수. 손이 멈추면 조준이 끊긴다 - R 찌르기는 근접 예외로 따로 본다")]
        [SerializeField, Min(0f)] private float _bowRangedAttackerHitStopScale = 0f;

        [Tooltip("무녀(방울) 멈춤 배수. 멈춤보다 울림이 길다")]
        [SerializeField, Min(0f)] private float _bellHitStopScale = 0.5f;

        [Tooltip("같은 대상 멈춤이 첫 요청부터 넘지 않는 길이(초)")]
        [SerializeField, Min(0f)] private float _targetHitStopMaxSeconds = 0.10f;

        [Tooltip("같은 대상 멈춤이 끝난 뒤 다시 걸리지 않는 유예(초)")]
        [SerializeField, Min(0f)] private float _targetHitStopCooldownSeconds = 0.10f;

        [Tooltip("같은 적의 움찔이 다시 걸리지 않는 간격(초). 3인 연타가 떨림으로 보이지 않게. 서버가 동료 화면 움찔을 대상별로 보내는 최소 간격도 이 값이다")]
        [SerializeField, Min(0f)] private float _flinchRepeatGuardSeconds = 0.10f;

        [Tooltip("이 시간 안에 두 번째 처치부터는 T2로 낮춘다(초). 서버 시각(NetworkTime.time) 기준. 잡귀 떼에서 화면이 쉬지 않는 것을 막는다")]
        [SerializeField, Min(0f)] private float _killChainWindowSeconds = 0.5f;

        [Tooltip("서버 시각(NetworkTime.time) 기준 이보다 늦게 도착한 개인 표현(멈춤, 셰이크, 플래시, 극한 회피)은 버린다(초). 데미지 숫자와 피해 자체와는 무관하다")]
        [SerializeField, Min(0f)] private float _lateResultDiscardSeconds = 0.5f;

        [Tooltip("화면 가장자리 플래시 색")]
        [SerializeField] private Color _screenFlashColor = new Color(1f, 0.93f, 0.8f, 1f);

        [Header("데미지 숫자 (S10)")]
        [SerializeField] private bool _damageNumbersEnabledByDefault = true;
        [SerializeField] private Color _damageNumberNormalColor   = new Color(1f, 0.96f, 0.88f, 1f);
        [SerializeField] private Color _damageNumberCriticalColor = new Color(1f, 0.78f, 0.25f, 1f);
        [Tooltip("1080p 기준 글자 크기(px)")]
        [SerializeField, Min(1f)] private float _damageNumberFontSize = 24f;
        [SerializeField, Min(0.05f)] private float _damageNumberLifetimeSeconds = 0.65f;
        [Tooltip("수명 동안 위로 뜨는 거리(1080p 기준 px)")]
        [SerializeField, Min(0f)] private float _damageNumberRisePixels = 48f;
        [Tooltip("같은 자리 숫자가 겹치지 않게 좌우로 흩는 폭(1080p 기준 px)")]
        [SerializeField, Min(0f)] private float _damageNumberSpreadPixels = 18f;
        [SerializeField, Min(1f)] private float _damageNumberCriticalScale = 1.2f;
        [Tooltip("T2~T4 진입 크기 상한. 치명 배수와 곱하지 않고 큰 쪽 하나만 쓴다")]
        [SerializeField, Min(1f)] private float _damageNumberTierScaleMax = 1.2f;
        [Tooltip("같은 공격자·실행·대상·치명 여부의 후속 피해를 합치는 창(초)")]
        [SerializeField, Min(0f)] private float _damageNumberMergeWindowSeconds = 0.08f;
        [SerializeField, Min(1)] private int _damageNumberMaxOnScreen = 24;
        [SerializeField, Min(1)] private int _damageNumberMaxPerTarget = 4;
        [Tooltip("조준점 보호 반경(화면 높이 비율). 이 안에는 숫자를 두지 않는다")]
        [SerializeField, Range(0f, 0.5f)] private float _damageNumberCenterGuard = 0.08f;
        [Tooltip("글자 외곽선 두께(TMP OutlineWidth, 0~1)")]
        [SerializeField, Range(0f, 1f)] private float _damageNumberOutlineWidth = 0.25f;
        [Tooltip("수명 중 이 비율부터 흐려진다(0~1)")]
        [SerializeField, Range(0f, 1f)] private float _damageNumberFadeStart = 0.6f;

        [Header("타이밍 섬광 (S4)")]
        [SerializeField] private Color _parryFlashParryableColor   = new Color(0.35f, 0.7f, 1f, 1f);
        [SerializeField] private Color _parryFlashUnparryableColor = new Color(1f, 0.25f, 0.2f, 1f);
        [Tooltip("패링 가능 반짝임 길이(초)")]
        [SerializeField, Min(0.01f)] private float _parryFlashSeconds = 0.12f;
        [Tooltip("패링 불가 경고가 타격보다 먼저 뜨는 시간(초). 패링 창이 없으므로 따로 정한다")]
        [SerializeField, Min(0f)] private float _unparryableWarningLeadSeconds = 0.35f;
        [Tooltip("공격 대상이 아닌 사람이 보는 섬광 크기·음량 배수")]
        [SerializeField, Range(0f, 1f)] private float _parryFlashNonTargetScale = 0.5f;

        [Header("극한 회피 · 공동 순간")]
        [SerializeField, Min(0f)] private float _perfectDodgeVignetteSeconds = 0.3f;
        [SerializeField, Range(0f, 1f)] private float _perfectDodgeVignetteAlpha = 0.35f;
        [Tooltip("그로기 진입·꼬리 파괴 때 HUD 게이지가 번쩍이는 길이(초)")]
        [SerializeField, Min(0f)] private float _partyMomentHudPulseSeconds = 0.4f;

        [Header("받아베기 먹 번짐 (S6)")]
        [Tooltip("표식 3개 받아베기의 먹 번짐 오버레이 길이(초). 입력은 잠그지 않는다")]
        [SerializeField, Min(0f)] private float _counterInkSeconds = 0.25f;
        [Tooltip("먹 번짐 색. 거의 검정에 약간 푸른 먹색")]
        [SerializeField] private Color _counterInkColor = new Color(0.03f, 0.05f, 0.09f, 1f);
        [Tooltip("먹 번짐이 가장 짙을 때의 불투명도")]
        [SerializeField, Range(0f, 1f)] private float _counterInkAlpha = 0.6f;

        /****************************************
        *              Properties
        ****************************************/

        public float SwordHitStopScale             => Mathf.Max(0f, _swordHitStopScale);
        public float BowRangedAttackerHitStopScale => Mathf.Max(0f, _bowRangedAttackerHitStopScale);
        public float BellHitStopScale              => Mathf.Max(0f, _bellHitStopScale);
        public float TargetHitStopMaxSeconds       => Mathf.Max(0f, _targetHitStopMaxSeconds);
        public float TargetHitStopCooldownSeconds  => Mathf.Max(0f, _targetHitStopCooldownSeconds);
        public float FlinchRepeatGuardSeconds      => Mathf.Max(0f, _flinchRepeatGuardSeconds);
        public float KillChainWindowSeconds        => Mathf.Max(0f, _killChainWindowSeconds);
        public float LateResultDiscardSeconds      => Mathf.Max(0f, _lateResultDiscardSeconds);
        public Color ScreenFlashColor              => _screenFlashColor;

        public bool  DamageNumbersEnabledByDefault  => _damageNumbersEnabledByDefault;
        public Color DamageNumberNormalColor        => _damageNumberNormalColor;
        public Color DamageNumberCriticalColor      => _damageNumberCriticalColor;
        public float DamageNumberFontSize           => Mathf.Max(1f, _damageNumberFontSize);
        public float DamageNumberLifetimeSeconds    => Mathf.Max(0.05f, _damageNumberLifetimeSeconds);
        public float DamageNumberRisePixels         => Mathf.Max(0f, _damageNumberRisePixels);
        public float DamageNumberSpreadPixels       => Mathf.Max(0f, _damageNumberSpreadPixels);
        public float DamageNumberCriticalScale      => Mathf.Max(1f, _damageNumberCriticalScale);
        public float DamageNumberTierScaleMax       => Mathf.Max(1f, _damageNumberTierScaleMax);
        public float DamageNumberMergeWindowSeconds => Mathf.Max(0f, _damageNumberMergeWindowSeconds);
        public int   DamageNumberMaxOnScreen        => Mathf.Max(1, _damageNumberMaxOnScreen);
        public int   DamageNumberMaxPerTarget       => Mathf.Max(1, _damageNumberMaxPerTarget);
        public float DamageNumberCenterGuard        => Mathf.Clamp(_damageNumberCenterGuard, 0f, 0.5f);
        public float DamageNumberOutlineWidth       => Mathf.Clamp01(_damageNumberOutlineWidth);
        public float DamageNumberFadeStart          => Mathf.Clamp01(_damageNumberFadeStart);

        public Color ParryFlashParryableColor      => _parryFlashParryableColor;
        public Color ParryFlashUnparryableColor    => _parryFlashUnparryableColor;
        public float ParryFlashSeconds             => Mathf.Max(0.01f, _parryFlashSeconds);
        public float UnparryableWarningLeadSeconds => Mathf.Max(0f, _unparryableWarningLeadSeconds);
        public float ParryFlashNonTargetScale      => Mathf.Clamp01(_parryFlashNonTargetScale);

        public float PerfectDodgeVignetteSeconds => Mathf.Max(0f, _perfectDodgeVignetteSeconds);
        public float PerfectDodgeVignetteAlpha   => Mathf.Clamp01(_perfectDodgeVignetteAlpha);
        public float PartyMomentHudPulseSeconds  => Mathf.Max(0f, _partyMomentHudPulseSeconds);

        public float CounterInkSeconds => Mathf.Max(0f, _counterInkSeconds);
        public Color CounterInkColor   => _counterInkColor;
        public float CounterInkAlpha   => Mathf.Clamp01(_counterInkAlpha);

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 단계의 표현 채널. 에셋의 배열이 짧으면 빈 값(아무 표현 없음)을 준다 </summary>
        public ImpactTierSpec GetImpactTier(ImpactTier tier)
        {
            int index = (int)tier;

            if ((_impactTiers == null) || (index < 0) || (index >= _impactTiers.Length))
            {
                return default;
            }

            return _impactTiers[index];
        }
    }
}
