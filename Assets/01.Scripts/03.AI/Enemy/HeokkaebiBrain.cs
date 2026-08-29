// Unity
using UnityEngine;

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
            Attack,   // 선딜 끝, 이번 틱에 공격 판정 실행
            Cooldown, // 공격 직후 재공격 대기
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
            }
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
    }
}
