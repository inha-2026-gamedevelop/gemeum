namespace Geumeum.Network
{
    /// <summary>
    /// 런 단계가 바뀔 때 서버가 발행한다.
    /// 결계 축소와 잡귀 강화가 각자 시간을 다시 계산하지 않고 이 통지 하나만 보게 한다
    /// </summary>
    public readonly struct RunPhaseChangedEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 직전 단계 </summary>
        public RunPhase Previous { get; }

        /// <summary> 새 단계 </summary>
        public RunPhase Current { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RunPhaseChangedEvent(RunPhase previous, RunPhase current)
        {
            Previous = previous;
            Current  = current;
        }
    }
}
