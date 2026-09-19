// System
using System;

namespace Geumeum.Common
{
    /// <summary> 피격 표현이 구독하는 체력 감소 알림. 서버가 확정해 동기화한 값이 줄었을 때 각 화면에서 발생한다 </summary>
    public interface IDamagePresentationSource
    {
        event Action HealthDecreased;
    }
}
