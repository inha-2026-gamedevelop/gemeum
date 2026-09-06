// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 시계 하나의 상태. 필드와 보스가 각각 하나씩 갖는다.
    ///
    /// 경과 시간을 매 틱 누적하지 않고 "멈출 때까지 쌓인 값 + 지금 돌고 있는 만큼"으로 환산한다.
    /// 틱이 밀려도 시계가 어긋나지 않고, 같은 값을 받은 클라이언트가 서버와 같은 결과를 낸다.
    ///
    /// Mirror가 SyncVar로 실을 수 있도록 public 값 필드만 두고 참조 타입을 넣지 않는다
    /// </summary>
    [Serializable]
    public struct RunClockState : IEquatable<RunClockState>
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 마지막으로 멈출 때까지 쌓인 시간(초) </summary>
        public double AccumulatedSeconds;

        /// <summary> 지금 구간이 시작한 서버 시각. 멈춰 있으면 의미 없다 </summary>
        public double ResumeServerTime;

        /// <summary> 지금 시간이 흐르는 중인지 </summary>
        public bool IsRunning;

        /// <summary> 같은 값 재전송과 실제 변경을 구분한다 </summary>
        public uint Revision;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 아직 한 번도 시작하지 않은 시계 </summary>
        public static RunClockState Zero => default;

        /// <summary> 시작한 적이 있는지. 보스 시계가 열렸는지 가릴 때 쓴다 </summary>
        public bool HasStarted => ((IsRunning) || (AccumulatedSeconds > 0.0));

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금까지의 경과 시간(초). 멈춰 있으면 멈춘 순간의 값 그대로다 </summary>
        public double ElapsedAt(double nowServerTime)
        {
            if (!IsRunning)
            {
                return AccumulatedSeconds;
            }

            double running = (nowServerTime - ResumeServerTime);
            if (running < 0.0)
            {
                return AccumulatedSeconds; // 시각이 뒤로 간 경우, 쌓인 값을 줄이지 않는다
            }

            return (AccumulatedSeconds + running);
        }

        /// <summary> 시계를 돌린다. 이미 돌고 있으면 기준 시각을 다시 잡지 않는다 </summary>
        public RunClockState Started(double nowServerTime)
        {
            if (IsRunning)
            {
                return this;
            }

            return new RunClockState
            {
                AccumulatedSeconds = AccumulatedSeconds,
                ResumeServerTime   = nowServerTime,
                IsRunning          = true,
                Revision           = (Revision + 1u),
            };
        }

        /// <summary> 시계를 멈추고 지금까지의 값을 확정한다. 이미 멈춰 있으면 그대로다 </summary>
        public RunClockState Stopped(double nowServerTime)
        {
            if (!IsRunning)
            {
                return this;
            }

            return new RunClockState
            {
                AccumulatedSeconds = ElapsedAt(nowServerTime),
                ResumeServerTime   = 0.0,
                IsRunning          = false,
                Revision           = (Revision + 1u),
            };
        }

        public bool Equals(RunClockState other)
        {
            return ((AccumulatedSeconds.Equals(other.AccumulatedSeconds)) &&
                    (ResumeServerTime.Equals(other.ResumeServerTime)) &&
                    (IsRunning == other.IsRunning) &&
                    (Revision == other.Revision));
        }

        public override bool Equals(object obj) => ((obj is RunClockState other) && (Equals(other)));

        public override int GetHashCode()
        {
            return (AccumulatedSeconds.GetHashCode() ^ ResumeServerTime.GetHashCode() ^ Revision.GetHashCode());
        }
    }
}
