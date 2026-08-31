// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

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

        private const float TARGET_SCAN_INTERVAL    = 0.5f; // 매 프레임 탐색 대신 주기 탐색
        private const float TARGET_SCAN_RADIUS      = 20f;  // Brain의 AggroRange보다 넉넉히 잡고, 실제 인지 여부는 Brain이 판단
        private const float PROJECTILE_RANGE_MARGIN = 1.5f; // 시전 사거리보다 이만큼 더 날아갈 여유를 준다

        [SerializeField] private EnemyStatus     _status;
        [SerializeField] private NavMeshAgent    _agent;
        [SerializeField] private CurseProjectile _projectilePrefab; // 원거리형만 배정, 근접형은 비워둔다

        private EnemyBrainBase _brain;
        private float          _scanTimer    = 0f;
        private float          _defaultSpeed = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_status == null)
            {
                _status = GetComponent<EnemyStatus>();
            }

            if (_agent == null)
            {
                _agent = GetComponent<NavMeshAgent>();
            }
        }

        public override void OnStartServer()
        {
            int fieldLevel = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.FieldLevel : 0;
            _status.Initialize(fieldLevel);

            _defaultSpeed = _agent.speed;
            _brain        = CreateBrain(_status.Kind);
            _status.Died += HandleDied;

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[EnemyController] AuthorityTickScheduler가 없어 AI를 처리할 수 없다", this);
                return;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
        }

        public override void OnStopServer()
        {
            _status.Died -= HandleDied;

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
        }

        // AuthorityTickScheduler가 물리 스텝마다 TICK_PRIORITY_AI 순서로 호출한다
        private void ServerTick(float deltaTime)
        {
            if (_status.IsDead || (_brain == null))
            {
                return;
            }

            _scanTimer += deltaTime;
            if (_scanTimer >= TARGET_SCAN_INTERVAL)
            {
                _scanTimer = 0f;
                RefreshTarget();
            }

            _brain.Tick(deltaTime);
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
                case EnemyKind.Changgwi:
                    return new ChanggwiBrain(transform, _status);
                case EnemyKind.Aekgwi:
                    return new AekgwiBrain(transform, _status);
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
                if (hits[i].TryGetComponent(out IDamageable damageable) && damageable.IsDead)
                {
                    continue; // 다운·관전 중인 플레이어는 때려도 무의미하다, 살아있는 대상만 노린다
                }

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
            Vector3? destination = _brain.ChaseDestinationOverride;
            if ((destination == null) && (_brain.Target != null))
            {
                destination = _brain.Target.position;
            }

            if (_brain.ShouldChase && (destination != null))
            {
                _agent.speed     = _brain.MoveSpeedOverride ?? _defaultSpeed;
                _agent.isStopped = false;
                _agent.SetDestination(destination.Value);
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

            if (_brain.IsRangedAttack)
            {
                SpawnProjectile(target);
                return;
            }

            if (!target.TryGetComponent(out IDamageable damageable))
            {
                return;
            }

            damageable.TakeDamage(new DamageInfo(_status.AttackPower, netId, HitStrength.Light));
        }

        [Server]
        private void SpawnProjectile(Transform target)
        {
            if (_projectilePrefab == null)
            {
                Debug.LogError($"[EnemyController] {_status.Kind}의 투사체 프리팹이 비어있다", this);
                return;
            }

            Vector3 direction = target.position - transform.position;
            direction.y = 0f;
            direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : transform.forward;

            CurseProjectile projectile = Instantiate(_projectilePrefab, transform.position, Quaternion.LookRotation(direction));
            projectile.Initialize(direction, _status.ProjectileSpeed, _status.AttackPower, netId, _status.AttackRange * PROJECTILE_RANGE_MARGIN);
            NetworkServer.Spawn(projectile.gameObject);
        }

        [Server]
        private void HandleDied(uint killerNetId)
        {
            // HasInstance→Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다, Instance를 한 번만 받는다
            GameEventBus bus = GameEventBus.Instance;
            if (bus != null)
            {
                bus.Publish(new EnemyDiedEvent(netId, _status.Kind, transform.position, killerNetId, false));
            }

            NetworkServer.Destroy(gameObject);
        }
    }
}
