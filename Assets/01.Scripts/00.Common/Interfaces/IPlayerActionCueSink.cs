// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>권위 행동 cue를 로컬 VFX와 음향으로 표현하는 client 전용 창구.</summary>
    public interface IPlayerActionCueSink
    {
        void PlayActionCue(in PlayerActionCue cue, float normalizedProgress);
        void CancelActionCue(PlayerActionCueKind kind, uint executionId);
        void PlayActionImpact(PlayerActionCueKind kind, uint executionId,
            bool successful, bool critical, double impactGameplayTime);
        void PlayMarkCue(uint executionId, Vector3 worldPosition,
            double appliedGameplayTime);
    }
}
