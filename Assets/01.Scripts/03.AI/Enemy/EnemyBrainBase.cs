// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 행동 결정 베이스. 서버에서만 Tick되며, 실제 이동 · 공격 실행은 EnemyController가 담당한다
    /// </summary>
    public abstract class EnemyBrainBase
    {
        /****************************************
        *                Fields
        ****************************************/

        protected readonly Transform   Self;
        protected readonly EnemyStatus Status;

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

        /// <summary> 선딜 · 공격 판정 중이면 true. true인 동안은 일반 피격으로 경직하지 않는다 </summary>
        public virtual bool IsAttackCommitted => false;

        /// <summary> 현재 표현 상태, EnemyController가 매 틱 읽어 클라이언트에 동기화한다 </summary>
        public abstract EnemyVisualState VisualState { get; }

        /// <summary> 여러 공격 모션 중 이번 공격에 쓸 인덱스, 몬스터별로 다른 공격이 없으면 항상 0. 애니메이션 표현 전용, 판정에는 영향 없다 </summary>
        public virtual int AttackVariant => 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(EnemyController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);

        /// <summary> 비공격 상태에서 일반 피격을 당했을 때 EnemyController가 호출한다 </summary>
        public virtual void EnterStagger() { }

        /// <summary> 피격 누적이 문턱에 도달했거나 패링당했을 때 EnemyController · 구현체가 호출한다 </summary>
        public virtual void EnterKnockback() { }

        /// <summary> 인지 범위 밖에서도 공격당하면 EnemyController가 호출한다, Target을 먼저 채운 뒤 호출해야 한다.
        /// Idle 상태일 때만 Chase로 강제 전환한다, 이미 다른 행동 중이면 끼어들지 않는다 </summary>
        public virtual void ForceAggro() { }
    }
}
