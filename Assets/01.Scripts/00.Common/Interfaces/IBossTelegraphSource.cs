// System
using System;

namespace Geumeum.Common
{
    /// <summary> 보스 표현이 AI 구현을 참조하지 않고 현재 예고와 변경을 받는 창구 </summary>
    public interface IBossTelegraphSource
    {
        BossTelegraphCue TelegraphCue { get; }
        event Action<BossTelegraphCue> TelegraphChanged;
    }
}
