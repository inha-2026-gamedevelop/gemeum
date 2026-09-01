// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 액귀 행동 결정: 사거리 유지 · 후퇴하며 저주 투사체를 시전한다.
    /// 실제 투사체 스폰은 EnemyController가 IsRangedAttack을 보고 수행한다
    /// </summary>
    public sealed class AekgwiBrain : EnemyBrainBase
    {
        /****************************************
        *                Types
        ****************************************/

        public enum BrainState
        {
            Idle,      // 대상 없음 또는 인지 범위 밖
            Chase,     // 인지 범위 안, 시전 사거리 밖 — 접근 필요
            Retreat,   // 시전 사거리 안이지만 대상이 너무 가까움 — 거리를 벌린다
            Return,    // 스폰 지점 기준 리시 범위를 벗어나 추적을 포기하고 복귀 중
            Telegraph, // 사거리 유지 구간, 선딜 진행 중
            Cast,      // 선딜 끝, 이번 틱에 투사체 발사
            Cooldown,  // 시전 직후 재시전 대기
            Stagger,   // 비공격 상태에서 일반 피격을 당해 경직
            Knockback, // 피격 누적 문턱 도달 또는 패링당해 넉백
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float RETREAT_MARGIN   = 1.5f; // 후퇴 목적지를 RetreatRange보다 이만큼 더 벌려 왕복 떨림을 막는다
        private const float ARRIVAL_EPSILON  = 0.3f; // 복귀 목적지(스폰 지점) 도달 판정 여유

        private readonly Vector3 _spawnPosition; // 리시 판정과 복귀 목적지의 기준점

        private BrainState _state       = BrainState.Idle;
        private float      _stateTimer  = 0f;
        private Vector3    _retreatDestination;

        /****************************************
        *              Properties
        ****************************************/

        public BrainState State => _state;

        public override bool ShouldChase     => (_state == BrainState.Chase) || (_state == BrainState.Retreat) || (_state == BrainState.Return);
        public override bool ShouldAttackNow => _state == BrainState.Cast;
        public override bool IsRangedAttack  => true;

        public override Vector3? ChaseDestinationOverride =>
            (_state == BrainState.Retreat) ? _retreatDestination :
            (_state == BrainState.Return)  ? _spawnPosition : (Vector3?)null;

        public override bool IsAttackCommitted => (_state == BrainState.Telegraph) || (_state == BrainState.Cast);

        public override EnemyVisualState VisualState => ToVisualState(_state);

        /****************************************
        *              Constructor
        ****************************************/

        public AekgwiBrain(Transform self, EnemyStatus status) : base(self, status)
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
                case BrainState.Idle:
                    TickIdle();
                    break;
                case BrainState.Chase:
                    TickChase();
                    break;
                case BrainState.Retreat:
                    TickRetreat();
                    break;
                case BrainState.Return:
                    TickReturn();
                    break;
                case BrainState.Telegraph:
                    TickTelegraph(deltaTime);
                    break;
                case BrainState.Cast:
                    TickCast();
                    break;
                case BrainState.Cooldown:
                    TickCooldown(deltaTime);
                    break;
                case BrainState.Stagger:
                    TickStagger(deltaTime);
                    break;
                case BrainState.Knockback:
                    TickKnockback(deltaTime);
                    break;
            }
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

        /// <summary> 피격 누적 문턱 도달 시 EnemyController가 호출한다 </summary>
        public override void EnterKnockback()
        {
            if (Status.IsDead)
            {
                return;
            }

            _state      = BrainState.Knockback;
            _stateTimer = 0f;
        }

        /// <summary> 인지 범위 밖에서 공격당했을 때 EnemyController가 호출한다 </summary>
        public override void ForceAggro()
        {
            if (Status.IsDead || (_state != BrainState.Idle))
            {
                return; // 이미 다른 행동 중이면 끼어들지 않는다
            }

            _state = BrainState.Chase;
        }

        private void TickIdle()
        {
            if (Target == null)
            {
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance <= Status.AggroRange)
            {
                _state = BrainState.Chase;
            }
        }

        private void TickChase()
        {
            if (IsBeyondLeash())
            {
                EnterReturn();
                return;
            }

            if (!EvaluateRange())
            {
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance <= Status.AttackRange)
            {
                EnterCastBand(distance);
            }
        }

        private void TickRetreat()
        {
            if (IsBeyondLeash())
            {
                EnterReturn();
                return;
            }

            if (!EvaluateRange())
            {
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance >= Status.RetreatRange)
            {
                EnterCastBand(distance);
            }
        }

        /// <summary> 스폰 지점 기준 리시(AggroRange) 밖으로 나갔으면 true, 추적을 포기하고 복귀시킨다 </summary>
        private bool IsBeyondLeash()
        {
            return Vector3.Distance(Self.position, _spawnPosition) > Status.AggroRange;
        }

        private void EnterReturn()
        {
            Target      = null; // 복귀 중에는 새 타겟도 무시한다, 스폰 지점에 도착해야 다시 인지한다
            _state      = BrainState.Return;
            _stateTimer = 0f;
        }

        private void TickReturn()
        {
            if (Vector3.Distance(Self.position, _spawnPosition) <= ARRIVAL_EPSILON)
            {
                _state = BrainState.Idle;
            }
        }

        private void TickTelegraph(float deltaTime)
        {
            // 선딜 중에는 대상이 사거리를 벗어나도 끝까지 진행한다, 다른 잡귀 패턴과 동일한 원칙
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.WindupDuration)
            {
                _state = BrainState.Cast;
            }
        }

        private void TickCast()
        {
            // 실제 투사체 발사는 EnemyController가 State == Cast를 보고 이번 틱에 수행한다
            _state      = BrainState.Cooldown;
            _stateTimer = 0f;
        }

        private void TickCooldown(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.AttackCooldown)
            {
                _state = BrainState.Chase; // 다음 틱에 Chase가 거리를 다시 판단해 Retreat/Telegraph로 분기한다
            }
        }

        /// <summary> Target 유효성과 인지 범위를 확인한다, 실패하면 Idle로 돌리고 false를 반환한다 </summary>
        private bool EvaluateRange()
        {
            if (Target == null)
            {
                _state = BrainState.Idle;
                return false;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance > Status.AggroRange)
            {
                _state = BrainState.Idle;
                return false;
            }

            return true;
        }

        /// <summary> 사거리 안에 들어왔을 때 너무 가까우면 후퇴, 아니면 시전 선딜로 진입한다 </summary>
        private void EnterCastBand(float distance)
        {
            if (distance < Status.RetreatRange)
            {
                Vector3 away = Self.position - Target.position;
                away.y = 0f;
                Vector3 retreatDirection = (away.sqrMagnitude > 0.0001f) ? away.normalized : Self.forward;

                _retreatDestination = Self.position + (retreatDirection * (Status.RetreatRange - distance + RETREAT_MARGIN));
                _state               = BrainState.Retreat;
                return;
            }

            _state      = BrainState.Telegraph;
            _stateTimer = 0f;
        }

        private void TickStagger(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.StaggerDuration)
            {
                _state = BrainState.Idle;
            }
        }

        private void TickKnockback(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.KnockbackDuration)
            {
                _state = BrainState.Idle;
            }
        }

        private static EnemyVisualState ToVisualState(BrainState state)
        {
            switch (state)
            {
                case BrainState.Idle:      return EnemyVisualState.Idle;
                case BrainState.Chase:     return EnemyVisualState.Move;
                case BrainState.Retreat:   return EnemyVisualState.Move;
                case BrainState.Return:    return EnemyVisualState.Move;
                case BrainState.Telegraph: return EnemyVisualState.Telegraph;
                case BrainState.Cast:      return EnemyVisualState.Attack;
                case BrainState.Cooldown:  return EnemyVisualState.Recovery;
                case BrainState.Stagger:   return EnemyVisualState.Stagger;
                case BrainState.Knockback: return EnemyVisualState.Knockback;
                default:                   return EnemyVisualState.Idle;
            }
        }
    }
}
