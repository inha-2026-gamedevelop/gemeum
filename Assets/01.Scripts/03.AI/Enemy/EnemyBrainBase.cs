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

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(EnemyController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);
    }
}
