// System
using System;

namespace Geumeum.Common
{
    /// <summary> UI와 연출이 최종 구간 원혼 상태를 읽는 창구 </summary>
    public interface IBossSoulSource
    {
        BossSoulSnapshot SoulSnapshot { get; }
        event Action<BossSoulSnapshot> SoulChanged;
    }
}
