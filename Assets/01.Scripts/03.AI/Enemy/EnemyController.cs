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
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 공통 실행부. Brain의 판단을 받아 실제 추적 · 공격을 수행하고 사망 이벤트를 발행한다.
    /// EnemyKind로 알맞은 Brain을 골라 데이터 구동한다, 종류별 상속은 쓰지 않는다
    /// </summary>
    [RequireComponent(typeof(EnemyStatus))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : NetworkBehaviour, IPrecisionInterruptTarget
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
        private IDamageable    _currentTargetDamageable;

        private Vector3 _herdingCenter;
        private float   _herdingRadius;
        private double  _nextHerdingPathGameplayTime;
        private int     _herdingPathFailureCount;
        private bool    _hasHerdingPath;
        private bool    _herdingPathFailed;
        private uint    _lastPrecisionInterruptedExecutionId;
        private bool    _isRunPauseApplied;
        private bool    _wasAgentStoppedBeforeRunPause;

        private static ulong G_HerdingBudgetGeneration = 0UL;
        private static ulong G_HerdingBudgetTick       = 0UL;
        private static int   G_HerdingBudgetUsed       = 0;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 표현 전용 컴포넌트(EnemyAnimationController)가 구독한다. 서버 · 클라이언트 모두에서 값이 바뀔 때마다 발생한다 </summary>
        public event Action<EnemyVisualState> VisualStateChanged;

        /// <summary> 공격 모션 인덱스가 바뀔 때마다 발생한다, VisualStateChanged와 같은 틱에 함께 갱신된다 </summary>
        public event Action<int> AttackVariantChanged;

        public bool IsHerding => (_brain != null) && _brain.IsHerding;
        public bool HerdingPathFailed => _herdingPathFailed;
        public TargetRef PrecisionTargetRef => CreateBodyTargetRef();

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHerdingBudget()
        {
            G_HerdingBudgetGeneration = 0UL;
            G_HerdingBudgetTick       = 0UL;
            G_HerdingBudgetUsed       = 0;
        }

        public override void OnStartServer()
        {
            int fieldLevel = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.FieldLevel : 0;
            _status.Initialize(fieldLevel);

            _defaultSpeed = _agent.speed;
            _brain        = CreateBrain(_status.Kind);
            _currentTargetDamageable = null;
            _herdingPathFailureCount = 0;
            _hasHerdingPath          = false;
            _herdingPathFailed       = false;
            _lastPrecisionInterruptedExecutionId = 0u;
            _status.Died       += HandleDied;
            _status.Damaged    += HandleDamaged;
            _status.HitLanded  += HandleHitLanded;
            _status.Knockback  += HandleKnockback;

            GameEventBus.Instance.Subscribe<RunPausedEvent>(HandleRunPaused);
            GameEventBus.Instance.Subscribe<RunResumedEvent>(HandleRunResumed);
            if (RunTimeLocator.Current.IsPaused)
            {
                ApplyRunPause();
            }

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
            MarkRegistry.ClearEntity(netId);

            _status.Died      -= HandleDied;
            _status.Damaged   -= HandleDamaged;
            _status.HitLanded -= HandleHitLanded;
            _status.Knockback -= HandleKnockback;

            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Unsubscribe<RunPausedEvent>(HandleRunPaused);
                GameEventBus.Instance.Unsubscribe<RunResumedEvent>(HandleRunResumed);
            }

            _isRunPauseApplied = false;

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

            if (RunTimeLocator.Current.IsPaused)
            {
                ApplyRunPause();
                return;
            }

            RestoreAfterRunPause();

            if (EnemyTargetRules.IsUnavailable(_currentTargetDamageable))
            {
                _currentTargetDamageable = null;
                _brain.Target            = null;
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

        /// <summary> 목표 원 밖의 개체를 강화하고 NavMesh 몰이 이동을 예약한다 </summary>
        [Server]
        public bool ServerBeginHerding(Vector3 targetCenter, float targetRadius,
            int targetFieldLevel, uint revision)
        {
            if ((_brain == null) || _status.IsDead || (revision == 0u) ||
                (revision <= _brain.HerdingRevision) ||
                float.IsNaN(targetRadius) || float.IsInfinity(targetRadius) ||
                (targetRadius <= 0f) ||
                BarrierTargetSelector.IsInside(transform.position, targetCenter, targetRadius))
            {
                return false;
            }

            if (!_status.ServerTryApplyHerdingEnhancement(targetFieldLevel, revision))
            {
                Debug.LogError($"[EnemyController] netId={netId} revision={revision} 몰이 강화를 적용하지 못했다", this);
                return false;
            }

            Vector3 destination = BarrierTargetSelector.GetHerdingDestination(
                transform.position, targetCenter, targetRadius,
                GameDB.Spawn.HerdingDestinationInset);
            if (!_brain.BeginHerding(destination, revision))
            {
                return false;
            }

            _herdingCenter           = targetCenter;
            _herdingRadius           = targetRadius;
            _herdingPathFailureCount = 0;
            _hasHerdingPath          = false;
            _herdingPathFailed       = false;

            int spreadSlots = Mathf.Max(GameDB.Spawn.HerdingPathRequestsPerTick * 2, 1);
            _nextHerdingPathGameplayTime = RunTimeLocator.Current.FieldElapsedSeconds +
                ((netId % (uint) spreadSlots) * GameDB.Spawn.HerdingRequestSpreadSeconds);
            return true;
        }

        /// <summary> 현재 허용된 잡귀 공격을 실제 정밀 화살 명중으로 중단한다 </summary>
        [Server]
        public bool TryInterruptPrecisionShot(in PrecisionInterruptContext context)
        {
            if ((_brain == null) || _status.IsDead || !context.IsPrecision ||
                (context.ActionId == 0u) || !context.Attacker.IsValid ||
                double.IsNaN(context.HitServerTime) ||
                double.IsInfinity(context.HitServerTime) ||
                ((_status.Kind != EnemyKind.Heokkaebi) &&
                 (_status.Kind != EnemyKind.Changgwi)))
            {
                return false;
            }

            TargetRef currentTarget = CreateBodyTargetRef();
            uint executionId = _brain.ActiveAttackExecutionId;
            if (!currentTarget.IsValid || !context.Target.Equals(currentTarget) ||
                (executionId == 0u) ||
                (executionId == _lastPrecisionInterruptedExecutionId))
            {
                return false;
            }

            if (!_brain.TryInterruptPrecision(executionId))
            {
                return false;
            }

            _lastPrecisionInterruptedExecutionId = executionId;
            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = true;
            }

            return true;
        }

        private bool TryApplyHerdingMovement()
        {
            if (!_brain.IsHerding)
            {
                return false;
            }

            uint revision = _brain.HerdingRevision;
            if (BarrierTargetSelector.IsInside(transform.position,
                    _herdingCenter, _herdingRadius))
            {
                _brain.CompleteHerding(revision);
                if (_agent.isOnNavMesh)
                {
                    _agent.ResetPath();
                }

                return false;
            }

            if (!_brain.AllowsHerdingMovement)
            {
                return false;
            }

            if (_herdingPathFailed)
            {
                return true;
            }

            double now = RunTimeLocator.Current.FieldElapsedSeconds;
            if (_hasHerdingPath)
            {
                if (!_agent.isOnNavMesh)
                {
                    _hasHerdingPath = false;
                    RegisterHerdingPathFailure("NavMesh에서 이탈했다", now);
                    return true;
                }

                if (_agent.pathPending)
                {
                    return true;
                }

                if (_agent.pathStatus == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }

                _hasHerdingPath = false;
                RegisterHerdingPathFailure("계산된 경로가 완전하지 않다", now);
            }

            if ((now < _nextHerdingPathGameplayTime) || !TryConsumeHerdingPathBudget())
            {
                return true;
            }

            if (!_agent.isOnNavMesh ||
                !NavMesh.SamplePosition(_brain.HerdingDestination,
                    out NavMeshHit hit, GameDB.Spawn.HerdingNavMeshSampleRadius,
                    NavMesh.AllAreas) || !_agent.SetDestination(hit.position))
            {
                RegisterHerdingPathFailure("NavMesh 목적지를 만들지 못했다", now);
                return true;
            }

            _hasHerdingPath = true;
            return true;
        }

        private void RegisterHerdingPathFailure(string reason, double now)
        {
            ++_herdingPathFailureCount;
            _nextHerdingPathGameplayTime = now + GameDB.Spawn.HerdingPathRetrySeconds;
            if (_herdingPathFailureCount < GameDB.Spawn.HerdingMaxPathFailures)
            {
                return;
            }

            _herdingPathFailed = true;
            Debug.LogError($"[EnemyController] netId={netId} revision={_brain.HerdingRevision} 몰이 경로 실패: {reason}", this);
        }

        private static bool TryConsumeHerdingPathBudget()
        {
            AuthorityTickScheduler scheduler = AuthorityTickScheduler.ServerInstance;
            if (scheduler == null)
            {
                return false;
            }

            ulong generation = AuthorityTickScheduler.ServerGeneration;
            if ((G_HerdingBudgetGeneration != generation) ||
                (G_HerdingBudgetTick != scheduler.TickId))
            {
                G_HerdingBudgetGeneration = generation;
                G_HerdingBudgetTick       = scheduler.TickId;
                G_HerdingBudgetUsed       = 0;
            }

            if (G_HerdingBudgetUsed >= GameDB.Spawn.HerdingPathRequestsPerTick)
            {
                return false;
            }

            ++G_HerdingBudgetUsed;
            return true;
        }

        private TargetRef CreateBodyTargetRef()
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                TargetRef.BODY_PART_ID);
        }

        [Server]
        private void RefreshTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, TARGET_SCAN_RADIUS, _scanBuffer, _playerLayerMask);

            Transform nearest         = null;
            IDamageable nearestDamageable = null;
            float     nearestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                // 콜라이더가 플레이어 자식 오브젝트에 붙어 있을 수 있어, 콜라이더 본인이 아니라
                // 부모로 올라가며 IDamageable을 찾는다 (DamageTargetResolver 참고)
                if ((!DamageTargetResolver.TryResolve(_scanBuffer[i], out IDamageable damageable)) ||
                    EnemyTargetRules.IsUnavailable(damageable))
                {
                    continue; // 다운·관전 중인 플레이어는 때려도 무의미하다, 살아있는 대상만 노린다
                }

                Transform candidate = ((Component)damageable).transform;
                float     distance  = Vector3.Distance(transform.position, candidate.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest         = candidate;
                    nearestDamageable = damageable;
                }
            }

            _currentTargetDamageable = nearestDamageable;
            _brain.Target            = nearest;
        }

        [Server]
        private void ApplyBrainDecision()
        {
            bool herdingOwnsMovement = TryApplyHerdingMovement();
            Vector3? destination = _brain.ChaseDestinationOverride;
            if ((destination == null) && (_brain.Target != null))
            {
                destination = _brain.Target.position;
            }

            if (herdingOwnsMovement)
            {
                _agent.speed     = _defaultSpeed;
                _agent.isStopped = _herdingPathFailed;
            }
            else if (_brain.ShouldChase && (destination != null))
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
            if ((target == null) ||
                !DamageTargetResolver.TryResolve(target, out IDamageable damageable) ||
                EnemyTargetRules.IsUnavailable(damageable))
            {
                return;
            }

            if (_brain.IsRangedAttack)
            {
                SpawnProjectile(target);
                return;
            }

            damageable.TakeDamage(new DamageInfo(_status.AttackPower, netId, HitStrength.Light));
        }

        [Server]
        private void HandleRunPaused(RunPausedEvent evt)
        {
            ApplyRunPause();
        }

        [Server]
        private void HandleRunResumed(RunResumedEvent evt)
        {
            RestoreAfterRunPause();
        }

        private void ApplyRunPause()
        {
            if (_isRunPauseApplied)
            {
                return;
            }

            _isRunPauseApplied = true;
            if ((_agent == null) || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            {
                return;
            }

            _wasAgentStoppedBeforeRunPause = _agent.isStopped;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        private void RestoreAfterRunPause()
        {
            if (!_isRunPauseApplied)
            {
                return;
            }

            _isRunPauseApplied = false;
            if ((_agent == null) || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            {
                return;
            }

            _agent.isStopped = _wasAgentStoppedBeforeRunPause;
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

            if (!DamageTargetResolver.TryResolve(attacker.transform,
                    out IDamageable damageable) ||
                EnemyTargetRules.IsUnavailable(damageable))
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, attacker.transform.position);
            if (distance > TARGET_SCAN_RADIUS)
            {
                return; // 너무 멀리서 온 피해(투사체 등)로는 강제 인식하지 않는다
            }

            _currentTargetDamageable = damageable;
            _brain.Target            = attacker.transform;
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
            MarkRegistry.ClearEntity(netId);

            // HasInstance->Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다, Instance를 한 번만 받는다
            GameEventBus bus = GameEventBus.Instance;
            if (bus != null)
            {
                bus.Publish(EnemyDiedEvent.ForEnemy(netId, _status.Kind, transform.position,
                    killerNetId, _status.FieldLevel, _status.RewardMultiplier));
            }

            NetworkServer.Destroy(gameObject);
        }
    }
}
