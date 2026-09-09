// System
using System;

namespace Geumeum.Common
{
    /// <summary> 런 하나의 팀 공용 잔액과 원자 거래 창구 </summary>
    public interface IPartyWallet
    {
        /// <summary> 현재 팀 공용 잔액 </summary>
        int Balance { get; }

        /// <summary> 잔액 변경 계획이 참조하는 현재 판 </summary>
        uint Revision { get; }

        /// <summary> 잔액이 동기화된 뒤 새 잔액을 알린다 </summary>
        event Action<int> BalanceChanged;

        /// <summary> 서버에서 잔액을 늘린다 </summary>
        bool TryCredit(int amount);

        /// <summary> 서버에서 환불을 적용한다 </summary>
        bool TryRefund(int amount);

        /// <summary> 현재 잔액을 기준으로 지출 계획을 만든다 </summary>
        bool TryPrepareSpend(int amount, out PartyWalletSpendPlan plan);

        /// <summary> 검증 당시 잔액과 판이 그대로일 때 지출 계획을 커밋한다 </summary>
        bool TryCommitSpend(in PartyWalletSpendPlan plan);

        /// <summary> 지출 계획을 만들고 바로 커밋한다 </summary>
        bool TrySpend(int amount);

        /// <summary> 잔액 기준 비율 손실을 적용한다 </summary>
        bool TryApplyLossRatio(float ratio, out int loss);

        /// <summary> 같은 참가자의 사망 세대에 부활 손실을 한 번 적용한다 </summary>
        bool TryApplyReviveLoss(ParticipantId participant, uint deathGeneration, float ratio, out int loss);
    }
}
