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
        private float _patrolRadius        = 0f;
        private float _patrolIdleDuration  = 0f;
        private float _patrolWalkDuration  = 0f;
        private float _chargeSpeed         = 0f;
        private float _chargeDistance      = 0f;
        private float _chargeHitRadius     = 0f;
        private float _staggerDuration     = 0f;
        private float _parryWindowDuration = 0f;
        private float _projectileSpeed     = 0f;
        private float _retreatRange        = 0f;
        private int   _hitsToKnockback     = 0;
        private float _knockbackDuration   = 0f;
        private int   _hitCount            = 0;
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
        public float     PatrolRadius        => _patrolRadius;
        public float     PatrolIdleDuration  => _patrolIdleDuration;
        public float     PatrolWalkDuration  => _patrolWalkDuration;
        public float     ChargeSpeed         => _chargeSpeed;
        public float     ChargeDistance      => _chargeDistance;
        public float     ChargeHitRadius     => _chargeHitRadius;
        public float     StaggerDuration     => _staggerDuration;
        public float     ParryWindowDuration => _parryWindowDuration;
        public float     ProjectileSpeed     => _projectileSpeed;
        public float     RetreatRange        => _retreatRange;
        public float     KnockbackDuration   => _knockbackDuration;

        /// <summary> 사망 시 서버에서 발생한다, killerNetId를 전달한다. 이벤트 발행 · 오브젝트 정리는 EnemyController가 한다 </summary>
        public event Action<uint> Died;

        /// <summary> 치명타가 아닌 모든 피격에서 발생한다, attackerNetId 전달. 인지 범위 밖이어도 공격자를 강제 인식시키는 데 쓴다 </summary>
        public event Action<uint> Damaged;

        /// <summary> 넉백 문턱에 못 미친 일반 피격, 경직 여부는 EnemyController가 Brain 상태를 보고 정한다 </summary>
        public event Action HitLanded;

        /// <summary> 일반 피격이 HitsToKnockback만큼 누적되면 발생한다 </summary>
        public event Action Knockback;

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

            _maxHealth           = stat.MaxHealth   * healthMultiplier;
            _attackPower         = stat.AttackPower * attackMultiplier;
            _aggroRange          = stat.AggroRange; // 사거리 · 타이밍은 강화 배수 대상이 아니다
            _attackRange         = stat.AttackRange;
            _windupDuration      = stat.WindupDuration;
            _attackCooldown      = stat.AttackCooldown;
            _patrolRadius        = stat.PatrolRadius;
            _patrolIdleDuration  = stat.PatrolIdleDuration;
            _patrolWalkDuration  = stat.PatrolWalkDuration;
            _chargeSpeed         = stat.ChargeSpeed;
            _chargeDistance      = stat.ChargeDistance;
            _chargeHitRadius     = stat.ChargeHitRadius;
            _staggerDuration     = stat.StaggerDuration;
            _parryWindowDuration = stat.ParryWindowDuration;
            _projectileSpeed     = stat.ProjectileSpeed;
            _retreatRange        = stat.RetreatRange;
            _hitsToKnockback     = stat.HitsToKnockback;
            _knockbackDuration   = stat.KnockbackDuration;
            _health              = _maxHealth;
            _hitCount            = 0;
            _isDead              = false;
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

            // 때린 플레이어의 전투 시각도 여기서 갱신된다. 공격 판정 지점마다 걸면 하나를 빠뜨린다
            CombatClock.ServerNotifyDamage(netId, damage.AttackerNetId, NetworkTime.time);

            _health = Mathf.Max(0f, _health - damage.Amount);

            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                Die(damage.AttackerNetId);
                return;
            }

            Damaged?.Invoke(damage.AttackerNetId);
            RegisterHit();
        }

        /// <summary> 패링 성공 등 넉백을 즉시 강제할 때 호출한다, 일반 피격 누적과 별도 경로다 </summary>
        [Server]
        public void ResetHitCount()
        {
            _hitCount = 0;
        }

        [Server]
        private void RegisterHit()
        {
            ++_hitCount;
            if (_hitCount >= _hitsToKnockback)
            {
                _hitCount = 0;
                Knockback?.Invoke();
                return;
            }

            HitLanded?.Invoke();
        }

        [Server]
        private void Die(uint killerNetId)
        {
            _isDead = true;
            Died?.Invoke(killerNetId);
        }
    }
}
