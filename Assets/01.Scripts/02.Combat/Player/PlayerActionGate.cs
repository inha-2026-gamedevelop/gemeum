// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// phase·생명 상태·무기·진행 중 행동·쿨다운·자원의 공통 허용 여부만 계산한다.
    /// Mirror에 의존하지 않으며 상태 변경은 각 행동의 상태 소유자가 원자적으로 커밋한다
    /// </summary>
    public static class PlayerActionGate
    {
        /// <summary> 이동 · 공격 · 패링 · 구르기가 공통으로 요구하는 최소 조건 </summary>
        public static bool CanAct(RunPhase phase, PlayerLifeState lifeState)
        {
            if ((phase == RunPhase.Lobby) || (phase == RunPhase.Result))
            {
                return false;
            }

            return ((lifeState == PlayerLifeState.Alive) || (lifeState == PlayerLifeState.ReviveInvulnerable));
        }

        /// <summary> 단발 행동을 시작할 수 있는지 공통 조건을 한 번에 계산한다 </summary>
        public static bool CanStartAction(in PlayerActionContext context)
        {
            if (!CanAct(context.Phase, context.LifeState))
            {
                return false;
            }

            if ((!context.HasRequiredWeapon) || (context.IsActionInProgress))
            {
                return false;
            }

            if (context.ServerTime < context.CooldownEndServerTime)
            {
                return false;
            }

            if (context.ResourceCost < 0f)
            {
                return false;
            }

            return context.ResourceBalance >= context.ResourceCost;
        }
    }
}
