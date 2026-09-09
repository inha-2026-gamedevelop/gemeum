namespace Geumeum.Common
{
    /// <summary> 서버가 축소 경고 시점에 다음 목표 원을 확정한 사실 </summary>
    public readonly struct BarrierTransitionPreparedEvent
    {
        public BarrierTransitionState Snapshot { get; }

        public BarrierTransitionPreparedEvent(in BarrierTransitionState snapshot)
        {
            Snapshot = snapshot;
        }
    }
}
