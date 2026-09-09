// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 네트워크와 시스템을 잇는 팀 공용 지갑 로케이터 </summary>
    public static class PartyWalletLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly NullPartyWallet _fallback = new NullPartyWallet();

        private static IPartyWallet _current = _fallback;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 지갑 구현, 없으면 잔액 0을 읽는 기본 구현 </summary>
        public static IPartyWallet Current => _current;

        /// <summary> 실제 서버 지갑이 등록되어 있는지 </summary>
        public static bool HasProvider => (_current != _fallback);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지갑 구현을 등록한다 </summary>
        public static void Register(IPartyWallet wallet)
        {
            if (wallet == null)
            {
                Debug.LogError("[PartyWalletLocator] null 지갑을 등록하려 했다");
                return;
            }

            _current = wallet;
        }

        /// <summary> 등록한 지갑만 해제하고 씬 전환 중 교체 경쟁을 안전하게 넘긴다 </summary>
        public static void Unregister(IPartyWallet wallet)
        {
            if (_current != wallet)
            {
                return;
            }

            _current = _fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _current = _fallback;
        }

        /****************************************
        *              Null Object
        ****************************************/

        private sealed class NullPartyWallet : IPartyWallet
        {
            public int Balance  => 0;
            public uint Revision => 0u;

            public event System.Action<int> BalanceChanged
            {
                add { }
                remove { }
            }

            public bool TryCredit(int amount)
            {
                return false;
            }

            public bool TryRefund(int amount)
            {
                return false;
            }

            public bool TryPrepareSpend(int amount, out PartyWalletSpendPlan plan)
            {
                plan = default;
                return false;
            }

            public bool TryCommitSpend(in PartyWalletSpendPlan plan)
            {
                return false;
            }

            public bool TrySpend(int amount)
            {
                return false;
            }

            public bool TryApplyLossRatio(float ratio, out int loss)
            {
                loss = 0;
                return false;
            }

            public bool TryApplyReviveLoss(ParticipantId participant, uint deathGeneration,
                float ratio, out int loss)
            {
                loss = 0;
                return false;
            }
        }
    }
}
