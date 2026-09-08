// System
using System;

namespace Geumeum.Common
{
    /// <summary> 장착 무기를 제외한 인벤토리 12칸의 원본. 장비와 소모품을 함께 담는다 </summary>
    [Serializable]
    public class InventorySnapshot
    {
        /****************************************
        *               Constants
        ****************************************/

        public const int COLUMN_COUNT = 4;
        public const int ROW_COUNT    = 3;
        public const int SLOT_COUNT   = COLUMN_COUNT * ROW_COUNT;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 낙관적 동시성 검증용. 서버가 값을 바꿀 때마다 1 올린다 </summary>
        public uint Revision;

        private readonly InventorySlotSnapshot[] _slots = new InventorySlotSnapshot[SLOT_COUNT];

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 차 있는 칸 수 </summary>
        public int UsedCount
        {
            get
            {
                int used = 0;
                for (int i = 0; i < SLOT_COUNT; ++i)
                {
                    if (!_slots[i].IsEmpty)
                    {
                        ++used;
                    }
                }

                return used;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsValidIndex(int index) => ((index >= 0) && (index < SLOT_COUNT));

        public InventorySlotSnapshot GetSlot(int index)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _slots[index];
        }

        public void SetSlot(int index, in InventorySlotSnapshot value)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _slots[index] = value;
        }

        public void Clear()
        {
            Revision = 0u;
            Array.Clear(_slots, 0, SLOT_COUNT);
        }

        /// <summary> 참조를 공유하지 않는 복사본. carrier 캡처와 복구에 쓴다 </summary>
        public InventorySnapshot Clone()
        {
            var copy = new InventorySnapshot { Revision = Revision };
            Array.Copy(_slots, copy._slots, SLOT_COUNT);
            return copy;
        }

        /// <summary> 다른 그릇의 값을 그대로 덮어쓴다 </summary>
        public void CopyFrom(InventorySnapshot other)
        {
            if (other == null)
            {
                Clear();
                return;
            }

            Revision = other.Revision;
            Array.Copy(other._slots, _slots, SLOT_COUNT);
        }
    }
}
