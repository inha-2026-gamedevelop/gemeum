// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> 패링 성공 누적값을 서버에서 소유하고 다음 공격의 한 번 소비를 제공한다 </summary>
    public class ParryGauge : NetworkBehaviour, IRunStateCarrier
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnGaugeChanged))] private float _gauge = 0f;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 치명의 일격 준비 여부가 바뀌었다 </summary>
        public event System.Action<bool> CriticalReadyChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정한 현재 패링 게이지 </summary>
        public float Gauge => _gauge;

        public int CarrierOrder => 30;

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

            bool wasReady = (oldValue >= max);
            bool isReady  = (newValue >= max);

            if (wasReady == isReady)
            {
                return;
            }

            CriticalReadyChanged?.Invoke(isReady);
        }

        /****************************************
        *           Interface Members
        ****************************************/

        /// <summary> 게이지를 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            snapshot.ParryGauge = _gauge;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) || float.IsNaN(snapshot.ParryGauge) ||
                float.IsInfinity(snapshot.ParryGauge) || (snapshot.ParryGauge < 0f))
            {
                failure = "패링 게이지가 유효하지 않다";
                return false;
            }

            failure = null;
            return true;
        }

        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (!Validate(snapshot, out _))
            {
                return false;
            }

            float max = GameDB.IsInitialized ? GameDB.Combat.ParryGaugeMax : snapshot.ParryGauge;

            _gauge = Mathf.Clamp(snapshot.ParryGauge, 0f, max);
            return true;
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

            ServerAddSuccess(GameDB.Combat.ParryGaugeGainPerSuccess);
        }

        /// <summary> 수락한 loadout이 확정한 패링 성공 보상을 누적한다 </summary>
        [Server]
        public void ServerAddSuccess(float amount)
        {
            if ((!GameDB.IsInitialized) || float.IsNaN(amount) ||
                float.IsInfinity(amount) || (amount <= 0f))
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            _gauge = Mathf.Min(combat.ParryGaugeMax, _gauge + amount);
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
