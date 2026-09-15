// System
using System.Collections;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class RunStatistics
    {
        /****************************************
        *           Network Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ResolveScoreRule();
            RunStatisticsSinkLocator.Register(this);
            RunResultFactsProviderLocator.Register(this);
            RunResultProviderLocator.Register(this);
            SubscribeEnemyDied();
        }

        public override void OnStopServer()
        {
            UnsubscribeEnemyDied();
            RunStatisticsSinkLocator.Unregister(this);
            RunResultFactsProviderLocator.Unregister(this);
            RunResultProviderLocator.Unregister(this);
            ClearRunState();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleEnemyDied(EnemyDiedEvent fact)
        {
            RecordEnemyDeath(fact);
        }

        private void SubscribeEnemyDied()
        {
            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
                _hasEnemyDiedSubscription = true;
                return;
            }

            _eventBusWaitRoutine = StartCoroutine(CoWaitForEventBus());
        }

        private IEnumerator CoWaitForEventBus()
        {
            while (!GameEventBus.HasInstance)
            {
                yield return null;
            }

            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
            _hasEnemyDiedSubscription = true;
            _eventBusWaitRoutine      = null;
        }

        private void UnsubscribeEnemyDied()
        {
            if (_eventBusWaitRoutine != null)
            {
                StopCoroutine(_eventBusWaitRoutine);
                _eventBusWaitRoutine = null;
            }

            if ((!_hasEnemyDiedSubscription) || (!GameEventBus.HasInstance))
            {
                _hasEnemyDiedSubscription = false;
                return;
            }

            GameEventBus.Instance.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
            _hasEnemyDiedSubscription = false;
        }
    }
}
