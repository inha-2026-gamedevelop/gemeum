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
    /// 잡귀 HP와 결계 단계별 강화 배수를 관리한다. IDamageable 구현
    /// </summary>
    public class EnemyStatus : NetworkBehaviour, IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EnemyKind _kind;

        [SyncVar] private float _health;

        private float _maxHealth           = 0f;
        private float _attackPower         = 0f;
        private float _aggroRange          = 0f;
        private float _attackRange         = 0f;
        private float _windupDuration      = 0f;
        private float _attackCooldown      = 0f;
        private float _chargeSpeed         = 0f;
        private float _chargeDistance      = 0f;
        private float _chargeHitRadius     = 0f;
        private float _staggerDuration     = 0f;
        private float _parryWindowDuration = 0f;
        private bool  _isDead              = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool      IsDead              => _isDead;
        public EnemyKind Kind                => _kind;
        public float     AttackPower         => _attackPower;
        public float     AggroRange          => _aggroRange;
        public float     AttackRange         => _attackRange;
        public float     WindupDuration      => _windupDuration;
        public float     AttackCooldown      => _attackCooldown;
        public float     ChargeSpeed         => _chargeSpeed;
        public float     ChargeDistance      => _chargeDistance;
        public float     ChargeHitRadius     => _chargeHitRadius;
        public float     StaggerDuration     => _staggerDuration;
        public float     ParryWindowDuration => _parryWindowDuration;

        /// <summary> 사망 시 서버에서 발생한다, killerNetId를 전달한다. 이벤트 발행 · 오브젝트 정리는 EnemyController가 한다 </summary>
        public event Action<uint> Died;

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 시점 결계 단계로 스탯을 확정한다, 이후 재계산하지 않는다 </summary>
        [Server]
        public void Initialize(int fieldLevel)
        {
            if (!GameDB.Enemy.TryGetStat(_kind, out EnemySO.StatEntry stat))
            {
                Debug.LogError($"[EnemyStatus] {_kind} 기본 스탯을 찾지 못했다", this);
                return;
            }

            float healthMultiplier = 1f;
            float attackMultiplier = 1f;

            if (GameDB.Enemy.TryGetEnhance(fieldLevel, out EnemySO.EnhanceEntry enhance))
            {
                healthMultiplier = enhance.HealthMultiplier;
                attackMultiplier = enhance.AttackMultiplier;
            }

            _maxHealth      = stat.MaxHealth   * healthMultiplier;
            _attackPower    = stat.AttackPower * attackMultiplier;
            _aggroRange     = stat.AggroRange;     // 사거리 · 타이밍은 강화 배수 대상이 아니다
            _attackRange    = stat.AttackRange;
            _windupDuration = stat.WindupDuration;
            _attackCooldown = stat.AttackCooldown;
            _chargeSpeed        = stat.ChargeSpeed;
            _chargeDistance     = stat.ChargeDistance;
            _chargeHitRadius    = stat.ChargeHitRadius;
            _staggerDuration    = stat.StaggerDuration;
            _parryWindowDuration = stat.ParryWindowDuration;
            _health         = _maxHealth;
            _isDead         = false;
        }

        /// <summary> 피해를 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public void TakeDamage(in DamageInfo damage)
        {
            if (_isDead)
            {
                return;
            }

            if (damage.Amount <= Constants.Combat.DAMAGE_EPSILON)
            {
                return;
            }

            _health = Mathf.Max(0f, _health - damage.Amount);

            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                Die(damage.AttackerNetId);
            }
        }

        [Server]
        private void Die(uint killerNetId)
        {
            _isDead = true;
            Died?.Invoke(killerNetId);
        }
    }
}
