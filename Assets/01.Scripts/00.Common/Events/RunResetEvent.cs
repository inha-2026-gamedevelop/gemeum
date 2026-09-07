namespace Geumeum.Common
{
    /// <summary> 런이 끝나고 로비로 돌아가 다음 판을 열 수 있는 상태가 됐을 때 서버가 발행한다 </summary>
    public readonly struct RunResetEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 방금 끝난 런의 식별자 </summary>
        public uint FinishedRunIdValue { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RunResetEvent(uint finishedRunIdValue)
        {
            FinishedRunIdValue = finishedRunIdValue;
        }
    }
}
