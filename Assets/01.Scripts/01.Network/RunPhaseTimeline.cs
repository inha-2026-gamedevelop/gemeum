// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 런 구간의 경계 계산과 데이터 불변식 검사.
    /// Mirror와 Unity에 의존하지 않아야 틱이 밀린 경우를 단위로 확인할 수 있다
    /// </summary>
    public sealed class RunPhaseTimeline
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 구간 하나의 시간 경계. 반경 · 피해량은 이 레이어가 알 필요가 없다 </summary>
        public readonly struct Segment
        {
            public RunPhase Phase        { get; }
            public float    StartSeconds { get; }
            public float    EndSeconds   { get; }

            public Segment(RunPhase phase, float startSeconds, float endSeconds)
            {
                Phase        = phase;
                StartSeconds = startSeconds;
                EndSeconds   = endSeconds;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float TIME_EPSILON = 0.001f; // 인스펙터 입력값의 부동소수 오차 허용치(초)

        // 런 데이터가 가져야 하는 구간과 순서. Lobby · Result는 런 데이터 밖의 세션 상태다
        private static readonly RunPhase[] G_RequiredOrder =
        {
            RunPhase.Farming,
            RunPhase.Shrink1,
            RunPhase.Shrink2,
            RunPhase.Shrink3,
            RunPhase.Boss,
        };

        private readonly Segment[] _segments;

        /****************************************
        *                Methods
        ****************************************/

        public RunPhaseTimeline(Segment[] segments)
        {
            _segments = segments ?? Array.Empty<Segment>();
        }

        /// <summary> 서버 시작 전 데이터 불변식을 검사한다, 실패하면 기본값으로 진행하지 않고 부트를 막는다 </summary>
        public bool Validate(float runTimeLimitSeconds, out string error)
        {
            if (_segments.Length != G_RequiredOrder.Length)
            {
                error = $"구간 수가 {G_RequiredOrder.Length}이 아니다: {_segments.Length}";
                return false;
            }

            for (int i = 0; i < _segments.Length; ++i)
            {
                if (_segments[i].Phase != G_RequiredOrder[i])
                {
                    error = $"{i}번 구간이 {G_RequiredOrder[i]}가 아니다: {_segments[i].Phase}";
                    return false;
                }

                if (_segments[i].StartSeconds >= _segments[i].EndSeconds)
                {
                    error = $"{_segments[i].Phase} 구간의 길이가 0 이하다: {_segments[i].StartSeconds} ~ {_segments[i].EndSeconds}";
                    return false;
                }
            }

            if (_segments[0].StartSeconds > TIME_EPSILON)
            {
                error = $"첫 구간이 0초에서 시작하지 않는다: {_segments[0].StartSeconds}";
                return false;
            }

            for (int i = 1; i < _segments.Length; ++i)
            {
                float gap = _segments[i].StartSeconds - _segments[i - 1].EndSeconds;
                if ((gap > TIME_EPSILON) || (gap < -TIME_EPSILON))
                {
                    error = $"{_segments[i - 1].Phase}와 {_segments[i].Phase} 사이가 이어지지 않는다: {_segments[i - 1].EndSeconds} → {_segments[i].StartSeconds}";
                    return false;
                }
            }

            float lastEndSeconds = _segments[_segments.Length - 1].EndSeconds;
            float limitGap       = lastEndSeconds - runTimeLimitSeconds;
            if ((limitGap > TIME_EPSILON) || (limitGap < -TIME_EPSILON))
            {
                error = $"마지막 구간 종료가 런 제한 시간과 다르다: {lastEndSeconds} ≠ {runTimeLimitSeconds}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary> 경과 시간이 속한 구간을 찾는다, 어느 구간에도 없으면 false </summary>
        public bool TryGetPhaseAt(float elapsedSeconds, out RunPhase phase)
        {
            for (int i = 0; i < _segments.Length; ++i)
            {
                if ((elapsedSeconds >= _segments[i].StartSeconds) && (elapsedSeconds < _segments[i].EndSeconds))
                {
                    phase = _segments[i].Phase;
                    return true;
                }
            }

            phase = default;
            return false;
        }

        /// <summary>
        /// previousElapsedSeconds 이후 currentElapsedSeconds까지 새로 진입한 구간을 순서대로 담는다.
        /// 틱이 밀려 경계를 여러 개 지나가도 건너뛰지 않고 각각 한 번씩 나온다
        /// </summary>
        public int CollectEnteredPhases(float previousElapsedSeconds, float currentElapsedSeconds, List<RunPhase> buffer)
        {
            if (buffer == null)
            {
                return 0;
            }

            buffer.Clear();

            for (int i = 0; i < _segments.Length; ++i)
            {
                float startSeconds = _segments[i].StartSeconds;
                if ((startSeconds > previousElapsedSeconds) && (startSeconds <= currentElapsedSeconds))
                {
                    buffer.Add(_segments[i].Phase);
                }
            }

            return buffer.Count;
        }
    }
}
