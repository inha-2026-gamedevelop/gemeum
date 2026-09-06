// System
using System;
using System.Collections;

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
        private const int   TARGET_SCAN_BUFFER_SIZE = 8;    // 파티 최대 인원(3)보다 넉넉히 잡은 NonAlloc 버퍼 크기

        [SerializeField] private EnemyStatus     _status;
        [SerializeField] private NavMeshAgent    _agent;
        [SerializeField] private CurseProjectile _projectilePrefab; // 원거리형만 배정, 근접형은 비워둔다

        [SyncVar(hook = nameof(OnVisualStateChanged))]
        private EnemyVisualState _visualState = EnemyVisualState.Idle;

        [SyncVar(hook = nameof(OnAttackVariantChanged))]
        private int _attackVariant = 0;

        private EnemyBrainBase _brain;
        private float          _scanTimer       = 0f;
        private float          _defaultSpeed    = 0f;
        private Coroutine      _schedulerWaitRoutine;
        private int            _playerLayerMask;
        private readonly Collider[] _scanBuffer = new Collider[TARGET_SCAN_BUFFER_SIZE];

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 표현 전용 컴포넌트(EnemyAnimationController)가 구독한다. 서버 · 클라이언트 모두에서 값이 바뀔 때마다 발생한다 </summary>
        public event Action<EnemyVisualState> VisualStateChanged;

        /// <summary> 공격 모션 인덱스가 바뀔 때마다 발생한다, VisualStateChanged와 같은 틱에 함께 갱신된다 </summary>
        public event Action<int> AttackVariantChanged;

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

            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        public override void OnStartServer()
        {
            int fieldLevel = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.FieldLevel : 0;
            _status.Initialize(fieldLevel);

            _defaultSpeed = _agent.speed;
            _brain        = CreateBrain(_status.Kind);
            _status.Died       += HandleDied;
            _status.Damaged    += HandleDamaged;
            _status.HitLanded  += HandleHitLanded;
            _status.Knockback  += HandleKnockback;

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
                return;
            }

            // 씬 오브젝트 간 OnStartServer 호출 순서는 Mirror가 보장하지 않는다 —
            // 스케줄러가 아직 안 떴을 수 있으니, 뜰 때까지 기다렸다가 등록한다
            _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
        }

        public override void OnStopServer()
        {
            _status.Died      -= HandleDied;
            _status.Damaged   -= HandleDamaged;
            _status.HitLanded -= HandleHitLanded;
            _status.Knockback -= HandleKnockback;

            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
        }

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
            _schedulerWaitRoutine = null;
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
            _visualState   = _brain.VisualState;
            _attackVariant = _brain.AttackVariant;
        }

        /// <summary> _visualState 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnVisualStateChanged(EnemyVisualState oldState, EnemyVisualState newState)
        {
            VisualStateChanged?.Invoke(newState);
        }

        /// <summary> _attackVariant 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnAttackVariantChanged(int oldVariant, int newVariant)
        {
            AttackVariantChanged?.Invoke(newVariant);
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
            int count = Physics.OverlapSphereNonAlloc(transform.position, TARGET_SCAN_RADIUS, _scanBuffer, _playerLayerMask);

            Transform nearest         = null;
            float     nearestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                // 콜라이더가 플레이어 자식 오브젝트에 붙어 있을 수 있어, 콜라이더 본인이 아니라
                // 부모로 올라가며 IDamageable을 찾는다 (DamageTargetResolver 참고)
                if ((!DamageTargetResolver.TryResolve(_scanBuffer[i], out IDamageable damageable)) || (damageable.IsDead))
                {
                    continue; // 다운·관전 중인 플레이어는 때려도 무의미하다, 살아있는 대상만 노린다
                }

                Transform candidate = ((Component)damageable).transform;
                float     distance  = Vector3.Distance(transform.position, candidate.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest         = candidate;
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

            if (!DamageTargetResolver.TryResolve(target, out IDamageable damageable))
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

        /// <summary> 인지 범위 밖이어도 공격자를 즉시 인식시킨다, 스캔 반경(TARGET_SCAN_RADIUS) 밖 공격자는 무시한다 </summary>
        [Server]
        private void HandleDamaged(uint attackerNetId)
        {
            if (_brain == null)
            {
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity attacker))
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, attacker.transform.position);
            if (distance > TARGET_SCAN_RADIUS)
            {
                return; // 너무 멀리서 온 피해(투사체 등)로는 강제 인식하지 않는다
            }

            _brain.Target = attacker.transform;
            _brain.ForceAggro();
        }

        /// <summary> 넉백 문턱에 못 미친 일반 피격, 공격 판정 중이 아닐 때만 경직으로 반영한다 </summary>
        [Server]
        private void HandleHitLanded()
        {
            if ((_brain != null) && !_brain.IsAttackCommitted)
            {
                _brain.EnterStagger();
            }
        }

        [Server]
        private void HandleKnockback()
        {
            _brain?.EnterKnockback();
        }

        [Server]
        private void HandleDied(uint killerNetId)
        {
            // HasInstance->Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다, Instance를 한 번만 받는다
            GameEventBus bus = GameEventBus.Instance;
            if (bus != null)
            {
                bus.Publish(new EnemyDiedEvent(netId, _status.Kind, transform.position, killerNetId, false));
            }

            NetworkServer.Destroy(gameObject);
        }
    }
}
