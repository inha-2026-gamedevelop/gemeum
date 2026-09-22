// System
using System;
using System.Collections;

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
    public partial class CurrencyManager
    {
        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _serverBalance         = 0;
            _serverBalanceRevision = 0u;
            _balance          = 0;
            _balanceRevision  = 0u;
            _rewardedEnemies.Clear();
            _reviveLosses.Clear();
            _random           = null;
            _transactionLocked = false;

            PartyWalletLocator.Register(this);
            ServerSubscribeEvents();
        }

        public override void OnStopServer()
        {
            if (_eventBusWaitRoutine != null)
            {
                StopCoroutine(_eventBusWaitRoutine);
                _eventBusWaitRoutine = null;
            }

            ServerUnsubscribeEvents();
            PartyWalletLocator.Unregister(this);
        }

        /****************************************
        *              IPartyWallet
        ****************************************/

        /// <summary> 서버에서 잔액을 늘린다 </summary>
        [Server]
        public bool TryCredit(int amount)
        {
            if (!ServerCanChangeBalance())
            {
                return false;
            }

            if (!CurrencyRules.TryCredit(_serverBalance, amount, out int nextBalance))
            {
                return false;
            }

            return ServerCommitBalance(nextBalance);
        }

        /// <summary> 서버에서 환불을 적용한다 </summary>
        [Server]
        public bool TryRefund(int amount)
        {
            return TryCredit(amount);
        }

        /// <summary> 현재 잔액과 판을 확인해 지출 계획을 만든다 </summary>
        [Server]
        public bool TryPrepareSpend(int amount, out PartyWalletSpendPlan plan)
        {
            plan = default;

            if (!ServerCanChangeBalance())
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid))
            {
                return false;
            }

            if (_serverBalanceRevision == uint.MaxValue)
            {
                return false;
            }

            return CurrencyRules.TryPrepareSpend(run.RunId, _serverBalance, _serverBalanceRevision,
                amount, out plan);
        }

        /// <summary> 잔액과 판이 계획 생성 시점과 같을 때만 지출을 커밋한다 </summary>
        [Server]
        public bool TryCommitSpend(in PartyWalletSpendPlan plan)
        {
            if (!ServerCanChangeBalance())
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid))
            {
                return false;
            }

            if (!ServerCanApplySpend(plan))
            {
                return false;
            }

            ServerApplySpend(plan);
            ServerPublishTransaction();
            return true;
        }

        /// <summary> 지출 계획을 만들고 바로 커밋한다 </summary>
        [Server]
        public bool TrySpend(int amount)
        {
            if (!TryPrepareSpend(amount, out PartyWalletSpendPlan plan))
            {
                return false;
            }

            return TryCommitSpend(plan);
        }

        /// <summary> 현재 잔액에서 손실 비율을 한 번 적용한다 </summary>
        [Server]
        public bool TryApplyLossRatio(float ratio, out int loss)
        {
            loss = 0;

            if (!ServerCanChangeBalance())
            {
                return false;
            }

            if (!CurrencyRules.TryCalculateLoss(_serverBalance, ratio, out loss))
            {
                loss = 0;
                return false;
            }

            return ServerCommitBalance(_serverBalance - loss);
        }

        /// <summary> 같은 런과 사망 세대의 부활 손실을 한 번만 적용한다 </summary>
        [Server]
        public bool TryApplyReviveLoss(ParticipantId participant, uint deathGeneration,
            float ratio, out int loss)
        {
            loss = 0;

            if ((!participant.IsValid) || (deathGeneration == NO_NET_ID))
            {
                return false;
            }

            if (!ServerCanChangeBalance())
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid))
            {
                return false;
            }

            ReviveLossKey key = new ReviveLossKey(run.RunId.Value, participant,
                deathGeneration);

            if (!CurrencyRules.TryCalculateLoss(_serverBalance, ratio, out loss))
            {
                loss = 0;
                return false;
            }

            if (_reviveLosses.Contains(key))
            {
                // Idempotent retries let a reconnect finish the same death generation
                // without charging the participant twice
                loss = 0;
                return true;
            }

            int nextBalance = _serverBalance - loss;
            if (!ServerCanCommitBalance(nextBalance))
            {
                loss = 0;
                return false;
            }

            ServerApplyBalance(nextBalance);
            _reviveLosses.Add(key);
            ServerPublishTransaction();
            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 다음 런을 위해 잔액과 중복 기록을 비운다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            _serverBalance         = 0;
            _serverBalanceRevision = 0u;
            _balance          = 0;
            _balanceRevision  = 0u;
            _rewardedEnemies.Clear();
            _reviveLosses.Clear();
            _random           = null;
            _transactionLocked = false;
        }

        /****************************************
        *              Event Handlers
        ****************************************/

        [Server]
        private void HandleEnemyDied(EnemyDiedEvent evt)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!ServerCanChangeBalance()) || (!evt.IsValid))
            {
                return;
            }

            if (evt.EnemyNetId == NO_NET_ID)
            {
                return;
            }

            EnemyRewardKey key = new EnemyRewardKey(run.RunId.Value, evt.EnemyNetId);
            if (_rewardedEnemies.Contains(key))
            {
                return;
            }

            if (evt.IsBoss)
            {
                _rewardedEnemies.Add(key);
                return;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[CurrencyManager] GameDB가 없어 적 처치 보상을 계산하지 못했다", this);
                return;
            }

            if ((!evt.Kind.HasValue) || (!GameDB.Enemy.TryGetStat(evt.Kind.Value, out EnemySO.StatEntry stat)))
            {
                Debug.LogError($"[CurrencyManager] {evt.Kind} 적의 화폐 스탯을 찾지 못했다", this);
                return;
            }

            if (_random == null)
            {
                _random = new SystemRandom(Environment.TickCount);
            }

            if (!CurrencyRules.TryRollReward(stat.CurrencyMin, stat.CurrencyMax,
                evt.RewardMultiplier, _random, out int reward))
            {
                Debug.LogError($"[CurrencyManager] {evt.Kind} 적의 화폐 보상 데이터가 유효하지 않다", this);
                return;
            }

            if (!TryCredit(reward))
            {
                return;
            }

            _rewardedEnemies.Add(key);
        }

        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            ServerResetForNewRun();
        }

        /****************************************
        *              Internals
        ****************************************/

        [Server]
        private bool ServerCanChangeBalance()
        {
            if (_transactionLocked)
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                return false;
            }

            return ((!run.IsPaused) && (run.Phase != RunPhase.Lobby) &&
                    (run.Phase != RunPhase.Result));
        }

        [Server]
        internal bool ServerCanApplySpend(in PartyWalletSpendPlan plan)
        {
            if (!ServerCanChangeBalance())
            {
                return false;
            }

            RunManager run = RunManager.ServerInstance;
            return ((run != null) && (_serverBalanceRevision != uint.MaxValue) &&
                CurrencyRules.TryCommitSpend(plan, run.RunId, _serverBalance,
                    _serverBalanceRevision, out _));
        }

        /// <summary> 커밋부터 모든 통지가 끝날 때까지 다른 지갑 변경을 막는다 </summary>
        [Server]
        internal bool ServerTryAcquireSpend(in PartyWalletSpendPlan plan)
        {
            if (!ServerCanApplySpend(plan))
            {
                return false;
            }

            _transactionLocked = true;
            return true;
        }

        [Server]
        internal void ServerReleaseTransaction()
        {
            _transactionLocked = false;
        }

        /// <summary> 사전 검증된 지출을 서버 원본에만 적용한다 </summary>
        [Server]
        internal void ServerApplySpend(in PartyWalletSpendPlan plan)
        {
            _serverBalance = plan.ExpectedBalance - plan.Amount;
            _serverBalanceRevision = plan.ExpectedRevision + 1u;
        }

        /// <summary> 여러 원본의 적용이 끝난 뒤 지갑 표시값을 공개한다 </summary>
        [Server]
        internal void ServerPublishTransaction()
        {
            try
            {
                _balance = _serverBalance;
            }
            catch (Exception e)
            {
                // 사용자 observer 외의 예외가 생겨도 revision 공개는 반드시 진행한다
                Debug.LogError($"[CurrencyManager] 잔액 공개 실패: {e}", this);
            }
            finally
            {
                _balanceRevision = _serverBalanceRevision;
            }
        }

        [Server]
        private bool ServerCommitBalance(int nextBalance)
        {
            if (!ServerCanCommitBalance(nextBalance))
            {
                return false;
            }

            if (_serverBalance == nextBalance)
            {
                return true;
            }

            ServerApplyBalance(nextBalance);
            ServerPublishTransaction();
            return true;
        }

        private bool ServerCanCommitBalance(int nextBalance)
        {
            return ((nextBalance >= 0) && ((_serverBalance == nextBalance) ||
                (_serverBalanceRevision != uint.MaxValue)));
        }

        private void ServerApplyBalance(int nextBalance)
        {
            if (_serverBalance == nextBalance)
            {
                return;
            }

            _serverBalance = nextBalance;
            ++_serverBalanceRevision;
        }

        [Server]
        private void ServerSubscribeEvents()
        {
            if (_hasEventSubscription)
            {
                return;
            }

            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
                GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);
                _hasEventSubscription = true;
                return;
            }

            if (_eventBusWaitRoutine == null)
            {
                _eventBusWaitRoutine = StartCoroutine(CoWaitForEventBus());
            }
        }

        [Server]
        private void ServerUnsubscribeEvents()
        {
            if (!_hasEventSubscription || !GameEventBus.HasInstance)
            {
                _hasEventSubscription = false;
                return;
            }

            GameEventBus.Instance.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
            GameEventBus.Instance.Unsubscribe<RunResetEvent>(HandleRunReset);
            _hasEventSubscription = false;
        }

        private IEnumerator CoWaitForEventBus()
        {
            while (!GameEventBus.HasInstance)
            {
                if (!isServer)
                {
                    _eventBusWaitRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _eventBusWaitRoutine = null;
            ServerSubscribeEvents();
        }
    }
}
