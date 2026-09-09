// System
using System;

namespace Geumeum.Common
{
    public enum BossEncounterStage
    {
        Fighting          = 0,
        Groggy            = 1,
        WakeWarning       = 2,
        FinalSoulWindow   = 3,
        FinalFailureAttack = 4,
        Killable          = 5,
        Defeated          = 6,
    }

    /// <summary> 후속 아트 바인딩이 읽는 현재 보스 단계와 마지막 전환 시각 </summary>
    [Serializable]
    public struct BossPhaseSnapshot
    {
        public int                PhaseIndex;
        public BossEncounterStage Stage;
        public uint               Revision;
        public double             ChangedServerTime;
        /// <summary>PhaseIndex가 마지막으로 바뀐 권위 게임 시간. stage 갱신에는 바뀌지 않는다.</summary>
        public double             PhaseChangedGameplayTime;
    }
}
