// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public enum InventoryResult
    {
        Success           = 0,
        InvalidItem       = 1,
        Full              = 2,
        DuplicateItem     = 3,
        StaleRevision     = 4,
        InvalidSlot       = 5,
        ItemChanged       = 6,
        RevisionExhausted = 7,
        InvalidAmount     = 8,
        StackFull         = 9,
        SlotEmpty         = 10,
        KindMismatch      = 11,
    }

    /// <summary> 서버가 인벤토리에 쓸 값 하나. 한 요청은 한 칸만 바꾼다 </summary>
    public readonly struct InventoryWrite
    {
        public readonly int                   SlotIndex;
        public readonly InventorySlotSnapshot Value;

        public InventoryWrite(int slotIndex, in InventorySlotSnapshot value)
        {
            SlotIndex = slotIndex;
            Value     = value;
        }
    }

    /// <summary> 검증이 성공한 경우에만 바꿀 칸 하나를 계산한다. Mirror에 의존하지 않는 순수 규칙이다 </summary>
    public static class InventoryRules
    {
        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 장비 하나를 빈 칸에 넣는다. 장비는 스택하지 않는다 </summary>
        public static InventoryResult TryStoreEquipment(InventorySnapshot current,
            in WeaponCombatSnapshot item, uint expectedRevision, out InventoryWrite write)
        {
            write = default;

            InventoryResult guard = CheckCommon(current, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if (!IsValidItem(item))
            {
                return InventoryResult.InvalidItem;
            }

            if (TryFindEquipment(current, item.InstanceId, out _))
            {
                return InventoryResult.DuplicateItem;
            }

            if (!TryFindEmptySlot(current, out int slot))
            {
                return InventoryResult.Full;
            }

            write = new InventoryWrite(slot, InventorySlotSnapshot.FromEquipment(item));
            return InventoryResult.Success;
        }

        /// <summary>
        /// 소모품을 넣는다. 같은 종류의 여유 있는 칸에 먼저 쌓고, 없으면 빈 칸을 쓴다.
        /// 한 요청은 한 칸에만 반영하므로 여러 칸에 걸쳐 나눠 담지 않는다
        /// </summary>
        public static InventoryResult TryStoreConsumable(InventorySnapshot current,
            ShopItemKind itemId, int amount, int carryLimit, uint expectedRevision, out InventoryWrite write)
        {
            write = default;

            InventoryResult guard = CheckCommon(current, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if ((itemId == ShopItemKind.None) || (!Enum.IsDefined(typeof(ShopItemKind), itemId)))
            {
                return InventoryResult.InvalidItem;
            }

            if (amount <= 0)
            {
                return InventoryResult.InvalidAmount;
            }

            int stackLimit = ResolveStackLimit(carryLimit);
            if (amount > stackLimit)
            {
                return InventoryResult.InvalidAmount;
            }

            if (TryFindStackable(current, itemId, amount, stackLimit, out int stackSlot))
            {
                InventorySlotSnapshot merged = current.GetSlot(stackSlot);
                write = new InventoryWrite(stackSlot,
                    InventorySlotSnapshot.FromConsumable(itemId, merged.Count + amount));
                return InventoryResult.Success;
            }

            // 같은 소모품이 이미 있는데 여유가 없다면 빈 칸이 남아도 소지 상한에 걸린 것이다
            if (HasSameConsumable(current, itemId))
            {
                return InventoryResult.StackFull;
            }

            if (!TryFindEmptySlot(current, out int slot))
            {
                return InventoryResult.Full;
            }

            write = new InventoryWrite(slot, InventorySlotSnapshot.FromConsumable(itemId, amount));
            return InventoryResult.Success;
        }

        /// <summary> 장비를 인스턴스 ID로 확인하고 그 칸을 비운다. 장착과 양도에 쓴다 </summary>
        public static InventoryResult TryTakeEquipment(InventorySnapshot current, int slot,
            uint instanceId, uint expectedRevision, out InventoryWrite write)
        {
            write = default;

            InventoryResult guard = CheckSlotRequest(current, slot, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            InventorySlotSnapshot value = current.GetSlot(slot);
            if (value.Kind != InventoryItemKind.Equipment)
            {
                return InventoryResult.KindMismatch;
            }

            if ((instanceId == 0u) || (value.Equipment.InstanceId != instanceId))
            {
                return InventoryResult.ItemChanged;
            }

            write = new InventoryWrite(slot, default);
            return InventoryResult.Success;
        }

        /// <summary> 소모품을 지정 수량만큼 쓴다. 0이 되면 칸을 비운다 </summary>
        public static InventoryResult TryConsume(InventorySnapshot current, int slot,
            ShopItemKind expectedItem, int amount, uint expectedRevision, out InventoryWrite write)
        {
            write = default;

            InventoryResult guard = CheckSlotRequest(current, slot, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if (amount <= 0)
            {
                return InventoryResult.InvalidAmount;
            }

            InventorySlotSnapshot value = current.GetSlot(slot);
            if (value.Kind != InventoryItemKind.Consumable)
            {
                return InventoryResult.KindMismatch;
            }

            if (value.ItemId != expectedItem)
            {
                return InventoryResult.ItemChanged;
            }

            if (value.Count < amount)
            {
                return InventoryResult.InvalidAmount;
            }

            int remaining = value.Count - amount;
            write = new InventoryWrite(slot, remaining > 0
                ? InventorySlotSnapshot.FromConsumable(value.ItemId, remaining)
                : default);
            return InventoryResult.Success;
        }

        /****************************************
        *                Queries
        ****************************************/

        public static bool TryFindEquipment(InventorySnapshot current, uint instanceId, out int slot)
        {
            slot = -1;
            if ((current == null) || (instanceId == 0u))
            {
                return false;
            }

            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                InventorySlotSnapshot value = current.GetSlot(i);
                if ((value.Kind == InventoryItemKind.Equipment) && (value.Equipment.InstanceId == instanceId))
                {
                    slot = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary> 구조 검증. 소지 상한은 저장 시점에만 보므로 여기서 보지 않는다 </summary>
        public static bool IsValid(InventorySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                InventorySlotSnapshot value = snapshot.GetSlot(i);
                if (!IsValidSlot(value))
                {
                    return false;
                }

                if (value.Kind != InventoryItemKind.Equipment)
                {
                    continue;
                }

                // 같은 장비가 두 칸에 있으면 복제다
                for (int j = i + 1; j < InventorySnapshot.SLOT_COUNT; ++j)
                {
                    InventorySlotSnapshot other = snapshot.GetSlot(j);
                    if ((other.Kind == InventoryItemKind.Equipment) &&
                        (other.Equipment.InstanceId == value.Equipment.InstanceId))
                    {
                        return false;
                    }
                }
            }

            return true;
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

        /// <summary> ShopSO의 CarryLimit을 스택 상한으로 바꾼다. -1은 무제한이다 </summary>
        public static int ResolveStackLimit(int carryLimit) => (carryLimit < 0 ? int.MaxValue : carryLimit);

        /****************************************
        *               Internals
        ****************************************/

        private static InventoryResult CheckCommon(InventorySnapshot current, uint expectedRevision)
        {
            if (current == null)
            {
                return InventoryResult.InvalidItem;
            }

            if (current.Revision != expectedRevision)
            {
                return InventoryResult.StaleRevision;
            }

            if (current.Revision == uint.MaxValue)
            {
                return InventoryResult.RevisionExhausted;
            }

            return InventoryResult.Success;
        }

        private static InventoryResult CheckSlotRequest(InventorySnapshot current, int slot, uint expectedRevision)
        {
            InventoryResult guard = CheckCommon(current, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if (!InventorySnapshot.IsValidIndex(slot))
            {
                return InventoryResult.InvalidSlot;
            }

            if (current.GetSlot(slot).IsEmpty)
            {
                return InventoryResult.SlotEmpty;
            }

            return InventoryResult.Success;
        }

        private static bool TryFindEmptySlot(InventorySnapshot current, out int slot)
        {
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                if (current.GetSlot(i).IsEmpty)
                {
                    slot = i;
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private static bool TryFindStackable(InventorySnapshot current, ShopItemKind itemId,
            int amount, int stackLimit, out int slot)
        {
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                InventorySlotSnapshot value = current.GetSlot(i);
                if ((value.Kind == InventoryItemKind.Consumable) && (value.ItemId == itemId) &&
                    ((stackLimit - value.Count) >= amount))
                {
                    slot = i;
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private static bool HasSameConsumable(InventorySnapshot current, ShopItemKind itemId)
        {
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                InventorySlotSnapshot value = current.GetSlot(i);
                if ((value.Kind == InventoryItemKind.Consumable) && (value.ItemId == itemId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidSlot(in InventorySlotSnapshot value)
        {
            switch (value.Kind)
            {
                case InventoryItemKind.None:
                    return value.Equals(default(InventorySlotSnapshot));

                case InventoryItemKind.Equipment:
                    return ((value.Count == 1) && (value.ItemId == ShopItemKind.None) &&
                            (IsValidItem(value.Equipment)));

                case InventoryItemKind.Consumable:
                    return ((value.Count > 0) && (value.ItemId != ShopItemKind.None) &&
                            (Enum.IsDefined(typeof(ShopItemKind), value.ItemId)) &&
                            (value.Equipment.Equals(default(WeaponCombatSnapshot))));

                default:
                    return false;
            }
        }

        private static bool IsValidOption(EquipmentOptionKind kind, float value)
        {
            return ((Enum.IsDefined(typeof(EquipmentOptionKind), kind)) && (IsFinite(value)) &&
                    ((kind != EquipmentOptionKind.None) || (value == 0f)));
        }

        private static bool IsFinite(float value) => ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
    }
}
