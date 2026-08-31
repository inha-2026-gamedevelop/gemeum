// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 창귀 행동 결정: 사거리 진입 시 짧은 선딜 후 직선 돌진, 명중하면 공격, 패링당하면 경직.
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
            Idle,       // 대상 없음 또는 인지 범위 밖
            Chase,      // 인지 범위 안, 돌진 커밋 거리 밖 — 추적 필요
            Telegraph,  // 돌진 커밋 거리 안, 선딜 진행 중 — 방향은 선딜 끝에 확정
            Charge,     // 고정된 방향으로 직선 돌진 중
            Attack,     // 돌진 중 명중, 이번 틱에 공격 판정 실행
            Recovery,   // 돌진 종료 후 재행동 대기
            Stagger,    // 패링당한 뒤 경직
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float ARRIVAL_EPSILON = 0.3f; // 돌진 목적지 도달 판정 여유

        private BrainState _state              = BrainState.Idle;
        private float      _stateTimer         = 0f;
        private Vector3    _chargeDestination;
        private float      _chargeMaxDuration; // 목표에 못 미쳐도 이 시간이 지나면 돌진을 끝낸다

        /****************************************
        *              Properties
        ****************************************/

        public BrainState State => _state;

        public override bool ShouldChase     => (_state == BrainState.Chase) || (_state == BrainState.Charge);
        public override bool ShouldAttackNow => _state == BrainState.Attack;

        public override Vector3? ChaseDestinationOverride => (_state == BrainState.Charge) ? _chargeDestination : (Vector3?)null;
        public override float?   MoveSpeedOverride         => (_state == BrainState.Charge) ? Status.ChargeSpeed : (float?)null;

        /// <summary> IParryable — 패링 판정 창 길이, 패링 시스템이 연결되면 사용된다 </summary>
        public float ParryWindowDuration => Status.ParryWindowDuration;

        /****************************************
        *              Constructor
        ****************************************/

        public ChanggwiBrain(Transform self, EnemyStatus status) : base(self, status) { }

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
                case BrainState.Telegraph:
                    TickTelegraph(deltaTime);
                    break;
                case BrainState.Charge:
                    TickCharge(deltaTime);
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
            }
        }

        /// <summary> 패링 성공 시 서버가 호출한다. 진행 중인 돌진을 취소하고 경직으로 전환한다 </summary>
        public void OnParried(uint parriedByNetId)
        {
            if (_state != BrainState.Charge)
            {
                return; // 이미 끝난 돌진에는 반응하지 않는다, 중복 경직 방지
            }

            _state      = BrainState.Stagger;
            _stateTimer = 0f;
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
                _state      = BrainState.Telegraph;
                _stateTimer = 0f;
            }
        }

        private void TickTelegraph(float deltaTime)
        {
            // 돌진 패턴은 읽기 쉬워야 한다, 선딜 중에는 대상이 사거리를 벗어나도 끝까지 진행한다
            _stateTimer += deltaTime;
            if (_stateTimer < Status.WindupDuration)
            {
                return;
            }

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
                _state = BrainState.Attack;
                return;
            }

            bool reachedDestination = Vector3.Distance(Self.position, _chargeDestination) <= ARRIVAL_EPSILON;
            if (reachedDestination || (_stateTimer >= _chargeMaxDuration))
            {
                _state      = BrainState.Recovery;
                _stateTimer = 0f;
            }
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
    }
}
