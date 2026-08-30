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

        [SyncVar] private float _gauge = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정한 현재 패링 게이지 </summary>
        public float Gauge => _gauge;

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
