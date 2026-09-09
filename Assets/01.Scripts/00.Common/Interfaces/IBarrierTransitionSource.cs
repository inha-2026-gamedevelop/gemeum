// System
using System;

namespace Geumeum.Common
{
    /// <summary> 결계의 현재 원과 경고 때 확정된 목표 원을 함께 공급하는 계약 </summary>
    public interface IBarrierTransitionSource : IBarrierState
    {
        /// <summary> 늦게 등록한 소비자가 즉시 적용할 현재 snapshot </summary>
        BarrierTransitionState Snapshot { get; }

        /// <summary> 서버 확정 또는 원격 동기화로 snapshot이 바뀐 뒤 발생한다 </summary>
        event Action<BarrierTransitionState> SnapshotChanged;
    }
}
