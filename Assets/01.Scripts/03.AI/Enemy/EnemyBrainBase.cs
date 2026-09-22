// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 행동 결정 베이스. 서버에서만 Tick되며, 실제 이동, 공격 실행은 EnemyController가 담당한다
    /// </summary>
    public abstract class EnemyBrainBase
    {
        /****************************************
        *                Fields
        ****************************************/

        protected readonly Transform   Self;
        protected readonly EnemyStatus Status;

        private bool    _isHerding        = false;
        private uint    _herdingRevision  = 0u;
        private Vector3 _herdingDestination;

        /****************************************
        *              Constructor
        ****************************************/

        protected EnemyBrainBase(Transform self, EnemyStatus status)
        {
            Self   = self;
            Status = status;
        }

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 추적할 대상, EnemyController가 주기적으로 채워준다 </summary>
        public Transform Target { get; set; }

        /// <summary> 이번 틱에 Target을 향해 이동해야 하면 true. EnemyController가 이 값만 보고 이동을 실행한다 </summary>
        public abstract bool ShouldChase { get; }

        /// <summary> 이번 틱에 공격 판정을 실행해야 하면 true. 한 번의 Tick 결과에서만 유지되는 신호다 </summary>
        public abstract bool ShouldAttackNow { get; }

        /// <summary> 이동 목적지를 Target 위치 대신 강제한다, null이면 Target을 따라간다. 돌진처럼 고정 방향으로 이동해야 하는 패턴에 쓴다 </summary>
        public virtual Vector3? ChaseDestinationOverride => null;

        /// <summary> 이번 틱의 이동 속도를 강제한다, null이면 Agent 기본 속도를 쓴다 </summary>
        public virtual float? MoveSpeedOverride => null;

        /// <summary> true면 ShouldAttackNow 실행 시 근접 피해 대신 투사체를 발사한다 </summary>
        public virtual bool IsRangedAttack => false;

        /// <summary>
        /// 타격이 확정되는 순간 대상이 이 거리 안에 있어야 근접 공격이 들어간다
        /// 선딜 동안 대상이 굴러 빠져나가면 빗나가야 하므로, 공격을 시작할 때가 아니라
        /// EnemyController가 피해를 주기 직전에 다시 잰다
        /// 기본은 시전 사거리이고, 돌진처럼 멈춰 서는 자리가 따로 있는 몬스터는 덮어쓴다
        /// </summary>
        public virtual float MeleeHitRange => Status.AttackRange;

        /// <summary> 선딜, 공격 판정 중이면 true. true인 동안은 일반 피격으로 경직하지 않는다 </summary>
        public virtual bool IsAttackCommitted => false;

        /// <summary> 현재 표현 상태, EnemyController가 매 틱 읽어 클라이언트에 동기화한다 </summary>
        public abstract EnemyVisualState VisualState { get; }

        /// <summary> 여러 공격 모션 중 이번 공격에 쓸 인덱스, 몬스터별로 다른 공격이 없으면 항상 0. 애니메이션 표현 전용, 판정에는 영향 없다 </summary>
        public virtual int AttackVariant => 0;

        /// <summary>
        /// 지금 이 순간이 대상의 실제 패링 판정 구간(windowStart~windowEnd) 안이면 true, 표현 전용
        /// 공격 모션 시작과 실제 패링 판정 구간 사이에 시간차가 있는 몬스터가 이 시그널로 플레이어에게
        /// "지금이 진짜 패링 타이밍"을 알려준다 - 패링 창을 열지 않는 몬스터는 항상 false
        /// </summary>
        public virtual bool IsParryWindowVisuallyOpen => false;

        /// <summary> 목표 결계 안으로 이동 중인지 </summary>
        public bool IsHerding => _isHerding;

        /// <summary> 현재 몰이 목표 revision </summary>
        public uint HerdingRevision => _herdingRevision;

        /// <summary> NavMeshAgent가 향할 목표 원 내부 지점 </summary>
        public Vector3 HerdingDestination => _herdingDestination;

        /// <summary> 공격, 경직, 넉백을 방해하지 않고 몰이 이동을 적용할 수 있는 상태인지 </summary>
        public virtual bool AllowsHerdingMovement => false;

        /// <summary> 현재 중단 가능한 공격 실행 ID. 창이 없으면 0 </summary>
        public virtual uint ActiveAttackExecutionId => 0u;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(EnemyController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);

        /// <summary> 더 새 revision의 목표 원 이동을 시작한다 </summary>
        public bool BeginHerding(Vector3 destination, uint revision)
        {
            if ((revision == 0u) || (revision <= _herdingRevision))
            {
                return false;
            }

            _herdingRevision    = revision;
            _herdingDestination = destination;
            _isHerding          = true;
            return true;
        }

        /// <summary> 목표 원 안에 도착했을 때 이동을 끝낸다 </summary>
        public void CompleteHerding(uint revision)
        {
            if (!_isHerding || (revision != _herdingRevision))
            {
                return;
            }

            _isHerding = false;
        }

        /// <summary> 캡처한 실행 ID가 여전히 현재 공격이면 정밀 명중으로 취소한다 </summary>
        public virtual bool TryInterruptPrecision(uint expectedExecutionId)
        {
            return false;
        }

        /// <summary> 비공격 상태에서 일반 피격을 당했을 때 EnemyController가 호출한다 </summary>
        public virtual void EnterStagger() { }

        public virtual bool TryEnterSkillStagger(float duration) => false;

        /// <summary> 피격 누적이 문턱에 도달했거나 패링당했을 때 EnemyController, 구현체가 호출한다 </summary>
        public virtual void EnterKnockback() { }

        /// <summary> 인지 범위 밖에서도 공격당하면 EnemyController가 호출한다, Target을 먼저 채운 뒤 호출해야 한다
        /// Idle 상태일 때만 Chase로 강제 전환한다, 이미 다른 행동 중이면 끼어들지 않는다 </summary>
        public virtual void ForceAggro() { }
    }
}
