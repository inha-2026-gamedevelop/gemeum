// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 락온과 조준 보정이 겨누는 지점 </summary>
    public interface IAimAssistTarget
    {
        /// <summary> 겨눌 월드 좌표 </summary>
        Vector3 AimPoint { get; }
    }
}
