// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 페이즈별 판단 베이스. 서버에서만 Tick되며, 실제 패턴 실행은 BossController가 담당한다.
    /// 패턴 선택 로직은 아직 없다 — BossPatternSO 확정 후 4단계에서 채운다 (docs/unity/BOSS_DESIGN.md 15장)
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
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(BossController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);
    }
}
