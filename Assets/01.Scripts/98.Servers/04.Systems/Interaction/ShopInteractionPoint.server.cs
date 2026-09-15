// System
using System.Collections;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    public partial class ShopInteractionPoint
    {
        public override void OnStartServer()
        {
            ServerRefreshOpenState();
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
        }

        [Server]
        public void Interact(uint playerNetId)
        {
            if (ServerEvaluate(playerNetId) != InteractionResult.Success)
            {
                return;
            }

            if ((!NetworkServer.spawned.TryGetValue(playerNetId, out NetworkIdentity player)) ||
                (player.connectionToClient == null))
            {
                return;
            }

            TargetOpenShop(player.connectionToClient, netId);
        }

        /// <summary> 구매 커밋 직전에도 같은 서버 조건을 다시 검사한다 </summary>
        [Server]
        internal InteractionResult ServerEvaluate(uint playerNetId)
        {
            if ((!_isOpen) || (!NetworkServer.spawned.TryGetValue(playerNetId,
                    out NetworkIdentity player)))
            {
                return InteractionResult.Unavailable;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Combat == null) || (GameDB.Shop == null))
            {
                return InteractionResult.InvalidRequest;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (run.Phase == RunPhase.Lobby) ||
                (run.Phase == RunPhase.Result))
            {
                return InteractionResult.RunNotActive;
            }

            if (run.Phase != GameDB.Shop.OpenPhase)
            {
                return InteractionResult.Unavailable;
            }

            if (run.IsPaused || RunSceneDirector.IsServerTransitioning)
            {
                return InteractionResult.RunNotActive;
            }

            if (player.gameObject.scene.handle != gameObject.scene.handle)
            {
                return InteractionResult.WrongScene;
            }

            var damageable = player.GetComponent<IDamageable>();
            if (damageable == null)
            {
                return InteractionResult.InvalidRequest;
            }

            CombatConfigSO combat = GameDB.Combat;
            var request = new InteractionRequest(
                (player.transform.position - transform.position).sqrMagnitude,
                combat.InteractRange, !damageable.IsDead, true,
                RunTimeLocator.Current.GameplayElapsedSeconds,
                CombatClock.GetLastCombatTime(playerNetId), combat.InteractOutOfCombatSeconds);
            return InteractionRules.Evaluate(request);
        }

        [Server]
        private void ServerRefreshOpenState()
        {
            RunManager run = RunManager.ServerInstance;
            _isOpen = (GameDB.IsInitialized && (GameDB.Shop != null) && (run != null) &&
                (!run.IsPaused) && (!RunSceneDirector.IsServerTransitioning) &&
                (run.Phase == GameDB.Shop.OpenPhase));
        }

        [Server]
        private void HandleRunPhaseChanged(RunPhaseChangedEvent evt)
        {
            ServerRefreshOpenState();
        }

        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            ServerRefreshOpenState();
        }

        [Server]
        private void HandleRunResumed(RunResumedEvent evt)
        {
            ServerRefreshOpenState();
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
                GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);
                GameEventBus.Instance.Subscribe<RunResumedEvent>(HandleRunResumed);
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

            GameEventBus.Instance.Unsubscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
            GameEventBus.Instance.Unsubscribe<RunResetEvent>(HandleRunReset);
            GameEventBus.Instance.Unsubscribe<RunResumedEvent>(HandleRunResumed);
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
