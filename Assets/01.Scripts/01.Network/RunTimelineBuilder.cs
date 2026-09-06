// System
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary> 결계 데이터에서 순수 C# 타임라인을 만든다. 시간 값은 여기서도 코드에 적지 않는다 </summary>
    public static class RunTimelineBuilder
    {
        /****************************************
        *                Methods
        ****************************************/

        public static RunPhaseTimeline Build(BarrierPhaseSO barrier)
        {
            if (barrier == null)
            {
                return new RunPhaseTimeline(null);
            }

            IReadOnlyList<BarrierPhaseSO.PhaseEntry> entries = barrier.Phases;
            if (entries == null)
            {
                return new RunPhaseTimeline(null);
            }

            // Boss는 필드 시계의 구간이 아니다. 보스전은 진입 시각과 무관한 독립 시계로 돌기 때문에
            // 여기에 넣으면 "몇 초에 보스가 시작한다"는 잘못된 계약이 다시 생긴다.
            // 결계 데이터에는 FieldLevel 조회용으로 남겨 두고 타임라인에서만 뺀다
            List<RunPhaseTimeline.Segment> segments = new List<RunPhaseTimeline.Segment>(entries.Count);
            for (int i = 0; i < entries.Count; ++i)
            {
                if (entries[i].Phase == RunPhase.Boss)
                {
                    continue;
                }

                segments.Add(new RunPhaseTimeline.Segment(entries[i].Phase, entries[i].StartSeconds, entries[i].EndSeconds));
            }

            return new RunPhaseTimeline(segments.ToArray());
        }
    }
}
