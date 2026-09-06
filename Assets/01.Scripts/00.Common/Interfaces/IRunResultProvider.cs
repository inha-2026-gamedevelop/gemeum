namespace Geumeum.Common
{
    /// <summary>
    /// 점수를 계산하는 계약. Systems가 순수 함수로 구현하고 Network는 결과 값을 보관, 배포만 한다.
    ///
    /// 점수식이 Network 안에 있으면 배점을 고칠 때마다 런 상태 머신을 건드리게 된다
    /// </summary>
    public interface IRunResultProvider
    {
        /// <summary> 지금 점수식의 판. 결과에 함께 실어 나중에 배점이 바뀌어도 옛 기록을 다시 계산하지 않는다 </summary>
        int ScoreRuleVersion { get; }

        /// <summary> 확정된 사실만 담긴 결과에서 점수를 낸다. 상태를 읽거나 바꾸지 않는다 </summary>
        int CalculateScore(in RunResultSnapshot facts);
    }
}
