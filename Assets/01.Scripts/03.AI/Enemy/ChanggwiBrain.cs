// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 창귀 행동 결정: 스폰 후에는 스폰 지점 주변을 배회하다가, 대상을 인지하거나 피격당하면
    /// 곧장 대상을 향해 직선 돌진한다. 돌진은 패링 여부와 무관하게 항상 명중 거리까지 도달해
    /// 대상 앞에 멈춘 뒤, 그 후딜 구간에서 패링당하면 넉백, 아니면 근접 공격 2종 중 하나를
    /// 무작위로 반복한다 — 성공하든 실패하든 정지 위치가 항상 대상 앞으로 일정하다.
    /// IParryable을 구현하지만 실제 패링 창 등록(OpenParryWindow)은 아직 연결하지 않는다 —
    /// AI가 Geumeum.Combat을 직접 참조할 수 없어 Combat 쪽 IParryWindowTarget 신설이 선행돼야 한다
    /// </summary>
    public sealed class ChanggwiBrain : EnemyBrainBase, IParryable
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

        private readonly Vector3 _spawnPosition; // 탐색 모드 배회의 중심점

        private BrainState _state              = BrainState.PatrolIdle;
        private float      _stateTimer         = 0f;
        private Vector3    _chargeDestination;
        private float      _chargeMaxDuration; // 목표에 못 미쳐도 이 시간이 지나면 돌진을 끝낸다
        private Vector3    _patrolDestination;
        private int         _attackVariant      = 0;

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

        public override bool IsAttackCommitted => (_state == BrainState.Telegraph) || (_state == BrainState.Charge) || (_state == BrainState.Attack);

        public override EnemyVisualState VisualState => ToVisualState(_state);

        /// <summary> 이번 공격에 쓸 모션 인덱스(0=Attack, 1=JumpAttack), 애니메이션 표현 전용 </summary>
        public override int AttackVariant => _attackVariant;

        /// <summary> IParryable — 패링 판정 창 길이, 패링 시스템이 연결되면 사용된다 </summary>
        public float ParryWindowDuration => Status.ParryWindowDuration;

        /****************************************
        *              Constructor
        ****************************************/

        public ChanggwiBrain(Transform self, EnemyStatus status) : base(self, status)
        {
            _spawnPosition = self.position;
        }

        /****************************************
        *                Methods
        ****************************************/

        public override void Tick(float deltaTime)
        {
            if (Status.IsDead)
            {
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
        /// 패링 성공 시 서버가 호출한다. 돌진 도착 후 후딜(Telegraph) 구간에서만 반응한다 —
        /// 이동 중(Charge)에는 반응하지 않아, 패링 성공·실패 모두 대상 앞에 도착한 뒤에 결과가 갈린다
        /// </summary>
        public void OnParried(uint parriedByNetId)
        {
            if (_state != BrainState.Telegraph)
            {
                return; // 돌진 이동 중이거나 이미 끝난 후딜에는 반응하지 않는다, 중복 반응 방지
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

        /// <summary> 피격 누적 문턱 도달 시 EnemyController가, 패링 성공 시 OnParried가 호출한다 </summary>
        public override void EnterKnockback()
        {
            if (Status.IsDead)
            {
                return;
            }

            _state      = BrainState.Knockback;
            _stateTimer = 0f;
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

                // TODO(오나): Combat 쪽에 IParryWindowTarget이 생기면 여기서 대상의 그 인터페이스를 찾아
                // OpenParryWindow(windowId, attackerNetId, this)를 호출한다. 지금은 이 호출이 없어서
                // OnParried가 절대 실행되지 않는다 — 패링 판정이 이 창귀 패턴과 연결되지 않은 상태다.
                // 도착(Telegraph 진입) 시점에 여는 게 맞다 — 이동 중(Charge)에 열면 패링 성공 시
                // 대상 앞에 도착하기 전에 멈춰버려, 성공·실패의 정지 위치가 달라진다
                return;
            }

            bool reachedDestination = Vector3.Distance(Self.position, _chargeDestination) <= ARRIVAL_EPSILON;
            if (reachedDestination || (_stateTimer >= _chargeMaxDuration))
            {
                // 돌진이 빗나갔다, Recovery에서 거리를 다시 재고 재돌진 여부를 정한다
                _state      = BrainState.Recovery;
                _stateTimer = 0f;
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
            _state      = BrainState.Recovery;
            _stateTimer = 0f;
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
    }
}
