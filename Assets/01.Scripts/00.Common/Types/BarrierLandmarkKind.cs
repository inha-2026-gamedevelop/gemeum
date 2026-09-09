// System
using System;

namespace Geumeum.Common
{
    /// <summary> 다음 결계 원 안에 남아야 하는 필드 랜드마크 종류 </summary>
    [Flags]
    public enum BarrierLandmarkKind
    {
        None         = 0,
        Start        = 1 << 0,
        Shop         = 1 << 1,
        BossEntrance = 1 << 2,
    }
}
