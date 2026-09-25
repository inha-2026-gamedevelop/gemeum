// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 스케줄 요청 결과. 뷰는 로그와 테스트로만 읽는다 </summary>
    public enum ParryFlashUpsertResult
    {
        Scheduled        = 0, // 시작 시각을 기다린다. 시작하면 섬광 + 소리
        RemainingOnly    = 1, // 이미 열린 구간에 늦게 왔다. 남은 구간만 강조, 시작 섬광, 소리 없음
        Updated          = 2, // 아직 시작 전인 같은 키의 시각을 새 개정으로 바꿨다
        IgnoredDuplicate = 3, // 이미 시작한 같은 키. 시작 섬광을 다시 재생하지 않는다
        IgnoredClosed    = 4, // 이미 닫힌 구간. 표시하지 않는다
        IgnoredStale     = 5, // 더 오래된 개정
        IgnoredFull      = 6, // 용량 초과
        IgnoredInvalid   = 7, // 시각이 유한하지 않거나 끝이 시작보다 앞
    }

    /// <summary> 스케줄 항목 하나. [Start, End) 동안 강조하고, 제때 시작했으면 Start에 섬광을 한 번 재생한다 </summary>
    public struct ParryFlashEntry
    {
        public ulong          Key;
        public uint           Revision;
        public ParryFlashKind Kind;
        public bool           IsTarget;
        public double         Start;
        public double         End;

        /// <summary> 구간에 들어갔다 </summary>
        public bool Begun;

        /// <summary> 늦게 받아 시작 섬광 없이 남은 구간만 강조한다 </summary>
        public bool RemainingOnly;

        /// <summary> 직전 Advance에서 막 시작했다. 소리처럼 한 번만 낼 것을 이때 낸다 </summary>
        public bool JustBegan;
    }

    /// <summary>
    /// 타이밍 섬광 스케줄(COMBAT_STYLE_DESIGN S4의 표 아래 규칙). 순수 로직이라 시각을 인자로 받는다
    /// - 늦게 받은 구간이 아직 열려 있으면 남은 구간만 강조하고 지난 시작 섬광은 재생하지 않는다
    /// - 닫힌 구간은 표시하지 않는다
    /// - 같은 키(취소, 개정, 반복)는 한 번만 재생한다
    /// 고정 용량 배열이라 실행 중 할당이 없다
    /// </summary>
    public sealed class ParryFlashScheduler
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ParryFlashEntry[] _entries;
        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        public ParryFlashEntry this[int index] => _entries[index];

        /****************************************
        *              Constructors
        ****************************************/

        public ParryFlashScheduler(int capacity)
        {
            _entries = new ParryFlashEntry[Math.Max(1, capacity)];
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 구간을 추가하거나 같은 키를 개정한다. revision 0은 개정 비교를 하지 않는다 </summary>
        public ParryFlashUpsertResult Upsert(ulong key, uint revision, ParryFlashKind kind, bool isTarget,
            double start, double end, double now)
        {
            if ((!IsFinite(start)) || (!IsFinite(end)) || (!IsFinite(now)) || (end <= start))
            {
                return ParryFlashUpsertResult.IgnoredInvalid;
            }

            int index = IndexOf(key);
            if (index >= 0)
            {
                return UpdateExisting(index, revision, isTarget, start, end, now);
            }

            if (now >= end)
            {
                return ParryFlashUpsertResult.IgnoredClosed;
            }

            if (_count >= _entries.Length)
            {
                return ParryFlashUpsertResult.IgnoredFull;
            }

            bool late = (now > start);
            _entries[_count] = new ParryFlashEntry
            {
                Key           = key,
                Revision      = revision,
                Kind          = kind,
                IsTarget      = isTarget,
                Start         = start,
                End           = end,
                Begun         = late,
                RemainingOnly = late,
                JustBegan     = false,
            };
            ++_count;

            return late ? ParryFlashUpsertResult.RemainingOnly : ParryFlashUpsertResult.Scheduled;
        }

        /// <summary> 취소된 구간을 뺀다. 진행 중이던 강조도 여기서 끝난다 </summary>
        public bool Remove(ulong key)
        {
            int index = IndexOf(key);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public bool Contains(ulong key)
        {
            return IndexOf(key) >= 0;
        }

        public bool TryGet(ulong key, out ParryFlashEntry entry)
        {
            int index = IndexOf(key);
            entry = (index >= 0) ? _entries[index] : default;
            return index >= 0;
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _count);
            _count = 0;
        }

        /// <summary>
        /// 시각을 진행한다. 끝난 구간은 빼고, 시작 시각을 지난 구간은 Begun + JustBegan으로 표시한다
        /// 한 프레임 사이에 시작과 끝을 모두 지나친 구간은 닫힌 창이므로 시작시키지 않고 뺀다
        /// </summary>
        public void Advance(double now)
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                ref ParryFlashEntry entry = ref _entries[i];
                entry.JustBegan = false;

                if (now >= entry.End)
                {
                    RemoveAt(i);
                    continue;
                }

                if ((!entry.Begun) && (now >= entry.Start))
                {
                    entry.Begun     = true;
                    entry.JustBegan = true;
                }
            }
        }

        /// <summary> 지금 그릴 항목. 대상 > 비대상, 같으면 늦게 시작한 쪽. 없으면 -1 </summary>
        public int FindDominantBegun()
        {
            int best = -1;
            for (int i = 0; i < _count; ++i)
            {
                ref ParryFlashEntry entry = ref _entries[i];
                if (!entry.Begun)
                {
                    continue;
                }

                if (best < 0)
                {
                    best = i;
                    continue;
                }

                ref ParryFlashEntry current = ref _entries[best];
                if ((entry.IsTarget && !current.IsTarget) ||
                    ((entry.IsTarget == current.IsTarget) && (entry.Start > current.Start)))
                {
                    best = i;
                }
            }

            return best;
        }

        /****************************************
        *                Methods
        ****************************************/

        private ParryFlashUpsertResult UpdateExisting(int index, uint revision, bool isTarget,
            double start, double end, double now)
        {
            ref ParryFlashEntry entry = ref _entries[index];

            if ((revision != 0u) && (entry.Revision != 0u) && (revision < entry.Revision))
            {
                return ParryFlashUpsertResult.IgnoredStale;
            }

            entry.Revision = revision;
            entry.IsTarget = entry.IsTarget || isTarget;

            if (entry.Begun)
            {
                // 이미 시작했다. 엇박 지연으로 끝이 밀렸으면 끝만 따라가고 시작 섬광은 다시 내지 않는다
                entry.End = end;
                if (now >= entry.End)
                {
                    RemoveAt(index);
                }

                return ParryFlashUpsertResult.IgnoredDuplicate;
            }

            entry.Start = start;
            entry.End   = end;

            if (now >= end)
            {
                RemoveAt(index);
                return ParryFlashUpsertResult.IgnoredClosed;
            }

            if (now > start)
            {
                entry.Begun         = true;
                entry.RemainingOnly = true;
                return ParryFlashUpsertResult.RemainingOnly;
            }

            return ParryFlashUpsertResult.Updated;
        }

        private int IndexOf(ulong key)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].Key == key)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveAt(int index)
        {
            --_count;
            if (index != _count)
            {
                _entries[index] = _entries[_count];
            }

            _entries[_count] = default;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
