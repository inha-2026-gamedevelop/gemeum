// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈 — 시험하는 판 (docs/unity/BOSS_PATTERN_CONTRACT.md 4장).
    /// 패턴 선택은 아직 없다 — BossPatternSO 확정 후 4단계에서 채운다
    /// </summary>
    public sealed class Phase2State : BossPhaseStateBase
    {
        public Phase2State(Transform self, BossStatus status) : base(self, status)
        {
        }

        public override void Tick(float deltaTime)
        {
            // TODO: 패턴 선택 · 발동 (BOSS_DESIGN.md 15장 4단계)
        }
    }
}
