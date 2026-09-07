// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 결계의 현재 상태를 읽는 계약 </summary>
    public interface IBarrierState
    {
        /// <summary> 결계가 켜져 있는지 </summary>
        bool IsActive { get; }

        /// <summary> 현재 중심. 단계마다 보스 지점 쪽으로 보간되므로 고정값이 아니다 </summary>
        Vector3 Center { get; }

        /// <summary> 현재 반경(m). 축소 중에는 보간 중인 값이다 </summary>
        float Radius { get; }

        /// <summary> 이 위치가 결계 밖인지. 높이는 보지 않고 XZ 평면 거리로만 판정한다 </summary>
        bool IsOutside(Vector3 worldPosition);

        /// <summary> 이 위치에 적용할 회복 효율. 1이면 정상, 쇠약이면 감소, 회복 봉쇄면 0 </summary>
        float GetHealingMultiplier(Vector3 worldPosition);
    }
}
