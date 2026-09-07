namespace Geumeum.Combat
{
    /// <summary> 단발 행동 하나가 PlayerActionPipeline에 제공해야 하는 세 조각 </summary>
    public interface IPlayerAction
    {
        /// <summary> 이번 요청의 게이트 입력을 만든다 </summary>
        bool TryBuildContext(double serverTime, out PlayerActionContext context);

        /// <summary> 게이트를 통과한 뒤 자원을 실제로 차감한다 </summary>
        bool TrySpendResource(in PlayerActionContext context);

        /// <summary> 자원까지 통과한 요청의 상태를 확정하고 전파한다 </summary>
        void CommitAction(in PlayerActionContext context);
    }
}
