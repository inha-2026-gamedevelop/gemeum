// System
using System;

namespace Geumeum.Common
{
    /// <summary> 아트가 AI 클래스를 참조하지 않고 페이즈 전환을 읽는 창구 </summary>
    public interface IBossPhaseSource
    {
        BossPhaseSnapshot PhaseSnapshot { get; }
        event Action<BossPhaseSnapshot> PhaseChanged;
    }
}
