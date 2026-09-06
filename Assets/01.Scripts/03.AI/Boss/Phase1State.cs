// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈 — 가르치는 판 (docs/unity/BOSS_PATTERN_CONTRACT.md 3장).
    /// 패턴 선택은 아직 없다 — BossPatternSO 확정 후 4단계에서 채운다
    /// </summary>
    public sealed class Phase1State : BossPhaseStateBase
    {
        public Phase1State(Transform self, BossStatus status) : base(self, status)
        {
        }

        public override void Tick(float deltaTime)
        {
            // TODO: 패턴 선택 · 발동 (BOSS_DESIGN.md 15장 4단계)
        }
    }
}
