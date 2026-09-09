// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 지갑 잔액, 적 처치 보상, 부활 손실의 순수 계산 규칙 </summary>
    public static class CurrencyRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MIN_BALANCE     = 0;
        private const int   MIN_AMOUNT      = 0;
        private const float MIN_LOSS_RATIO  = 0f;
        private const float MAX_LOSS_RATIO  = 1f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 잔액에 금액을 더할 수 있는지 계산한다 </summary>
        public static bool TryCredit(int balance, int amount, out int nextBalance)
        {
            nextBalance = balance;

            if ((balance < MIN_BALANCE) || (amount < MIN_AMOUNT))
            {
                return false;
            }

            long result = ((long) balance + amount);
            if (result > int.MaxValue)
            {
                return false;
            }

            nextBalance = (int) result;
            return true;
        }

        /// <summary> 현재 잔액과 판을 묶은 지출 계획을 만든다 </summary>
        public static bool TryPrepareSpend(RunId runId, int balance, uint revision, int amount,
            out PartyWalletSpendPlan plan)
        {
            plan = default;

            if ((!runId.IsValid) || (balance < MIN_BALANCE) || (amount <= MIN_AMOUNT) ||
                (amount > balance))
            {
                return false;
            }

            plan = new PartyWalletSpendPlan(runId, amount, balance, revision);
            return true;
        }

        /// <summary> 지출 계획이 아직 유효한지 확인하고 다음 잔액을 계산한다 </summary>
        public static bool TryCommitSpend(in PartyWalletSpendPlan plan, RunId runId,
            int balance, uint revision, out int nextBalance)
        {
            nextBalance = balance;

            if (!plan.IsValid)
            {
                return false;
            }

            if ((!runId.IsValid) || (runId != plan.ExpectedRunId) ||
                (balance < MIN_BALANCE) || (balance != plan.ExpectedBalance) ||
                (revision != plan.ExpectedRevision) || (plan.Amount > balance))
            {
                return false;
            }

            nextBalance = (balance - plan.Amount);
            return true;
        }

        /// <summary> 부활 손실을 확정 직전 잔액에서 내림해 계산한다 </summary>
        public static bool TryCalculateLoss(int balance, float ratio, out int loss)
        {
            loss = 0;

            if ((balance < MIN_BALANCE) || float.IsNaN(ratio) || float.IsInfinity(ratio) ||
                (ratio < MIN_LOSS_RATIO) || (ratio > MAX_LOSS_RATIO))
            {
                return false;
            }

            double rawLoss = Math.Floor((double) balance * ratio);
            if (rawLoss <= MIN_AMOUNT)
            {
                return true;
            }

            loss = (rawLoss >= balance) ? balance : (int) rawLoss;
            return true;
        }

        /// <summary> 유효한 입력에서 부활 손실을 반환한다 </summary>
        public static int CalculateLoss(int balance, float ratio)
        {
            return (TryCalculateLoss(balance, ratio, out int loss) ? loss : MIN_AMOUNT);
        }

        /// <summary> 서버 난수로 기본 범위를 고르고 보상 배수를 내림 적용한다 </summary>
        public static bool TryRollReward(int currencyMin, int currencyMax, float rewardMultiplier,
            Random random, out int reward)
        {
            reward = 0;

            if ((currencyMin < MIN_AMOUNT) || (currencyMax < currencyMin) ||
                float.IsNaN(rewardMultiplier) || float.IsInfinity(rewardMultiplier) ||
                (rewardMultiplier < MIN_LOSS_RATIO) || (random == null))
            {
                return false;
            }

            double span = ((double) currencyMax - currencyMin) + 1d;
            double rolled = Math.Floor(currencyMin + (random.NextDouble() * span));
            int baseReward = (rolled > currencyMax) ? currencyMax : (int) rolled;
            double scaled = Math.Floor(baseReward * (double) rewardMultiplier);

            if ((scaled < MIN_AMOUNT) || (scaled > int.MaxValue))
            {
                return false;
            }

            reward = (int) scaled;
            return true;
        }
    }
}
