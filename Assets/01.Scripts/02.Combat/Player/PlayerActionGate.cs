// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> phase, 생명 상태, 무기, 진행 중 행동, 쿨다운, 자원의 공통 허용 여부를 계산한다 </summary>
    public static class PlayerActionGate
    {
        /// <summary> 이동, 공격, 패링, 구르기가 공통으로 요구하는 최소 조건 </summary>
        public static bool CanAct(RunPhase phase, PlayerLifeState lifeState, bool isPaused)
        {
            if (isPaused)
            {
                return false;
            }

            if ((phase == RunPhase.Lobby) || (phase == RunPhase.Result))
            {
                return false;
            }

            return ((lifeState == PlayerLifeState.Alive) || (lifeState == PlayerLifeState.ReviveInvulnerable));
        }

        /// <summary>
        /// 이번 틱에 이동 입력을 적용해도 되는가.
        /// 공격 동작 중에는 제자리에 선다. 움직이면서 휘두르면 검기와 판정 방향이 발밑을 따라 흘러
        /// 눈에 보이는 것과 맞은 위치가 어긋난다
        /// </summary>
        public static bool CanMove(bool canAct, bool isAttackInProgress)
        {
            return (canAct) && (!isAttackInProgress);
        }

        /// <summary> 단발 행동을 시작할 수 있는지 공통 조건을 한 번에 계산한다 </summary>
        public static bool CanStartAction(in PlayerActionContext context)
        {
            if (!CanAct(context.Phase, context.LifeState, context.IsPaused))
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
