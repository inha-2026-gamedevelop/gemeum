// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> 패링 성공 누적값을 서버에서 소유하고 다음 공격의 한 번 소비를 제공한다 </summary>
    public class ParryGauge : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnGaugeChanged))] private float _gauge = 0f;

        /****************************************
        *                Events
        ****************************************/

        // 게이지 값 자체는 계속 변하므로 UI가 프로퍼티로 읽는다.
        // 이벤트로 알리는 것은 "다음 공격이 치명타가 됐다"는 이산 전이뿐이다
        /// <summary> 치명의 일격 준비 여부가 바뀌었다 </summary>
        public event System.Action<bool> CriticalReadyChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정한 현재 패링 게이지 </summary>
        public float Gauge => _gauge;

        /// <summary> 게이지가 최대치에 닿아 다음 근접 공격이 치명의 일격인지 </summary>
        public bool IsCriticalReady => ((GameDB.IsInitialized) && (_gauge >= GameDB.Combat.ParryGaugeMax));

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnGaugeChanged(float oldValue, float newValue)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            float max = GameDB.Combat.ParryGaugeMax;

            // 최대치를 넘나든 순간에만 알린다. 매 누적마다 부르면 UI가 같은 값을 반복해서 받는다
            bool wasReady = (oldValue >= max);
            bool isReady  = (newValue >= max);

            if (wasReady == isReady)
            {
                return;
            }

            CriticalReadyChanged?.Invoke(isReady);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 패링 성공 보상을 최대치까지 누적한다 </summary>
        [Server]
        public void ServerAddSuccess()
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            _gauge = Mathf.Min(combat.ParryGaugeMax, _gauge + combat.ParryGaugeGainPerSuccess);
        }

        /// <summary> 최대치에 도달한 게이지를 다음 공격이 한 번만 소비한다 </summary>
        [Server]
        public bool ServerTryConsume()
        {
            if ((!GameDB.IsInitialized) || (_gauge < GameDB.Combat.ParryGaugeMax))
            {
                return false;
            }

            _gauge = 0f;
            return true;
        }

        /// <summary> 무기 교체 시 게이지를 초기화한다 </summary>
        [Server]
        public void ServerReset()
        {
            _gauge = 0f;
        }
    }
}
