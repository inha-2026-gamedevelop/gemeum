// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈, 시험하는 판. 패턴 선택은 아직 없다, 나머지 세 템플릿과 함께 후속 슬라이스에서 채운다
    /// </summary>
    public sealed class Phase2State : BossPhaseStateBase
    {
        public Phase2State(Transform self, BossStatus status, BossController controller) : base(self, status)
        {
        }

        public override void Tick(float deltaTime)
        {
            // TODO: 패턴 선택, 발동
        }
    }
}
