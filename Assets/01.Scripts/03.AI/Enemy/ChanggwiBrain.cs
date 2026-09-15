// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 창귀 행동 결정: 스폰 후에는 스폰 지점 주변을 배회하다가, 대상을 인지하거나 피격당하면
    /// 곧장 대상을 향해 직선 돌진한다. 돌진은 패링 여부와 무관하게 항상 명중 거리까지 도달해
    /// 대상 앞에 멈춘 뒤, 그 후딜 구간에서 패링당하면 넉백, 아니면 근접 공격 2종 중 하나를
    /// 무작위로 반복한다 — 성공하든 실패하든 정지 위치가 항상 대상 앞으로 일정하다.
    /// 대상의 IParryWindowTarget을 직접 찾아 창을 열고 닫는다 — AI가 Geumeum.Combat을 직접
    /// 참조하지 않도록 Common의 인터페이스로만 접촉한다. 패링 source는 이 Brain 자신이 아니라
    /// 공격마다 새로 만드는 ChanggwiAttackExecution이다 — 그래야 이전 공격의 늦은 패링 성공
    /// 통지가 이미 시작된 다음 공격을 잘못 넉백시키지 않는다 (MONSTER_PARRY_LINK_DESIGN.md 3.3절)
    /// </summary>
    public sealed class ChanggwiBrain : EnemyBrainBase
    {
        /****************************************
        *                Types
        ****************************************/

        public enum BrainState
        {
            PatrolIdle, // 탐색 모드, 제자리 대기
            PatrolWalk, // 탐색 모드, 스폰 지점 주변 랜덤 지점으로 이동
            Charge,     // 전투 모드, 고정된 방향으로 대상을 향해 직선 돌진
            Telegraph,  // 돌진 도착 후 공격 전 대기 — 이 구간이 지나야 공격한다
            Attack,     // 이번 틱에 공격 판정 실행
            Recovery,   // 공격 · 빗나간 돌진 직후 재행동 대기
            Stagger,    // 비공격 상태에서 일반 피격을 당해 경직
            Knockback,  // 피격 누적 문턱 도달 또는 패링당해 넉백
        }

        /****************************************
        *              Constants
        ****************************************/

        private const float ARRIVAL_EPSILON     = 0.3f; // 돌진 목적지 도달 판정 여유
        private const int   ATTACK_VARIANT_COUNT = 2;    // 일반 공격 종류 수(Attack, JumpAttack)

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3         _spawnPosition; // 탐색 모드 배회의 중심점
        private readonly NetworkIdentity _selfIdentity;  // 패링 창 키의 공격자 netId 조회용, 초기화 시점에 캐싱

        private BrainState               _state              = BrainState.PatrolIdle;
        private float                    _stateTimer         = 0f;
        private Vector3                  _chargeDestination;
        private float                    _chargeMaxDuration; // 목표에 못 미쳐도 이 시간이 지나면 돌진을 끝낸다
        private Vector3                  _patrolDestination;
        private int                      _attackVariant      = 0;
        private uint                     _nextParryWindowId  = 0;
        private uint                     _nextAttackExecutionId   = 0u;
        private uint                     _activeAttackExecutionId = 0u;
        private ChanggwiAttackExecution  _activeExecution; // 지금 열려 있는 창을 낸 실행, 없으면 null
        private IParryWindowTarget       _openParryTarget; // 위 실행이 연 창의 대상, 없으면 null
        private ParryWindowKey           _openParryKey;
        private double                   _parryVisualOpenTime;  // 표현 전용, 서버 판정 구간(windowStart)과 같은 공식으로 근사 계산
        private double                   _parryVisualCloseTime; // 표현 전용, windowEnd 근사치

        /****************************************
        *              Properties
        ****************************************/

        public BrainState State => _state;

        public override bool ShouldChase     => (_state == BrainState.PatrolWalk) || (_state == BrainState.Charge);
        public override bool ShouldAttackNow => _state == BrainState.Attack;

        public override Vector3? ChaseDestinationOverride =>
            (_state == BrainState.PatrolWalk) ? _patrolDestination :
            (_state == BrainState.Charge)     ? _chargeDestination : (Vector3?)null;

        public override float? MoveSpeedOverride => (_state == BrainState.Charge) ? Status.ChargeSpeed : (float?)null;

        // 시전 사거리(AttackRange)는 "이 거리면 돌진을 시작한다"는 값이라 실제 손이 닿는 거리보다 훨씬 멀다.
        // 때리는 것은 돌진이 멈춰 서는 자리이므로 그 반경을 쓴다
        public override float MeleeHitRange => Status.ChargeHitRadius;

        public override bool IsAttackCommitted => (_state == BrainState.Telegraph) || (_state == BrainState.Charge) || (_state == BrainState.Attack);

        public override EnemyVisualState VisualState => ToVisualState(_state);

        /// <summary>
        /// 실제 서버 패링 판정 구간(windowStart~windowEnd)을 같은 공식으로 근사 계산해 반영한다.
        /// 로드아웃 보너스(ParryWindowBonus)는 여기서 모르므로 반영하지 않는다 — 표현 전용이라 오차 허용
        /// </summary>
        public override bool IsParryWindowVisuallyOpen =>
            (_state == BrainState.Telegraph) &&
            (_parryVisualCloseTime > _parryVisualOpenTime) &&
            (NetworkTime.time >= _parryVisualOpenTime) &&
            (NetworkTime.time <= _parryVisualCloseTime);

        public override bool AllowsHerdingMovement =>
            (_state == BrainState.PatrolIdle) || (_state == BrainState.PatrolWalk) ||
            (_state == BrainState.Recovery);

        public override uint ActiveAttackExecutionId =>
            ((_state == BrainState.Charge) || (_state == BrainState.Telegraph))
                ? _activeAttackExecutionId
                : 0u;

        /// <summary> 이번 공격에 쓸 모션 인덱스(0=Attack, 1=JumpAttack), 애니메이션 표현 전용 </summary>
        public override int AttackVariant => _attackVariant;

        /****************************************
        *              Constructor
        ****************************************/

        public ChanggwiBrain(Transform self, EnemyStatus status) : base(self, status)
        {
            _spawnPosition = self.position;
            _selfIdentity  = self.GetComponent<NetworkIdentity>();
        }

        /****************************************
        *                Methods
        ****************************************/

        public override void Tick(float deltaTime)
        {
            if (Status.IsDead)
            {
                // 죽은 뒤에도 창이 열려 있으면 대상이 이미 사라진 공격을 패링해 성공하는 상황이 생긴다
                CloseParryWindowIfOpen();
                return;
            }

            switch (_state)
            {
                case BrainState.PatrolIdle:
                    TickPatrolIdle(deltaTime);
                    break;
                case BrainState.PatrolWalk:
                    TickPatrolWalk(deltaTime);
                    break;
                case BrainState.Charge:
                    TickCharge(deltaTime);
                    break;
                case BrainState.Telegraph:
                    TickTelegraph(deltaTime);
                    break;
                case BrainState.Attack:
                    TickAttack();
                    break;
                case BrainState.Recovery:
                    TickRecovery(deltaTime);
                    break;
                case BrainState.Stagger:
                    TickStagger(deltaTime);
                    break;
                case BrainState.Knockback:
                    TickKnockback(deltaTime);
                    break;
            }
        }

        /// <summary>
        /// 패링 성공 시 그 공격의 ChanggwiAttackExecution이 호출한다. 지금 활성 실행과 참조가
        /// 같고 돌진 도착 후 후딜(Telegraph) 구간일 때만 반응한다 — 참조가 다르면 이미 다음
        /// 공격으로 넘어간 뒤 도착한 이전 공격의 늦은 통지이므로 무시한다
        /// </summary>
        internal void HandleExecutionParried(ChanggwiAttackExecution execution, uint parriedByNetId)
        {
            if ((!ReferenceEquals(execution, _activeExecution)) || (_state != BrainState.Telegraph))
            {
                return;
            }

            Status.ResetHitCount(); // 넉백 직후 곧바로 일반 피격 누적이 겹쳐 문턱을 채우지 않도록
            EnterKnockback();
        }

        /// <summary> 비공격 상태에서 일반 피격을 당하면 EnemyController가 호출한다 </summary>
        public override void EnterStagger()
        {
            if (Status.IsDead || (_state == BrainState.Knockback))
            {
                return; // 넉백 중에는 더 약한 경직으로 덮어쓰지 않는다
            }

            _state      = BrainState.Stagger;
            _stateTimer = 0f;
        }

        /// <summary> 피격 누적 문턱 도달 시 EnemyController가, 패링 성공 시 HandleExecutionParried가 호출한다 </summary>
        public override void EnterKnockback()
        {
            if (Status.IsDead)
            {
                return;
            }

            // 패링 성공 경로(HandleExecutionParried)는 서버가 이미 창을 닫은 뒤 호출하므로 여기서
            // 또 닫아도 안전하다 — 넉백 문턱 도달처럼 패링을 거치지 않고 들어온 경로에서 열린 창을 정리한다
            CloseParryWindowIfOpen();

            _activeAttackExecutionId = 0u;
            _state                    = BrainState.Knockback;
            _stateTimer               = 0f;
        }

        /// <summary> 인지 범위 밖에서 공격당했을 때 EnemyController가 호출한다, 탐색 모드일 때만 즉시 돌진으로 전환한다 </summary>
        public override void ForceAggro()
        {
            if (Status.IsDead || ((_state != BrainState.PatrolIdle) && (_state != BrainState.PatrolWalk)))
            {
                return; // 이미 전투 중이면 끼어들지 않는다
            }

            EnterCharge();
        }

        public override bool TryInterruptPrecision(uint expectedExecutionId)
        {
            bool hasInterruptWindow = (_state == BrainState.Charge) ||
                (_state == BrainState.Telegraph);
            if (!hasInterruptWindow || (expectedExecutionId == 0u) ||
                (expectedExecutionId != _activeAttackExecutionId))
            {
                return false;
            }

            CloseParryWindowIfOpen();
            _activeAttackExecutionId = 0u;
            _state                    = BrainState.Stagger;
            _stateTimer               = 0f;
            return true;
        }

        private void TickPatrolIdle(float deltaTime)
        {
            if (TryEnterCombat())
            {
                return;
            }

            _stateTimer += deltaTime;
            if (_stateTimer >= Status.PatrolIdleDuration)
            {
                EnterPatrolWalk();
            }
        }

        private void TickPatrolWalk(float deltaTime)
        {
            if (TryEnterCombat())
            {
                return;
            }

            _stateTimer += deltaTime;
            if (_stateTimer >= Status.PatrolWalkDuration)
            {
                _state      = BrainState.PatrolIdle;
                _stateTimer = 0f;
            }
        }

        private void EnterPatrolWalk()
        {
            Vector2 offset = Random.insideUnitCircle * Status.PatrolRadius;
            _patrolDestination = _spawnPosition + new Vector3(offset.x, 0f, offset.y);
            _state              = BrainState.PatrolWalk;
            _stateTimer         = 0f;
        }

        /// <summary> 대상이 인지 범위 안이면 돌진을 시작하고 true를 반환한다, 탐색 상태에서만 호출한다 </summary>
        private bool TryEnterCombat()
        {
            if (Target == null)
            {
                return false;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance > Status.AggroRange)
            {
                return false;
            }

            EnterCharge();
            return true;
        }

        private void EnterCharge()
        {
            Vector3 direction = (Target != null)
                ? (Target.position - Self.position)
                : Self.forward;
            direction.y = 0f;

            Vector3 chargeDirection = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : Self.forward;

            _nextAttackExecutionId = NextExecutionId(_nextAttackExecutionId);
            _activeAttackExecutionId = _nextAttackExecutionId;
            _chargeDestination = Self.position + (chargeDirection * Status.ChargeDistance);
            _chargeMaxDuration = (Status.ChargeSpeed > 0f) ? (Status.ChargeDistance / Status.ChargeSpeed) * 1.5f : 1f;
            _state             = BrainState.Charge;
            _stateTimer        = 0f;
        }

        private void TickCharge(float deltaTime)
        {
            _stateTimer += deltaTime;

            if ((Target != null) && (Vector3.Distance(Self.position, Target.position) <= Status.ChargeHitRadius))
            {
                _state      = BrainState.Telegraph;
                _stateTimer = 0f;

                // 도착(Telegraph 진입) 시점에 여는 게 맞다 — 이동 중(Charge)에 열면 패링 성공 시
                // 대상 앞에 도착하기 전에 멈춰버려, 성공·실패의 정지 위치가 달라진다
                TryOpenParryWindow();
                return;
            }

            bool reachedDestination = Vector3.Distance(Self.position, _chargeDestination) <= ARRIVAL_EPSILON;
            if (reachedDestination || (_stateTimer >= _chargeMaxDuration))
            {
                // 돌진이 빗나갔다, Recovery에서 거리를 다시 재고 재돌진 여부를 정한다
                _activeAttackExecutionId = 0u;
                _state                    = BrainState.Recovery;
                _stateTimer               = 0f;
            }
        }

        private void TickTelegraph(float deltaTime)
        {
            // 패링당하지 않고 이 시간이 지나면 공격을 시작한다, 패링 실패에 해당하는 구간
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.WindupDuration)
            {
                EnterAttack();
            }
        }

        private void EnterAttack()
        {
            _attackVariant = Random.Range(0, ATTACK_VARIANT_COUNT);
            _state         = BrainState.Attack;
            _stateTimer    = 0f;
        }

        private void TickAttack()
        {
            // 실제 공격 판정 실행은 EnemyController가 State == Attack을 보고 이번 틱에 수행한다
            _activeAttackExecutionId = 0u;
            _state                    = BrainState.Recovery;
            _stateTimer               = 0f;
        }

        private void TickRecovery(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer < Status.AttackCooldown)
            {
                return;
            }

            if (Target == null)
            {
                _state      = BrainState.PatrolIdle;
                _stateTimer = 0f;
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance > Status.AggroRange)
            {
                _state      = BrainState.PatrolIdle; // 놓쳤다, 탐색 모드로 복귀
                _stateTimer = 0f;
                return;
            }

            if (distance <= Status.ChargeHitRadius)
            {
                EnterAttack(); // 아직 근접 거리 안이다, 후딜 없이 바로 다음 공격
                return;
            }

            EnterCharge(); // 거리가 벌어졌다, 다시 돌진해 좁힌다
        }

        private void TickStagger(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.StaggerDuration)
            {
                _state = BrainState.PatrolIdle;
            }
        }

        private void TickKnockback(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.KnockbackDuration)
            {
                _state = BrainState.PatrolIdle;
            }
        }

        /// <summary> 대상이 IParryWindowTarget을 구현하면 이번 공격 전용 실행을 만들어 창을 연다 </summary>
        private void TryOpenParryWindow()
        {
            _parryVisualOpenTime  = 0d;
            _parryVisualCloseTime = 0d;

            if ((Target == null) || (_selfIdentity == null))
            {
                return;
            }

            IParryWindowTarget parryTarget = Target.GetComponent<IParryWindowTarget>();
            if (parryTarget == null)
            {
                return;
            }

            ++_nextParryWindowId;
            ParryWindowKey key = new ParryWindowKey(_selfIdentity.netId, _nextParryWindowId);

            double now                = NetworkTime.time;
            double telegraphStartTime = now;
            double impactTime         = now + Status.WindupDuration;

            // 서버 ParryController.OpenParryWindow와 같은 역산 공식 — 표현 전용 근사치라 로드아웃
            // 보너스는 반영하지 않는다(3절 참고)
            _parryVisualCloseTime = impactTime - Constants.Network.PARRY_RESOLUTION_LEAD;
            _parryVisualOpenTime  = _parryVisualCloseTime - Status.ParryWindowDuration;

            ChanggwiAttackExecution execution = new ChanggwiAttackExecution(this, Status.ParryWindowDuration);

            if (parryTarget.OpenParryWindow(execution, key, telegraphStartTime, impactTime))
            {
                _activeExecution = execution;
                _openParryTarget = parryTarget;
                _openParryKey    = key;
            }
        }

        /// <summary> 열려 있는 패링 창이 있으면 닫는다, 없으면 아무 일도 하지 않는다 </summary>
        private void CloseParryWindowIfOpen()
        {
            if (_openParryTarget == null)
            {
                return;
            }

            _openParryTarget.CloseParryWindow(_openParryKey);
            _activeExecution = null;
            _openParryTarget = null;
            _openParryKey    = default;
        }

        private static EnemyVisualState ToVisualState(BrainState state)
        {
            switch (state)
            {
                case BrainState.PatrolIdle: return EnemyVisualState.Idle;
                case BrainState.PatrolWalk: return EnemyVisualState.Move;
                case BrainState.Charge:     return EnemyVisualState.Charge;
                case BrainState.Telegraph:  return EnemyVisualState.Telegraph;
                case BrainState.Attack:     return EnemyVisualState.Attack;
                case BrainState.Recovery:   return EnemyVisualState.Recovery;
                case BrainState.Stagger:    return EnemyVisualState.Stagger;
                case BrainState.Knockback:  return EnemyVisualState.Knockback;
                default:                    return EnemyVisualState.Idle;
            }
        }

        private static uint NextExecutionId(uint current)
        {
            uint next = current + 1u;
            return (next == 0u) ? 1u : next;
        }
    }
}
