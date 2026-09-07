namespace Geumeum.Common
{
    /// <summary> 얼어 있던 런이 다시 돌기 시작할 때 서버가 발행한다 </summary>
    public readonly struct RunResumedEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런이 멈춰 있던 시간(초) </summary>
        public double PausedSeconds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RunResumedEvent(double pausedSeconds)
        {
            PausedSeconds = pausedSeconds;
        }
    }
}
