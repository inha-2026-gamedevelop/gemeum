// System
using System;

namespace Geumeum.Common
{
    /// <summary> UI와 아트가 AI 구현을 참조하지 않고 보스 부위 상태를 읽는 창구 </summary>
    public interface IBossPartSource
    {
        BossPartSnapshot PartSnapshot { get; }
        event Action<BossPartSnapshot> PartChanged;
    }
}
