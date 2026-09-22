using System;

namespace Geumeum.Common
{
    /// <summary> 서버가 확정한 일반 피격. 일시정지 가능한 런 시계를 사용한다 </summary>
    [Serializable]
    public struct PlayerHitReactionCue
    {
        public uint Generation;
        public double StartGameplayTime;
        public double EndGameplayTime;

        public bool IsActiveAt(double gameplayTime)
        {
            return Generation != 0u && gameplayTime >= StartGameplayTime &&
                gameplayTime < EndGameplayTime;
        }
    }
}
