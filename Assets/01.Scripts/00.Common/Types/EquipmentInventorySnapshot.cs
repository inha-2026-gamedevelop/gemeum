// System
using System;

namespace Geumeum.Common
{
    /// <summary> 장착 무기와 물약을 제외한 장비 보관 두 칸의 원본 </summary>
    [Serializable]
    public struct EquipmentInventorySnapshot : IEquatable<EquipmentInventorySnapshot>
    {
        public const int SLOT_COUNT = 2; // 고정 전송 필드 수

        public uint Revision;
        public WeaponCombatSnapshot Slot0;
        public WeaponCombatSnapshot Slot1;

        public int Count => (Slot0.HasEquipment ? 1 : 0) + (Slot1.HasEquipment ? 1 : 0);

        public WeaponCombatSnapshot GetSlot(int index)
        {
            if ((index < 0) || (index >= SLOT_COUNT))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index == 0 ? Slot0 : Slot1;
        }

        public bool Equals(EquipmentInventorySnapshot other)
        {
            return ((Revision == other.Revision) && (Slot0.Equals(other.Slot0)) && (Slot1.Equals(other.Slot1)));
        }

        public override bool Equals(object obj) => ((obj is EquipmentInventorySnapshot other) && (Equals(other)));

        public override int GetHashCode() => Revision.GetHashCode() ^ Slot0.GetHashCode() ^ Slot1.GetHashCode();
    }
}
