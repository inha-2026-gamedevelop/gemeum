// System
using System;

namespace Geumeum.UI
{
    /// <summary> 체력 바 칸을 요청한 결과 </summary>
    public enum EnemyHealthBarAction : byte
    {
        Reused   = 0, // 이미 이 대상의 바가 떠 있다. 순서만 최신으로 올렸다
        Spawned  = 1, // 빈 칸에 새 바
        Replaced = 2, // 상한이라 가장 오래 맞지 않은 바의 칸을 넘겨받았다
    }

    /// <summary>
    /// 화면에 떠 있는 잡귀 체력 바의 순수 칸 장부. 고정 크기 배열만 쓰고 할당하지 않는다
    /// 대상은 표시 층이 정한 정수 키로 구분한다. 상한이 차면 가장 오래 전에 맞은(갱신되지 않은) 바를 교체한다
    /// </summary>
    public sealed class EnemyHealthBarBook
    {
        /****************************************
        *                Types
        ****************************************/

        public struct Slot
        {
            public bool IsActive;
            public int  TargetId;

            /// <summary> 마지막으로 맞은 순서. 작을수록 오래됐다 </summary>
            public long Order;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Slot[] _slots;
        private int  _activeCount = 0;
        private long _nextOrder   = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity    => _slots.Length;
        public int ActiveCount => _activeCount;

        /****************************************
        *              Constructor
        ****************************************/

        public EnemyHealthBarBook(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "체력 바 칸은 1개 이상이어야 한다");
            }

            _slots = new Slot[capacity];
        }

        /****************************************
        *              Public API
        ****************************************/

        public ref readonly Slot Get(int index)
        {
            return ref _slots[index];
        }

        /// <summary> 대상의 바가 떠 있는 칸. 없으면 -1 </summary>
        public int Find(int targetId)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsActive && (_slots[i].TargetId == targetId))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 대상이 맞았을 때 칸을 받는다. 이미 있으면 그 칸, 없으면 빈 칸, 꽉 찼으면 가장 오래 맞지 않은 칸
        /// Replaced면 그 칸의 이전 대상은 호출자가 떼어 낸다
        /// </summary>
        public EnemyHealthBarAction Acquire(int targetId, out int slotIndex)
        {
            slotIndex = Find(targetId);

            if (slotIndex >= 0)
            {
                Touch(slotIndex);
                return EnemyHealthBarAction.Reused;
            }

            slotIndex = FindFree();

            if (slotIndex >= 0)
            {
                Write(slotIndex, targetId);
                ++_activeCount;
                return EnemyHealthBarAction.Spawned;
            }

            slotIndex = FindOldest();
            Write(slotIndex, targetId);
            return EnemyHealthBarAction.Replaced;
        }

        /// <summary> 다시 맞은 바를 가장 최근 순서로 올린다 </summary>
        public void Touch(int index)
        {
            if (_slots[index].IsActive)
            {
                _slots[index].Order = _nextOrder++;
            }
        }

        /// <summary> 표시가 끝났거나 대상이 사라진 칸을 비운다 </summary>
        public void Release(int index)
        {
            if (!_slots[index].IsActive)
            {
                return;
            }

            _slots[index] = default;
            --_activeCount;
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _activeCount = 0;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Write(int index, int targetId)
        {
            _slots[index] = new Slot
            {
                IsActive = true,
                TargetId = targetId,
                Order    = _nextOrder++,
            };
        }

        private int FindFree()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].IsActive)
                {
                    return i;
                }
            }

            return -1;
        }

        // 꽉 찼을 때만 부른다. 모든 칸이 활성이라 항상 하나를 찾는다
        private int FindOldest()
        {
            int  found      = 0;
            long foundOrder = long.MaxValue;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsActive && (_slots[i].Order < foundOrder))
                {
                    found      = i;
                    foundOrder = _slots[i].Order;
                }
            }

            return found;
        }
    }
}
