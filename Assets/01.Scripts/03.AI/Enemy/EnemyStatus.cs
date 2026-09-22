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
    /// 잡귀 HP와 결계 단계별 강화 배수를 관리한다. IDamageable 구현
    /// </summary>
    public partial class EnemyStatus : NetworkBehaviour, IDamageable, IDamageHealthSource, IDamagePresentationSource
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EnemyKind _kind;

        /****************************************
        *              Properties
        ****************************************/

        public bool      IsDead              => _isDead;
        public EnemyKind Kind                => _kind;
        public float     Health              => _health;
        public float     MaxHealth           => _maxHealth;
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
        public int       FieldLevel          => _fieldLevel;
        public int       CombatFieldLevel    => _combatFieldLevel;
        public float     RewardMultiplier    => _rewardMultiplier;
        public uint      LastHerdingRevision => _lastHerdingRevision;

        /// <summary> 사망 시 서버에서 발생한다, killerNetId를 전달한다. 이벤트 발행, 오브젝트 정리는 EnemyController가 한다 </summary>
        public event Action<uint> Died;

        /// <summary> 치명타가 아닌 모든 피격에서 발생한다, attackerNetId 전달. 인지 범위 밖이어도 공격자를 강제 인식시키는 데 쓴다 </summary>
        public event Action<uint> Damaged;

        /// <summary> 넉백 문턱에 못 미친 일반 피격, 경직 여부는 EnemyController가 Brain 상태를 보고 정한다 </summary>
        public event Action HitLanded;

        /// <summary> 일반 피격이 HitsToKnockback만큼 누적되면 발생한다 </summary>
        public event Action Knockback;

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
