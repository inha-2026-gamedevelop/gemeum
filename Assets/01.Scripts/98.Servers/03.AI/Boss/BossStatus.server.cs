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
    public partial class BossStatus
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
        [Server]
        internal void ServerDevSetHealthRatio(float ratio)
        {
            _health = _maxHealth * Mathf.Clamp01(ratio);
        }
#endif
        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 시점에 GameDB.Boss.MaxHealth로 HP를 확정한다 </summary>
        public override void OnStopServer()
        {
            CombatClock.Forget(netId);
        }

        [Server]
        public void Initialize()
        {
            _maxHealth            = GameDB.Boss.MaxHealth;
            _health               = _maxHealth;
            _isDead               = false;
            _bodyDamageMultiplier = 1f;
            _minimumHealth         = 0f;
            _isInvulnerable        = false;
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

        /// <summary> 남은 꼬리가 있을 때 본체가 내려갈 수 있는 HP 하한을 설정한다 </summary>
        [Server]
        internal void ServerSetHealthFloorRatio(float ratio)
        {
            if ((float.IsNaN(ratio)) || (float.IsInfinity(ratio)))
            {
                return;
            }

            _minimumHealth = _maxHealth * Mathf.Clamp01(ratio);
        }

        /// <summary> 최종 원혼 구간의 본체 무적을 서버가 설정한다 </summary>
        [Server]
        internal void ServerSetInvulnerable(bool value)
        {
            _isInvulnerable = value;
        }

        /// <summary> 피해를 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public void TakeDamage(in DamageInfo damage)
        {
            if (_isDead || _isInvulnerable)
            {
                return;
            }

            if ((float.IsNaN(damage.Amount)) || (float.IsInfinity(damage.Amount))
                || (damage.Amount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            // 보스를 때린 플레이어의 전투 시각을 갱신한다. 보스전 중에는 줍기가 열리지 않는다
            CombatClock.ServerNotifyDamage(netId, damage.AttackerNetId,
                RunTimeLocator.Current.GameplayElapsedSeconds);

            float previousHealth = _health;
            _health = Mathf.Max(_minimumHealth,
                _health - (damage.Amount * _bodyDamageMultiplier));

            RecordAppliedDamage(damage, previousHealth - _health);

            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                Die(damage.AttackerNetId);
                return;
            }

            ObserverDispatch.Invoke(Damaged, damage.AttackerNetId, this,
                nameof(BossStatus), nameof(Damaged));
        }

        [Server]
        private void Die(uint killerNetId)
        {
            _isDead = true;
            ObserverDispatch.Invoke(Died, killerNetId, this,
                nameof(BossStatus), nameof(Died));
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
            try
            {
                RunStatisticsSinkLocator.Current.RecordDamage(new RunAppliedDamageFact(
                    run.RunId, damage.AttackerParticipant, target, appliedAmount));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BossStatus] damage fact producer failed: {exception}", this);
            }
        }
    }
}
