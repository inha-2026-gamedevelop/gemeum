// System
using System;

namespace Geumeum.Common
{
    /// <summary> 섬광이 알리는 의미. 패링 가능(창이 열림)과 패링 불가(피해야 함)를 모양, 색, 소리로 가른다 </summary>
    public enum ParryFlashKind : byte
    {
        Parryable   = 0,
        Unparryable = 1,
    }

    /// <summary>
    /// 타이밍 섬광(COMBAT_STYLE_DESIGN S4)의 순수 시각, 모양 계산. Unity 객체를 만지지 않아 EditMode에서 검증한다
    /// 모든 시각은 NetworkTime.time 기준(서버 시계)이다
    /// </summary>
    public static class ParryFlashTiming
    {
        /****************************************
        *              Constants
        ****************************************/

        // 대상 창 끝 + PARRY_RESOLUTION_LEAD와 cue impact를 같은 타격으로 볼 허용 오차(초)
        // 둘 다 서버의 같은 double 식(공격 시작 + impact 오프셋)에서 나오므로 부동소수 반올림만 흡수한다. 밸런스 값이 아니다
        public const double IMPACT_MATCH_EPSILON = 0.001;

        private const ulong KIND_TAG_CUE_PARRYABLE   = 1ul << 62;
        private const ulong KIND_TAG_CUE_UNPARRYABLE = 2ul << 62;
        private const ulong KIND_TAG_EXACT_WINDOW    = 3ul << 62;
        private const int   ATTACK_ID_SHIFT          = 8;
        private const int   ATTACKER_ID_SHIFT        = 32;
        private const ulong LOW_32_MASK              = 0xFFFF_FFFFul;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 비대상이 보는 패링 창의 근사 구간. 비대상은 대상의 확정 창(로드아웃 보너스 포함)을 받지 못하므로
        /// 서버 ParryController와 같은 역산식에 기본 창 길이만 넣는다. 실제 창과 앞쪽이 어긋날 수 있다
        /// </summary>
        public static void ApproximateWindow(double impactServerTime, double baseWindowSeconds,
            double resolutionLeadSeconds, out double start, out double end)
        {
            end   = impactServerTime - Math.Max(0d, resolutionLeadSeconds);
            start = end - Math.Max(0d, baseWindowSeconds);
        }

        /// <summary> 패링 불가 경고가 시작되는 시각. 창이 없으므로 서버 impact에서 경고 선행시간만큼 당긴다 </summary>
        public static double UnparryableWarningStart(double impactServerTime, double warningLeadSeconds)
        {
            return impactServerTime - Math.Max(0d, warningLeadSeconds);
        }

        /// <summary> 대상의 확정 창과 보스 cue가 같은 타격인지. 끝 + 해소 여유가 cue impact와 같아야 한다 </summary>
        public static bool MatchesImpact(double windowEndServerTime, double resolutionLeadSeconds,
            double cueImpactServerTime)
        {
            return Math.Abs((windowEndServerTime + resolutionLeadSeconds) - cueImpactServerTime) <=
                IMPACT_MATCH_EPSILON;
        }

        /// <summary> 패링 가능 모양: 짧은 한 번의 반짝. 시작에 최대, 끝으로 빠르게 꺼진다 </summary>
        public static float SingleBlink(float normalizedTime)
        {
            if ((normalizedTime < 0f) || (normalizedTime >= 1f))
            {
                return 0f;
            }

            float remain = 1f - normalizedTime;
            return remain * remain;
        }

        /// <summary> 패링 불가 모양: 두 번 번지는 빛. 부드럽게 올라왔다 내려가는 봉우리 두 개 </summary>
        public static float DoubleSpread(float normalizedTime)
        {
            if ((normalizedTime < 0f) || (normalizedTime >= 1f))
            {
                return 0f;
            }

            float wave = (float) Math.Sin(normalizedTime * Math.PI * 2d);
            return wave * wave;
        }

        /// <summary> 모양 하나가 재생되는 길이. 불가 경고는 봉우리 두 개라 반짝임 두 번 길이다 </summary>
        public static float ShapeSeconds(ParryFlashKind kind, float flashSeconds)
        {
            float single = Math.Max(0f, flashSeconds);
            return (kind == ParryFlashKind.Unparryable) ? (single * 2f) : single;
        }

        /// <summary> 모양 값(0~1). 시작 이후 경과 시간과 모양 길이로 계산한다 </summary>
        public static float EvaluateShape(ParryFlashKind kind, double elapsedSeconds, float shapeSeconds)
        {
            if ((shapeSeconds <= 0f) || (elapsedSeconds < 0d))
            {
                return 0f;
            }

            float t = (float) (elapsedSeconds / shapeSeconds);
            return (kind == ParryFlashKind.Unparryable) ? DoubleSpread(t) : SingleBlink(t);
        }

        /// <summary> 대상은 그대로, 비대상은 nonTargetScale배로 낮춘 세기. 배수는 0~1로 자른다 </summary>
        public static float AudienceWeight(float weight, bool isTarget, float nonTargetScale)
        {
            return isTarget ? weight : (weight * ClampScale(nonTargetScale));
        }

        /// <summary> 비대상 배수를 0~1로 자른다. 색 밝기와 세기가 같은 배수를 쓴다 </summary>
        public static float ClampScale(float nonTargetScale)
        {
            return Math.Max(0f, Math.Min(1f, nonTargetScale));
        }

        /// <summary> 보스 cue 한 타(공격 인스턴스 + 반복 순서)의 스케줄 키 </summary>
        public static ulong CueKey(uint attackInstanceId, byte repeatIndex, ParryFlashKind kind)
        {
            ulong tag = (kind == ParryFlashKind.Unparryable) ? KIND_TAG_CUE_UNPARRYABLE : KIND_TAG_CUE_PARRYABLE;
            return tag | ((ulong) attackInstanceId << ATTACK_ID_SHIFT) | repeatIndex;
        }

        /// <summary> 대상이 받은 확정 창의 스케줄 키. 공격자와 창 ID를 함께 묶는다 </summary>
        public static ulong ExactWindowKey(uint attackerNetId, uint windowId)
        {
            // 태그 2비트를 쓰므로 공격자 ID 상위 2비트는 버린다. 한 뷰 안에서는 공격자가 하나라 충돌하지 않는다
            ulong attacker = ((ulong) attackerNetId << ATTACKER_ID_SHIFT) & ~(3ul << 62);
            return KIND_TAG_EXACT_WINDOW | attacker | (windowId & LOW_32_MASK);
        }

        /// <summary> 스케줄 키가 대상 확정 창인지 </summary>
        public static bool IsExactWindowKey(ulong key)
        {
            return (key & (3ul << 62)) == KIND_TAG_EXACT_WINDOW;
        }
    }
}
