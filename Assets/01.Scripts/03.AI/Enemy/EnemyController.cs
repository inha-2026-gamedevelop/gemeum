// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 공통 실행부. Brain의 판단을 받아 실제 추적 · 공격을 수행하고 사망 이벤트를 발행한다.
    /// EnemyKind로 알맞은 Brain을 골라 데이터 구동한다, 종류별 상속은 쓰지 않는다
    /// </summary>
    [RequireComponent(typeof(EnemyStatus))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TARGET_SCAN_INTERVAL = 0.5f; // 매 프레임 탐색 대신 주기 탐색
        private const float TARGET_SCAN_RADIUS    = 20f;  // Brain의 AggroRange보다 넉넉히 잡고, 실제 인지 여부는 Brain이 판단

        [SerializeField] private EnemyStatus  _status;
        [SerializeField] private NavMeshAgent _agent;

        private EnemyBrainBase _brain;
        private float          _scanTimer = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        public override void OnStartServer()
        {
            _brain = CreateBrain(_status.Kind);
            _status.Died += HandleDied;
        }

        public override void OnStopServer()
        {
            _status.Died -= HandleDied;
        }

        private void Update()
        {
            if (!isServer || _status.IsDead || (_brain == null))
            {
                return;
            }

            _scanTimer += Time.deltaTime;
            if (_scanTimer >= TARGET_SCAN_INTERVAL)
            {
                _scanTimer = 0f;
                RefreshTarget();
            }

            _brain.Tick(Time.deltaTime);
            ApplyBrainDecision();
        }

        /****************************************
        *              Server API
        ****************************************/

        [Server]
        private EnemyBrainBase CreateBrain(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Heokkaebi:
                    return new HeokkaebiBrain(transform, _status);
                default:
                    Debug.LogError($"[EnemyController] {kind} 종류의 Brain이 아직 없다", this);
                    return null;
            }
        }

        [Server]
        private void RefreshTarget()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, TARGET_SCAN_RADIUS, LayerMask.GetMask(Constants.Layer.PLAYER));

            Transform nearest         = null;
            float     nearestDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; ++i)
            {
                float distance = Vector3.Distance(transform.position, hits[i].transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest         = hits[i].transform;
                }
            }

            _brain.Target = nearest;
        }

        [Server]
        private void ApplyBrainDecision()
        {
            if (_brain.ShouldChase && (_brain.Target != null))
            {
                _agent.isStopped = false;
                _agent.SetDestination(_brain.Target.position);
            }
            else
            {
                _agent.isStopped = true;
            }

            if (_brain.ShouldAttackNow)
            {
                ExecuteAttack(_brain.Target);
            }
        }

        [Server]
        private void ExecuteAttack(Transform target)
        {
            if (target == null)
            {
                return;
            }

            if (!target.TryGetComponent(out IDamageable damageable))
            {
                return;
            }

            damageable.TakeDamage(new DamageInfo(_status.AttackPower, netId, HitStrength.Light));
        }

        [Server]
        private void HandleDied(uint killerNetId)
        {
            GameEventBus.Instance.Publish(new EnemyDiedEvent(netId, _status.Kind, transform.position, killerNetId, false));
            NetworkServer.Destroy(gameObject);
        }
    }
}
