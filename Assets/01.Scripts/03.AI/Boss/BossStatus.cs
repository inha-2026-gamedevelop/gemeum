// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 본체 HP와 현재 피해 배율을 관리한다.
    /// </summary>
    public partial class BossStatus : NetworkBehaviour, IDamageable, IDamagePresentationSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private float _maxHealth            = 0f;
        private bool  _isDead               = false;
        private float _bodyDamageMultiplier = 1f;
        private float _minimumHealth         = 0f;
        private bool  _isInvulnerable        = false;

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

        /// <summary> 동기화된 체력이 줄었다. 각 화면의 피격 플래시가 쓴다 </summary>
        public event Action HealthDecreased;

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnHealthSynced(float oldValue, float newValue)
        {
            if (newValue < oldValue)
            {
                HealthDecreased?.Invoke();
            }
        }
    }
}
