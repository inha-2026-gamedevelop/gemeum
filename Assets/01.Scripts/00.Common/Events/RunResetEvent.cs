namespace Geumeum.Common
{
    /// <summary>
    /// 런이 끝나고 로비로 돌아가 다음 판을 열 수 있는 상태가 됐을 때 서버가 발행한다.
    ///
    /// 한 세션에서 여러 판을 이어서 하므로, 씬이 파괴되지 않는 지금 구조에서는
    /// 이전 판의 생존 개체와 스폰 여부, 점유 목록이 그대로 남는다.
    /// 그 상태로 다음 판을 열면 보스가 스폰되지 않거나 이전 필드의 잡귀가 새 판에 서 있다.
    ///
    /// 이 이벤트는 통지이지 초기화 그 자체가 아니다 - 발행했다는 이유로 초기화됐다고 가정하지 않는다
    /// </summary>
    public readonly struct RunResetEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 방금 끝난 런의 식별자. 이 값으로 늦게 도착한 이전 판의 요청을 거른다 </summary>
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
