// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 같은 틱에 여러 종료 사유가 겹쳤을 때 무엇을 남길지 정한다.
    /// Mirror와 Unity에 의존하지 않아야 순서 조합을 단위로 확인할 수 있다.
    ///
    /// 보스가 죽는 순간 마지막 생존자가 함께 죽거나, 그 틱이 마침 기한이기도 한 상황은 실제로 일어난다.
    /// 먼저 도착한 콜백이 결과를 정하면 같은 상황에서 사람마다 다른 결말을 보게 되므로,
    /// 후보를 모아 두고 여기서 한 번만 고른다
    /// </summary>
    public static class RunOutcomeArbiter
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 종료 사유의 우선순위. 큰 값이 이긴다.
        ///
        /// 승리가 가장 높은 것은 이미 이룬 사실이기 때문이다. 전멸이 시간 초과보다 높은 것은
        /// "남은 사람이 없다"가 "시간이 다 됐다"보다 구체적인 사실이라서다.
        /// 이탈은 호스트가 대기를 끊는 제어 종료라 가장 낮다 - 앞서 닫힌 승리를 뒤집으면 안 된다
        /// </summary>
        public static int GetPriority(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.Victory:     return 4;
                case RunOutcome.PartyWipe:   return 3;
                case RunOutcome.TimeExpired: return 2;
                case RunOutcome.PlayerLeft:  return 1;
                default:                     return 0; // None
            }
        }

        /// <summary> 지금 후보를 새 후보로 바꿔야 하는지. 같은 우선순위면 먼저 온 것을 남긴다 </summary>
        public static bool ShouldReplace(RunOutcome current, RunOutcome candidate)
        {
            if (candidate == RunOutcome.None)
            {
                return false;
            }

            return (GetPriority(candidate) > GetPriority(current));
        }

        /// <summary> 두 후보 중 남길 것을 고른다 </summary>
        public static RunOutcome Select(RunOutcome current, RunOutcome candidate)
        {
            return (ShouldReplace(current, candidate) ? candidate : current);
        }
    }
}
