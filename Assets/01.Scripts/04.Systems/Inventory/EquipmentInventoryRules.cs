// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public enum InventoryResult
    {
        Success          = 0,
        InvalidItem      = 1,
        Full             = 2,
        DuplicateItem    = 3,
        StaleRevision    = 4,
        InvalidSlot      = 5,
        ItemChanged      = 6,
        RevisionExhausted = 7,
    }

    /// <summary> 검증이 성공한 경우에만 다음 보관함 값을 만든다 </summary>
    public static class EquipmentInventoryRules
    {
        public static InventoryResult TryStore(in EquipmentInventorySnapshot current,
            in WeaponCombatSnapshot item, uint expectedRevision, out EquipmentInventorySnapshot next)
        {
            next = current;
            if (current.Revision != expectedRevision)
            {
                return InventoryResult.StaleRevision;
            }

            if ((current.Revision == uint.MaxValue))
            {
                return InventoryResult.RevisionExhausted;
            }

            if ((!IsValid(current)) || (!IsValidItem(item)))
            {
                return InventoryResult.InvalidItem;
            }

            if ((current.Slot0.InstanceId == item.InstanceId) || (current.Slot1.InstanceId == item.InstanceId))
            {
                return InventoryResult.DuplicateItem;
            }

            if (current.Count == EquipmentInventorySnapshot.SLOT_COUNT)
            {
                return InventoryResult.Full;
            }

            if (!current.Slot0.HasEquipment)
            {
                next.Slot0 = item;
            }
            else
            {
                next.Slot1 = item;
            }

            ++next.Revision;
            return InventoryResult.Success;
        }

        public static InventoryResult TryRemove(in EquipmentInventorySnapshot current, int slot,
            uint instanceId, uint expectedRevision, out EquipmentInventorySnapshot next)
        {
            next = current;
            if (current.Revision != expectedRevision)
            {
                return InventoryResult.StaleRevision;
            }

            if (current.Revision == uint.MaxValue)
            {
                return InventoryResult.RevisionExhausted;
            }

            if (!IsValid(current))
            {
                return InventoryResult.InvalidItem;
            }

            if ((slot < 0) || (slot >= EquipmentInventorySnapshot.SLOT_COUNT))
            {
                return InventoryResult.InvalidSlot;
            }

            if ((instanceId == 0u) || (current.GetSlot(slot).InstanceId != instanceId))
            {
                return InventoryResult.ItemChanged;
            }

            if (slot == 0)
            {
                next.Slot0 = default;
            }
            else
            {
                next.Slot1 = default;
            }

            ++next.Revision;
            return InventoryResult.Success;
        }

        public static bool IsValid(in EquipmentInventorySnapshot snapshot)
        {
            return ((IsValidSlot(snapshot.Slot0)) && (IsValidSlot(snapshot.Slot1)) &&
                    ((!snapshot.Slot0.HasEquipment) || (snapshot.Slot0.InstanceId != snapshot.Slot1.InstanceId)));
        }

        public static bool IsValidItem(in WeaponCombatSnapshot item)
        {
            return ((item.HasEquipment) && (Enum.IsDefined(typeof(WeaponKind), item.WeaponKind)) &&
                    (Enum.IsDefined(typeof(ItemGrade), item.Grade)) && (IsFinite(item.AttackPower)) &&
                    (item.AttackPower > 0f) && (IsValidOption(item.Option0Kind, item.Option0Value)) &&
                    (IsValidOption(item.Option1Kind, item.Option1Value)) &&
                    ((item.Option1Kind == EquipmentOptionKind.None) ||
                     ((item.Option0Kind != EquipmentOptionKind.None) && (item.Option0Kind != item.Option1Kind))));
        }

        private static bool IsValidSlot(in WeaponCombatSnapshot item)
        {
            return item.HasEquipment ? IsValidItem(item) : item.Equals(default(WeaponCombatSnapshot));
        }

        private static bool IsValidOption(EquipmentOptionKind kind, float value)
        {
            return ((Enum.IsDefined(typeof(EquipmentOptionKind), kind)) && (IsFinite(value)) &&
                    ((kind != EquipmentOptionKind.None) || (value == 0f)));
        }

        private static bool IsFinite(float value) => ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
    }
}
