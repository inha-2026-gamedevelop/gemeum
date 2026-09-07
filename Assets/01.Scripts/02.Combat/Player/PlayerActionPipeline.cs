namespace Geumeum.Combat
{
    /// <summary> 단발 행동 요청이 지나는 유일한 서버 경로 </summary>
    public static class PlayerActionPipeline
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 요청 하나를 처리한다. ActionRejection.None이면 커밋까지 끝난 것이다 </summary>
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

            if (!action.TrySpendResource(context))
            {
                return ActionRejection.Resource;
            }

            action.CommitAction(context);
            return ActionRejection.None;
        }

        /// <summary> 게이트 결과를 거부 사유로 번역한다 </summary>
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
