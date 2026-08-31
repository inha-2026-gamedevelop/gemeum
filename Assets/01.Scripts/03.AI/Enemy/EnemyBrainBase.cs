// Unity
using UnityEngine;

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

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(EnemyController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);
    }
}
