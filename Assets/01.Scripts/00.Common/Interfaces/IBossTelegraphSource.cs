// System
using System;
using System.Collections.Generic;

namespace Geumeum.Common
{
    /// <summary> 보스 표현이 AI 구현을 참조하지 않고 현재 예고와 변경을 받는 창구 </summary>
    public interface IBossTelegraphSource
    {
        /// <summary> 기존 단일 소비자용. 활성 집합의 첫 cue이며 없으면 default </summary>
        BossTelegraphCue TelegraphCue { get; }

        /// <summary> 재접속 시 현재 상태를 먼저 읽는 최대 8개의 서버 확정 예고 </summary>
        IReadOnlyList<BossTelegraphCue> TelegraphCues { get; }

        event Action<BossTelegraphCue> TelegraphChanged;
    }
}
