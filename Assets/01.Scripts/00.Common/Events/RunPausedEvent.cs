namespace Geumeum.Common
{
    /// <summary>첫 정지 사유가 걸려 서버 simulation이 멈춘 직후 발행한다</summary>
    public readonly struct RunPausedEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        public RunPauseReason Reasons { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RunPausedEvent(RunPauseReason reasons)
        {
            Reasons = reasons;
        }
    }
}
