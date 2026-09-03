// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 결계의 현재 상태를 읽는 계약. BarrierController(시스템 파트)가 구현하고 다른 파트는 읽기만 한다.
    /// 단계 전환은 RunPhaseChangedEvent가 알리고, 이 인터페이스는 이벤트로 나를 수 없는 연속값(중심, 반경)과 판정을 맡는다.
    ///
    /// 판정을 여기 하나로 모으는 이유는 파트마다 XZ 거리 규칙을 다시 구현하면
    /// "안에 있는데 맞았다"가 생기기 때문이다. 화면에 그리는 원과 서버가 때리는 원이 갈라져서는 안 된다
    /// </summary>
    public interface IBarrierState
    {
        /// <summary> 결계가 켜져 있는지. Lobby, Boss, Result 단계에서는 false이고 이때 Center와 Radius는 의미가 없다 </summary>
        bool IsActive { get; }

        /// <summary> 현재 중심. 단계마다 보스 지점 쪽으로 보간되므로 고정값이 아니다 </summary>
        Vector3 Center { get; }

        /// <summary> 현재 반경(m). 축소 중에는 보간 중인 값이다 </summary>
        float Radius { get; }

        /// <summary> 이 위치가 결계 밖인지. 높이는 보지 않고 XZ 평면 거리로만 판정한다 </summary>
        bool IsOutside(Vector3 worldPosition);

        /// <summary>
        /// 이 위치에 적용할 회복 효율. 1이면 정상, 쇠약이면 감소, 회복 봉쇄면 0.
        /// 결계 밖에서만 불이익이 걸리므로 안쪽은 항상 1이다
        /// </summary>
        float GetHealingMultiplier(Vector3 worldPosition);
    }
}
