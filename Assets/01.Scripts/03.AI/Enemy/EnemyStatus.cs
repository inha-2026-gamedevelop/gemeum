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
        public float     KnockbackDistance   => _knockbackDistance;
        public float     KnockbackPushSeconds => _knockbackPushSeconds;
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

        /// <summary> 일반 피격이 HitsToKnockback만큼 누적되면 발생한다, 문턱을 채운 마지막 피격의 attackerNetId 전달(없으면 0). 밀어낼 방향의 기준이다 </summary>
        public event Action<uint> Knockback;

        /// <summary> 동기화된 체력이 줄었다. 각 화면의 피격 플래시가 쓴다 </summary>
        public event Action HealthDecreased;

        /****************************************
        *           Client Callbacks
        ****************************************/

        /// <summary> 이 화면의 잡귀 표현(머리 위 체력 바)이 찾을 수 있게 등록한다 </summary>
        public override void OnStartClient()
        {
            LocalEnemyPresentationRegistry.Register(this);
        }

        public override void OnStopClient()
        {
            LocalEnemyPresentationRegistry.Unregister(this);
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        /// <summary>
        /// 동료가 이 잡귀를 때렸다. 관찰자 화면의 방향 움찔만 재생하도록 표현 창구로 흘린다(판정 없음)
        /// 대상별 간격은 서버가 걸렀다. 표현이 빠져도 되므로 비신뢰 채널로 받는다
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcObservedHit(uint attackerNetId, byte yaw, byte tier)
        {
            // 공격한 본인은 피해 결과 경로로 이미 움찔한다. 호스트도 ClientRpc를 로컬로 받으므로 여기서 같이 거른다
            NetworkIdentity localPlayer   = NetworkClient.localPlayer;
            uint            localPlayerId = (localPlayer != null) ? localPlayer.netId : 0u;
            if (!LocalObservedHitFeed.ShouldObserve(attackerNetId, localPlayerId))
            {
                return;
            }

            LocalObservedHitFeed.Publish(new LocalObservedHitFeed.ObservedHit(netId, attackerNetId,
                LocalObservedHitFeed.DecodeYaw(yaw), (ImpactTier)tier));
        }

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
