// System
using System;
using System.Collections;
using System.Collections.Generic;

using SystemRandom = System.Random;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary> 런 하나의 팀 공용 잔액과 적 처치 보상을 서버에서 소유한다 </summary>
    [DisallowMultipleComponent]
    public partial class CurrencyManager : NetworkBehaviour, IPartyWallet
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint NO_NET_ID = 0u;

        /****************************************
        *                Types
        ****************************************/

        private readonly struct EnemyRewardKey : IEquatable<EnemyRewardKey>
        {
            public readonly uint RunIdValue;
            public readonly uint EnemyNetId;

            public EnemyRewardKey(uint runIdValue, uint enemyNetId)
            {
                RunIdValue  = runIdValue;
                EnemyNetId  = enemyNetId;
            }

            public bool Equals(EnemyRewardKey other)
            {
                return ((RunIdValue == other.RunIdValue) && (EnemyNetId == other.EnemyNetId));
            }

            public override bool Equals(object obj)
            {
                return ((obj is EnemyRewardKey other) && (Equals(other)));
            }

            public override int GetHashCode()
            {
                return (RunIdValue.GetHashCode() ^ EnemyNetId.GetHashCode());
            }
        }

        private readonly struct ReviveLossKey : IEquatable<ReviveLossKey>
        {
            public readonly uint RunIdValue;
            public readonly ParticipantId ParticipantValue;
            public readonly uint DeathGeneration;

            public ReviveLossKey(uint runIdValue, ParticipantId participantValue, uint deathGeneration)
            {
                RunIdValue       = runIdValue;
                ParticipantValue = participantValue;
                DeathGeneration = deathGeneration;
            }

            public bool Equals(ReviveLossKey other)
            {
                return ((RunIdValue == other.RunIdValue) &&
                        (ParticipantValue == other.ParticipantValue) &&
                        (DeathGeneration == other.DeathGeneration));
            }

            public override bool Equals(object obj)
            {
                return ((obj is ReviveLossKey other) && (Equals(other)));
            }

            public override int GetHashCode()
            {
                return (RunIdValue.GetHashCode() ^ ParticipantValue.GetHashCode() ^
                        DeathGeneration.GetHashCode());
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        // 거래 조정자는 원본을 모두 바꾼 뒤 위 SyncVar 표시값을 한 번에 공개한다
        private int  _serverBalance;
        private uint _serverBalanceRevision;

        private readonly HashSet<EnemyRewardKey> _rewardedEnemies =
            new HashSet<EnemyRewardKey>();

        private readonly HashSet<ReviveLossKey> _reviveLosses =
            new HashSet<ReviveLossKey>();

        private SystemRandom _random;
        private Coroutine _eventBusWaitRoutine;
        private bool      _hasEventSubscription;
        private bool      _transactionLocked;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 잔액이 클라이언트에 반영된 뒤 새 잔액을 알린다 </summary>
        public event Action<int> BalanceChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 팀 공용 잔액 </summary>
        public int Balance => isServer ? _serverBalance : _balance;

        /// <summary> 지출 계획 동시성 검증용 판 </summary>
        public uint Revision => isServer ? _serverBalanceRevision : _balanceRevision;

        internal bool IsTransactionLocked => _transactionLocked;

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            PartyWalletLocator.Register(this);
        }

        public override void OnStopClient()
        {
            PartyWalletLocator.Unregister(this);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnBalanceChanged(int oldValue, int newValue)
        {
            Action<int> handlers = BalanceChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<int>) observers[i])(newValue);
                }
                catch (Exception e)
                {
                    // SyncVar hook 밖으로 예외가 나가면 Mirror hook guard도 복구되지 않는다
                    Debug.LogError($"[CurrencyManager] 잔액 observer 실패: {e}", this);
                }
            }
        }
    }
}
