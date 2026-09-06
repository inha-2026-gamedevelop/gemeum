// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 페이즈별 판단 베이스. 서버에서만 Tick되며, 대상 스캔, 텔레그래프 RPC는 BossController가 담당한다.
    /// 1페이즈는 MeleeArc 패턴 1개까지 구현됐다(Phase1State), 나머지 세 템플릿과 2페이즈는
    /// 이 슬라이스가 L2로 검증된 뒤 채운다
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

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(BossController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);
    }
}
