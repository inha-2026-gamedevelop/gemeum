// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 같은 틱에 여러 종료 사유가 겹쳤을 때 무엇을 남길지 정한다 </summary>
    public static class RunOutcomeArbiter
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 종료 사유의 우선순위. 큰 값이 이긴다 </summary>
        public static int GetPriority(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.Victory:     return 4;
                case RunOutcome.PartyWipe:   return 3;
                case RunOutcome.TimeExpired: return 2;
                case RunOutcome.PlayerLeft:  return 1;
                default:                     return 0;
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
