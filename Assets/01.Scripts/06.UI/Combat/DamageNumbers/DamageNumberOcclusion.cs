// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 데미지 숫자 가림 검사의 예산과 짧은 재사용(COMBAT_STYLE_DESIGN S10 시야 밖·성능)
    /// 지형에 가린 적중점에는 새 숫자를 만들지 않는다. 검사는 새 숫자를 만들 때만, 프레임당 정해진 횟수까지 한다
    /// 같은 대상(부위 포함)의 결과는 짧게 다시 쓴다. 물리 쿼리는 숫자 층이 하고 여기는 순수 장부다
    /// 고정 크기 칸만 쓰고 할당하지 않는다
    /// </summary>
    public sealed class DamageNumberOcclusion
    {
        /****************************************
        *                Types
        ****************************************/

        private struct Entry
        {
            public bool  IsUsed;
            public uint  TargetNetId;
            public sbyte PartIndex;
            public float CheckedTime;
            public bool  IsOccluded;
        }

        /****************************************
        *              Constants
        ****************************************/

        // 끝점을 당긴 뒤 이보다 짧은 선분은 검사하지 않는다(가리는 것이 없다고 본다)
        private const float MIN_SEGMENT_LENGTH = 0.0001f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Entry[] _entries;
        private int _budgetFrame = -1;
        private int _budgetUsed  = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity => _entries.Length;

        /****************************************
        *              Constructor
        ****************************************/

        public DamageNumberOcclusion(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "가림 결과 칸은 1개 이상이어야 한다");
            }

            _entries = new Entry[capacity];
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 같은 대상(부위 포함)을 reuseSeconds 안에 검사했으면 그 결과를 준다 </summary>
        public bool TryGet(uint targetNetId, sbyte partIndex, float now, float reuseSeconds, out bool isOccluded)
        {
            isOccluded = false;

            int index = Find(targetNetId, partIndex);
            if (index < 0)
            {
                return false;
            }

            float age = now - _entries[index].CheckedTime;
            if ((age < 0f) || (age > reuseSeconds))
            {
                return false;
            }

            isOccluded = _entries[index].IsOccluded;
            return true;
        }

        /// <summary>
        /// 이번 프레임에 검사를 한 번 더 해도 되는지. 되면 한 칸을 쓴다
        /// checksPerFrame이 0 이하면 검사하지 않는다(항상 false)
        /// </summary>
        public bool TryConsumeBudget(int frame, int checksPerFrame)
        {
            if (checksPerFrame <= 0)
            {
                return false;
            }

            if (frame != _budgetFrame)
            {
                _budgetFrame = frame;
                _budgetUsed  = 0;
            }

            if (_budgetUsed >= checksPerFrame)
            {
                return false;
            }

            ++_budgetUsed;
            return true;
        }

        /// <summary> 검사 결과를 기억한다. 칸이 차면 가장 오래전에 검사한 칸을 덮는다 </summary>
        public void Store(uint targetNetId, sbyte partIndex, float now, bool isOccluded)
        {
            int index = Find(targetNetId, partIndex);

            if (index < 0)
            {
                index = FindReusable();
            }

            _entries[index] = new Entry
            {
                IsUsed      = true,
                TargetNetId = targetNetId,
                PartIndex   = partIndex,
                CheckedTime = now,
                IsOccluded  = isOccluded,
            };
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _budgetFrame = -1;
            _budgetUsed  = 0;
        }

        /// <summary>
        /// 카메라에서 적중점으로 긋는 선분의 끝점. 적중점은 대상 표면이나 중심이라 발밑 지형에 닿기 쉬워
        /// 카메라 쪽으로 inset만큼 당긴다. 선분이 남지 않으면 false(가림 없음으로 본다)
        /// </summary>
        public static bool TryResolveSegmentEnd(Vector3 from, Vector3 to, float inset, out Vector3 end)
        {
            Vector3 delta  = to - from;
            float distance = delta.magnitude;
            float length   = distance - Mathf.Max(0f, inset);

            if (length <= MIN_SEGMENT_LENGTH)
            {
                end = from;
                return false;
            }

            end = from + (delta * (length / distance));
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private int Find(uint targetNetId, sbyte partIndex)
        {
            for (int i = 0; i < _entries.Length; ++i)
            {
                if (_entries[i].IsUsed &&
                    (_entries[i].TargetNetId == targetNetId) &&
                    (_entries[i].PartIndex == partIndex))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindReusable()
        {
            int   oldest     = 0;
            float oldestTime = float.MaxValue;

            for (int i = 0; i < _entries.Length; ++i)
            {
                if (!_entries[i].IsUsed)
                {
                    return i;
                }

                if (_entries[i].CheckedTime < oldestTime)
                {
                    oldest     = i;
                    oldestTime = _entries[i].CheckedTime;
                }
            }

            return oldest;
        }
    }
}
