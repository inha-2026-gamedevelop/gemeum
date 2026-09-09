// System
using System;

namespace Geumeum.Common
{
    /// <summary> 잔액과 revision을 고정한 서버 지출 계획 </summary>
    public readonly struct PartyWalletSpendPlan : IEquatable<PartyWalletSpendPlan>
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지출을 검증할 런 ID </summary>
        public RunId ExpectedRunId { get; }

        /// <summary> 지출할 금액 </summary>
        public int Amount { get; }

        /// <summary> 계획을 만들 때의 잔액 </summary>
        public int ExpectedBalance { get; }

        /// <summary> 계획을 만들 때의 잔액 판 </summary>
        public uint ExpectedRevision { get; }

        /// <summary> 서버 지출 계획으로 쓸 수 있는 값인지 </summary>
        public bool IsValid => (ExpectedRunId.IsValid && (Amount > 0) &&
                                (ExpectedBalance >= Amount));

        /****************************************
        *            Constructors
        ****************************************/

        internal PartyWalletSpendPlan(RunId expectedRunId, int amount, int expectedBalance,
            uint expectedRevision)
        {
            ExpectedRunId    = expectedRunId;
            Amount           = amount;
            ExpectedBalance  = expectedBalance;
            ExpectedRevision = expectedRevision;
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(PartyWalletSpendPlan other)
        {
            return ((ExpectedRunId == other.ExpectedRunId) &&
                    (Amount == other.Amount) &&
                    (ExpectedBalance == other.ExpectedBalance) &&
                    (ExpectedRevision == other.ExpectedRevision));
        }

        public override bool Equals(object obj)
        {
            return ((obj is PartyWalletSpendPlan other) && (Equals(other)));
        }

        public override int GetHashCode()
        {
            return (ExpectedRunId.GetHashCode() ^ Amount ^ ExpectedBalance ^
                    ExpectedRevision.GetHashCode());
        }

        public static bool operator ==(PartyWalletSpendPlan left, PartyWalletSpendPlan right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PartyWalletSpendPlan left, PartyWalletSpendPlan right)
        {
            return (!left.Equals(right));
        }
    }
}
