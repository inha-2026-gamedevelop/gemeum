namespace Geumeum.Combat
{
    /// <summary>
    /// 단발 행동 요청이 지나는 유일한 서버 경로.
    /// 중복 검사, 게이트, 자원 차감, 커밋을 이 순서로만 밟게 하고 거부 사유를 돌려준다
    /// </summary>
    // Mirror에 의존하지 않는다. PlayerActionGate, ParryJudge와 같은 층의 순수 판정 코드라
    // 테스트에서 그대로 부를 수 있고, 데디케이티드로 옮겨도 그대로 간다.
    //
    // 패링은 여기를 지나지 않는다. 시각 재조정(클라 추정 시각 클램프)과 최소 요청 간격이
    // 이 뼈대에 없는 단계이고, 무엇보다 0.32초 판정 창은 이미 L2를 여러 번 통과한 코드다 -
    // 뼈대에 맞추려고 손대면 그 검증이 통째로 무효가 된다
    public static class PlayerActionPipeline
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 요청 하나를 처리한다. ActionRejection.None이면 커밋까지 끝난 것이다.
        /// tracker를 ref로 받는 이유는 RequestIdTracker가 struct라 값으로 받으면
        /// 수락 기록이 호출부에 남지 않기 때문이다
        /// </summary>
        public static ActionRejection TryExecute(
            ref RequestIdTracker tracker, uint requestId, IPlayerAction action, double serverTime)
        {
            if (action == null)
            {
                return ActionRejection.NotReady;
            }

            if (!tracker.TryAccept(requestId))
            {
                return ActionRejection.Duplicate;
            }

            if (!action.TryBuildContext(serverTime, out PlayerActionContext context))
            {
                return ActionRejection.NotReady;
            }

            ActionRejection gateResult = Evaluate(context);
            if (gateResult != ActionRejection.None)
            {
                return gateResult;
            }

            // 자원은 게이트를 전부 통과한 뒤에만 깎는다. 순서를 바꾸면 거부된 요청이 스태미나를 먹는다
            if (!action.TrySpendResource(context))
            {
                return ActionRejection.Resource;
            }

            action.CommitAction(context);
            return ActionRejection.None;
        }

        /// <summary>
        /// 게이트 결과를 거부 사유로 번역한다.
        /// PlayerActionGate는 bool 하나만 돌려주므로 어느 조건에서 막혔는지는 여기서 다시 가른다
        /// </summary>
        public static ActionRejection Evaluate(in PlayerActionContext context)
        {
            if (PlayerActionGate.CanStartAction(context))
            {
                return ActionRejection.None;
            }

            if (!PlayerActionGate.CanAct(context.Phase, context.LifeState, context.IsPaused))
            {
                return ActionRejection.State;
            }

            if (!context.HasRequiredWeapon)
            {
                return ActionRejection.NoWeapon;
            }

            if ((context.IsActionInProgress) || (context.ServerTime < context.CooldownEndServerTime))
            {
                return ActionRejection.Busy;
            }

            return ActionRejection.Resource;
        }
    }
}
