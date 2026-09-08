// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 본체 HP와 현재 피해 배율을 관리한다.
    /// </summary>
    public class BossStatus : NetworkBehaviour, IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private float _health;

        private float _maxHealth            = 0f;
        private bool  _isDead               = false;
        private float _bodyDamageMultiplier = 1f;

        /****************************************
        *              Properties
        ****************************************/

        public bool  IsDead      => _isDead;
        public float MaxHealth   => _maxHealth;
        public float Health      => _health;

        /// <summary> 0~1. 다음 페이즈 진입 판정(EnterHealthRatio)에 쓴다 </summary>
        public float HealthRatio => (_maxHealth <= Constants.Combat.HEALTH_EPSILON) ? 0f : (_health / _maxHealth);

        /// <summary> 사망 시 서버에서 발생한다, killerNetId를 전달한다. 이벤트 발행 · 오브젝트 정리는 BossController가 한다 </summary>
        public event Action<uint> Died;

        /// <summary> 치명타가 아닌 모든 피격에서 발생한다, attackerNetId 전달 </summary>
        public event Action<uint> Damaged;

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 시점에 GameDB.Boss.MaxHealth로 HP를 확정한다 </summary>
        [Server]
        public void Initialize()
        {
            _maxHealth            = GameDB.Boss.MaxHealth;
            _health               = _maxHealth;
            _isDead               = false;
            _bodyDamageMultiplier = 1f;
        }

        /// <summary> 서버가 확정한 현재 그로기 본체 피해 배율을 적용한다 </summary>
        [Server]
        internal void ServerSetBodyDamageMultiplier(float multiplier)
        {
            if ((float.IsNaN(multiplier)) || (float.IsInfinity(multiplier)) || (multiplier <= 0f))
            {
                Debug.LogError("[BossStatus] 본체 피해 배율이 유효하지 않다", this);
                return;
            }

            _bodyDamageMultiplier = multiplier;
        }

        /// <summary> 피해를 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public void TakeDamage(in DamageInfo damage)
        {
            if (_isDead)
            {
                return;
            }

            if ((float.IsNaN(damage.Amount)) || (float.IsInfinity(damage.Amount))
                || (damage.Amount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            // 보스를 때린 플레이어의 전투 시각을 갱신한다. 보스전 중에는 줍기가 열리지 않는다
            CombatClock.ServerNotifyDamage(netId, damage.AttackerNetId, NetworkTime.time);

            _health = Mathf.Max(0f, _health - (damage.Amount * _bodyDamageMultiplier));

            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                Die(damage.AttackerNetId);
                return;
            }

            Damaged?.Invoke(damage.AttackerNetId);
        }

        [Server]
        private void Die(uint killerNetId)
        {
            _isDead = true;
            Died?.Invoke(killerNetId);
        }
    }
}
