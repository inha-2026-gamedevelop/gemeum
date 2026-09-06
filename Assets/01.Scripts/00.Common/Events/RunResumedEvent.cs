namespace Geumeum.Common
{
    /// <summary>
    /// 얼어 있던 런이 다시 돌기 시작할 때 서버가 발행한다.
    ///
    /// 멈춘 동안에도 NetworkTime은 계속 흐르므로, 절대 시각으로 잡아 둔 타이머는
    /// 재개하는 순간 이미 지나 있다. 각자 이 값만큼 자기 종료 시각을 밀어야 남은 시간이 보존된다
    /// </summary>
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
