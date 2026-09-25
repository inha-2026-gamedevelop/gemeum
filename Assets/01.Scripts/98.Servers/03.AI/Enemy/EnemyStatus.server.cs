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
    public partial class EnemyStatus
    {

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
        private float _knockbackDistance   = 0f;
        private float _knockbackPushSeconds = 0f;
        private int   _hitCount            = 0;
        private bool  _isDead              = false;
        private int   _fieldLevel          = -1;
        private int   _combatFieldLevel    = -1;
        private float _rewardMultiplier    = -1f;
        private uint  _lastHerdingRevision = 0u;

        // 피해 결과의 대체 적중점. 몸 콜라이더 bounds 중심을 쓴다
        private Collider _damageResultCollider            = null;
        private bool     _hasResolvedDamageResultCollider = false;

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 서버에서 개체의 전투 시각 기록을 정리한다 </summary>
        public override void OnStopServer()
        {
            CombatClock.Forget(netId);
            ServerDamageResultHub.ServerForgetObservedTarget(netId);
        }

        /// <summary> 스폰 시점 결계 단계로 스탯과 보상 snapshot을 확정한다, 이후 재계산하지 않는다 </summary>
        [Server]
        public void Initialize(int fieldLevel)
        {
            _fieldLevel       = -1;
            _combatFieldLevel = -1;
            _rewardMultiplier = -1f;
            _lastHerdingRevision = 0u;

            if ((fieldLevel < 0) || (fieldLevel >= Constants.Combat.FIELD_LEVEL_COUNT))
            {
                Debug.LogError($"[EnemyStatus] 필드 레벨이 유효하지 않다: {fieldLevel}", this);
                return;
            }

            if (!GameDB.Enemy.TryGetStat(_kind, out EnemySO.StatEntry stat))
            {
                Debug.LogError($"[EnemyStatus] {_kind} 기본 스탯을 찾지 못했다", this);
                return;
            }

            if (!GameDB.Enemy.TryGetEnhance(fieldLevel, out EnemySO.EnhanceEntry enhance))
            {
                Debug.LogError($"[EnemyStatus] 필드 레벨 {fieldLevel} 강화 데이터를 찾지 못했다", this);
                return;
            }

            if ((float.IsNaN(enhance.RewardMultiplier)) ||
                (float.IsInfinity(enhance.RewardMultiplier)) || (enhance.RewardMultiplier < 0f))
            {
                Debug.LogError($"[EnemyStatus] 필드 레벨 {fieldLevel} 보상 배수가 유효하지 않다", this);
                return;
            }

            _fieldLevel       = fieldLevel;
            _combatFieldLevel = fieldLevel;
            _rewardMultiplier = enhance.RewardMultiplier;

            _maxHealth           = stat.MaxHealth   * enhance.HealthMultiplier;
            _attackPower         = stat.AttackPower * enhance.AttackMultiplier;
            _aggroRange          = stat.AggroRange; // 사거리, 타이밍은 강화 배수 대상이 아니다
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
            _knockbackDistance   = stat.KnockbackDistance;
            _knockbackPushSeconds = stat.KnockbackPushSeconds;
            _health             = _maxHealth;
            _hitCount            = 0;
            _isDead              = false;
        }

        /// <summary> 목표 결계 밖에서 몰이를 시작할 때 기준 스탯으로 새 단계 강화를 한 번 적용한다 </summary>
        [Server]
        public bool ServerTryApplyHerdingEnhancement(int fieldLevel, uint revision)
        {
            if (_isDead || (revision == 0u) || (revision <= _lastHerdingRevision) ||
                (fieldLevel <= _combatFieldLevel) ||
                (fieldLevel < 0) || (fieldLevel >= Constants.Combat.FIELD_LEVEL_COUNT))
            {
                return false;
            }

            if (!GameDB.Enemy.TryGetStat(_kind, out EnemySO.StatEntry stat) ||
                !GameDB.Enemy.TryGetEnhance(fieldLevel, out EnemySO.EnhanceEntry enhance))
            {
                Debug.LogError($"[EnemyStatus] 몰이 강화 단계 {fieldLevel}의 기준 데이터를 찾지 못했다", this);
                return false;
            }

            if (!EnemyHerdingEnhancementRules.TryCalculate(
                    _health, _maxHealth, stat.MaxHealth, stat.AttackPower,
                    enhance.HealthMultiplier, enhance.AttackMultiplier,
                    enhance.RewardMultiplier, out float nextHealth,
                    out float nextMaxHealth, out float nextAttackPower,
                    out float nextRewardMultiplier))
            {
                Debug.LogError($"[EnemyStatus] 몰이 강화 단계 {fieldLevel}의 스탯 또는 보상 배수가 유효하지 않다", this);
                return false;
            }

            _lastHerdingRevision = revision;
            _combatFieldLevel    = fieldLevel;
            _rewardMultiplier    = nextRewardMultiplier;
            _maxHealth           = nextMaxHealth;
            _attackPower         = nextAttackPower;
            _health              = nextHealth;
            return true;
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
            CombatClock.ServerNotifyDamage(netId, damage.AttackerNetId,
                RunTimeLocator.Current.GameplayElapsedSeconds);

            float previousHealth = _health;
            _health = Mathf.Max(0f, _health - damage.Amount);

            float appliedAmount = previousHealth - _health;
            bool  isKill        = (_health <= Constants.Combat.HEALTH_EPSILON);
            RecordAppliedDamage(damage, appliedAmount);

            // 사망 처리로 개체가 정리되기 전에 보고한다. 초과 피해는 빠진 실제 감소량이다
            Vector3 center = ResolveDamageResultCenter();
            ServerDamageResultHub.ServerReport(damage, netId, DamageResult.NO_PART, false,
                appliedAmount, isKill, center);

            if (isKill)
            {
                Die(damage.AttackerNetId);
                return;
            }

            // 동료 화면의 방향 움찔. 처치는 곧바로 파괴되어 움찔할 몸이 없으므로 보내지 않는다
            if ((appliedAmount > Constants.Combat.DAMAGE_EPSILON) &&
                ServerDamageResultHub.ServerTryAcceptObservedHit(damage, netId, center,
                    out byte observedYaw, out ImpactTier observedTier))
            {
                RpcObservedHit(damage.AttackerNetId, observedYaw, (byte)observedTier);
            }

            Damaged?.Invoke(damage.AttackerNetId);
            RegisterHit(damage.AttackerNetId);
        }

        /// <summary> 패링 성공 등 넉백을 즉시 강제할 때 호출한다, 일반 피격 누적과 별도 경로다 </summary>
        [Server]
        public void ResetHitCount()
        {
            _hitCount = 0;
        }

        [Server]
        private void RegisterHit(uint attackerNetId)
        {
            ++_hitCount;
            if (_hitCount >= _hitsToKnockback)
            {
                _hitCount = 0;
                Knockback?.Invoke(attackerNetId); // 문턱을 채운 공격자 반대쪽으로 민다
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

        /// <summary> 적중점이 없는 피해 결과가 쓸 몸 중심. 콜라이더는 처음 쓸 때 한 번만 찾는다 </summary>
        private Vector3 ResolveDamageResultCenter()
        {
            if (!_hasResolvedDamageResultCollider)
            {
                _hasResolvedDamageResultCollider = true;
                TryGetComponent(out _damageResultCollider);
            }

            return ((_damageResultCollider != null) && _damageResultCollider.enabled)
                ? _damageResultCollider.bounds.center
                : transform.position;
        }

        private void RecordAppliedDamage(in DamageInfo damage, float appliedAmount)
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) ||
                (!damage.AttackerParticipant.IsValid) ||
                (appliedAmount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            TargetRef target = TargetRef.Create(run.RunId,
                (director == null) ? default : director.Revision,
                netId, TargetRef.BODY_PART_ID);
            RunStatisticsSinkLocator.Current.RecordDamage(new RunAppliedDamageFact(
                run.RunId, damage.AttackerParticipant, target, appliedAmount));
        }
    }
}
