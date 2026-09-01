// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 허깨비 행동 결정: 근접 추적, 긴 선딜. EnemyController가 State를 읽어 실제 이동 · 공격을 실행한다
    /// </summary>
    public sealed class HeokkaebiBrain : EnemyBrainBase
    {
        /****************************************
        *                Types
        ****************************************/

        public enum BrainState
        {
            Idle,     // 대상 없음 또는 인지 범위 밖
            Chase,    // 인지 범위 안, 공격 사거리 밖 — 추적 필요
            Windup,   // 공격 사거리 안, 선딜 진행 중
            Attack,    // 선딜 끝, 이번 틱에 공격 판정 실행
            Cooldown,  // 공격 직후 재공격 대기
            Stagger,   // 비공격 상태에서 일반 피격을 당해 경직
            Knockback, // 피격 누적 문턱 도달 또는 패링당해 넉백
        }

        /****************************************
        *                Fields
        ****************************************/

        private BrainState _state = BrainState.Idle;
        private float       _stateTimer = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public BrainState State => _state;

        public override bool ShouldChase     => _state == BrainState.Chase;
        public override bool ShouldAttackNow => _state == BrainState.Attack;

        public override bool IsAttackCommitted => (_state == BrainState.Windup) || (_state == BrainState.Attack);

        public override EnemyVisualState VisualState => ToVisualState(_state);

        /****************************************
        *              Constructor
        ****************************************/

        public HeokkaebiBrain(Transform self, EnemyStatus status) : base(self, status) { }

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
                case BrainState.Windup:
                    TickWindup(deltaTime);
                    break;
                case BrainState.Attack:
                    TickAttack();
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
            if (Target == null)
            {
                _state = BrainState.Idle;
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance > Status.AggroRange)
            {
                _state = BrainState.Idle;
                return;
            }

            if (distance <= Status.AttackRange)
            {
                _state       = BrainState.Windup;
                _stateTimer  = 0f;
            }
        }

        private void TickWindup(float deltaTime)
        {
            // 선딜 시작 후에는 대상이 사거리를 벗어나도 끝까지 진행한다, 선딜이 읽기 쉬워야 하는 요구와 맞물린다
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.WindupDuration)
            {
                _state = BrainState.Attack;
            }
        }

        private void TickAttack()
        {
            // 실제 공격 판정 실행은 EnemyController가 State == Attack을 보고 이번 틱에 수행한다
            _state      = BrainState.Cooldown;
            _stateTimer = 0f;
        }

        private void TickCooldown(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= Status.AttackCooldown)
            {
                _state = BrainState.Idle;
            }
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
                case BrainState.Windup:    return EnemyVisualState.Telegraph;
                case BrainState.Attack:    return EnemyVisualState.Attack;
                case BrainState.Cooldown:  return EnemyVisualState.Recovery;
                case BrainState.Stagger:   return EnemyVisualState.Stagger;
                case BrainState.Knockback: return EnemyVisualState.Knockback;
                default:                   return EnemyVisualState.Idle;
            }
        }
    }
}
