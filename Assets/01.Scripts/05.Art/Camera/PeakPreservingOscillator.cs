// System
using System;

namespace Geumeum.Art
{
    /// <summary>
    /// 프레임이 달라도 같은 세기로 흔들리는 사인 진동기
    /// 그냥 sin(t)를 샘플링하면 낮은 프레임에서 극값을 건너뛰어 흔들림이 약해진다. 그래서
    /// ① 이전 샘플과 이번 샘플 사이에 극값이 있었으면 극값을 샘플로 쓰고
    /// ② 그 극값의 부호가 직전 극값과 같으면 강제로 뒤집는다 - 짝수 개를 건너뛰면 한쪽으로만 쏠리기 때문이다
    /// </summary>
    public struct PeakPreservingOscillator
    {
        /****************************************
        *              Constants
        ****************************************/

        // 위상은 주기 단위다(1 = 한 번 왕복). 극값은 0.25 + 0.5k에 있다
        private const double FIRST_PEAK_PHASE = 0.25;
        private const double PEAK_SPACING     = 0.5;
        private const double TWO_PI           = Math.PI * 2.0;

        /****************************************
        *                Fields
        ****************************************/

        private double _phase;
        private int    _lastPeakSign;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 낸 극값의 부호(+1 / -1) </summary>
        public int LastPeakSign => _lastPeakSign;

        /// <summary> 지금 위상의 값(-1~1). 시간을 흘리지 않는다 </summary>
        public float Current => (float)Math.Sin(_phase * TWO_PI);

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 극값에서 시작한다. 첫 프레임에 곧바로 최대 세기가 보여야 타격 순간과 맞는다 </summary>
        public static PeakPreservingOscillator StartAtPeak(bool positive)
        {
            return new PeakPreservingOscillator
            {
                _phase        = positive ? FIRST_PEAK_PHASE : (FIRST_PEAK_PHASE + PEAK_SPACING),
                _lastPeakSign = positive ? 1 : -1,
            };
        }

        /// <summary> deltaTime만큼 흘리고 이번 프레임에 보일 값(-1~1)을 낸다 </summary>
        public float Step(float frequency, float deltaTime)
        {
            if ((frequency <= 0f) || (deltaTime <= 0f))
            {
                return Current;
            }

            double previous = _phase;
            _phase += frequency * (double)deltaTime;

            long peakBefore = PeakIndex(previous);
            long peakAfter  = PeakIndex(_phase);

            if (peakAfter == peakBefore)
            {
                return Current;
            }

            // 가장 최근에 지난 극값. 짝수 번째가 +1, 홀수 번째가 -1이다
            int sign = ((peakAfter & 1L) == 0L) ? 1 : -1;

            if (sign == _lastPeakSign)
            {
                sign = -sign;
            }

            _lastPeakSign = sign;
            return sign;
        }

        /****************************************
        *              Helpers
        ****************************************/

        // 위상 이하에서 가장 가까운 극값의 번호
        private static long PeakIndex(double phase)
        {
            return (long)Math.Floor((phase - FIRST_PEAK_PHASE) / PEAK_SPACING);
        }
    }
}
