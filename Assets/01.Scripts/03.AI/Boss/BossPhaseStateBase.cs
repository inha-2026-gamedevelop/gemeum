// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 페이즈별 판단 베이스. 서버에서만 Tick되며, 대상 스캔, 텔레그래프 RPC는 BossController가 담당한다.
    /// </summary>
    public abstract class BossPhaseStateBase
    {
        /****************************************
        *                Fields
        ****************************************/

        protected readonly Transform  Self;
        protected readonly BossStatus Status;

        /****************************************
        *              Constructor
        ****************************************/

        protected BossPhaseStateBase(Transform self, BossStatus status)
        {
            Self   = self;
            Status = status;
        }

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공격 후보 대상, BossController가 주기적으로 채워준다 </summary>
        public Transform Target { get; set; }

        protected bool IsExited { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(BossController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);

        /// <summary> 진행 중인 판정, 패링 창, 예고를 정리하고 회복 상태로 돌아간다 </summary>
        public abstract void CancelActiveAttack();

        /// <summary> 페이즈를 종료한다. 종료된 상태는 다시 실행하지 않는다 </summary>
        public void Exit()
        {
            if (IsExited)
            {
                return;
            }

            IsExited = true;
            CancelActiveAttack();
            Target = null;
        }
    }
}
