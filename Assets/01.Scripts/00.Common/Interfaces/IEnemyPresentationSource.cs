// System
using System;

namespace Geumeum.Common
{
    /// <summary> 몬스터 표현이 읽는 현재 상태와 변경 알림. 서버 판정은 노출하지 않는다 </summary>
    public interface IEnemyPresentationSource
    {
        EnemyVisualState VisualState { get; }
        int AttackVariant { get; }
        bool IsParryWindowVisuallyOpen { get; }

        event Action<EnemyVisualState> VisualStateChanged;
        event Action<int> AttackVariantChanged;
        event Action<bool> ParryWindowVisuallyOpenChanged;
    }
}
