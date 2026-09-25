// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 데미지 숫자 장부가 쓰는 합산·밀도 규칙과 크기 선택(COMBAT_STYLE_DESIGN S10)
    /// 값은 PresentationConfigSO에서 읽는다. 테스트는 직접 만들어 넣는다
    /// </summary>
    public readonly struct DamageNumberRules
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 같은 키의 후속 피해를 기존 숫자에 더하는 창(초). 숫자 생성 시각부터 잰다 </summary>
        public readonly float MergeWindowSeconds;

        /// <summary> 화면에 동시에 있는 숫자 상한. 치명도 넘지 않는다 </summary>
        public readonly int MaxOnScreen;

        /// <summary> 같은 대상(netId) 숫자 상한 </summary>
        public readonly int MaxPerTarget;

        /****************************************
        *              Constructor
        ****************************************/

        public DamageNumberRules(float mergeWindowSeconds, int maxOnScreen, int maxPerTarget)
        {
            MergeWindowSeconds = Mathf.Max(0f, mergeWindowSeconds);
            MaxOnScreen        = Mathf.Max(1, maxOnScreen);
            MaxPerTarget       = Mathf.Max(1, maxPerTarget);
        }

        /****************************************
        *              Public API
        ****************************************/

        public static DamageNumberRules From(PresentationConfigSO config)
        {
            return new DamageNumberRules(config.DamageNumberMergeWindowSeconds,
                config.DamageNumberMaxOnScreen, config.DamageNumberMaxPerTarget);
        }

        /// <summary>
        /// 숫자의 진입 크기 배수. 치명 배수와 단계 배수(T2~T4)를 곱하지 않고 큰 쪽 하나만 쓴다
        /// T0·T1·T5는 단계 배수가 없다. T5는 공동 연출이라 숫자 크기를 키우지 않는다
        /// </summary>
        public static float ResolveScale(bool isCritical, ImpactTier tier, float criticalScale, float tierScaleMax)
        {
            float byCritical = (isCritical) ? Mathf.Max(1f, criticalScale) : 1f;
            bool  isRaised   = (tier >= ImpactTier.Medium) && (tier <= ImpactTier.Kill);
            float byTier     = (isRaised) ? Mathf.Max(1f, tierScaleMax) : 1f;

            return Mathf.Max(byCritical, byTier);
        }
    }
}
