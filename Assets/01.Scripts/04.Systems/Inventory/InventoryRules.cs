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
        InvalidRequest    = 12,
        InvalidState      = 13,
        DuplicateRequest = 14,
        NoEquippedWeapon  = 15,
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

    /// <summary> 한 revision에서 함께 반영할 인벤토리 원본 변경 계획 </summary>
    public readonly struct InventoryCommitPlan
    {
        public readonly uint           ExpectedRevision;
        public readonly InventoryWrite First;
        public readonly InventoryWrite Second;
        public readonly int            WriteCount;

        public bool IsValid => ((WriteCount == 1) ||
            ((WriteCount == 2) && (First.SlotIndex != Second.SlotIndex)));

        public InventoryCommitPlan(uint expectedRevision, in InventoryWrite first)
        {
            ExpectedRevision = expectedRevision;
            First            = first;
            Second           = default;
            WriteCount       = 1;
        }

        public InventoryCommitPlan(uint expectedRevision, in InventoryWrite first,
            in InventoryWrite second)
        {
            ExpectedRevision = expectedRevision;
            First            = first;
            Second           = second;
            WriteCount       = 2;
        }
    }

    /// <summary> 장착 교환에서 인벤토리 칸과 장착 무기를 함께 커밋할 값 </summary>
    public readonly struct EquipmentSwapWrite
    {
        public readonly int                   SlotIndex;
        public readonly InventorySlotSnapshot SlotValue;
        public readonly WeaponCombatSnapshot  PreviousWeapon;
        public readonly WeaponCombatSnapshot  NextWeapon;

        public EquipmentSwapWrite(int slotIndex, in InventorySlotSnapshot slotValue,
            in WeaponCombatSnapshot previousWeapon, in WeaponCombatSnapshot nextWeapon)
        {
            SlotIndex      = slotIndex;
            SlotValue      = slotValue;
            PreviousWeapon = previousWeapon;
            NextWeapon     = nextWeapon;
        }
    }

    /// <summary> 티켓으로 장착 장비 수치만 교체할 서버 변경 계획 </summary>
    public readonly struct EquippedEquipmentWrite
    {
        public readonly uint                  ExpectedLoadoutRevision;
        public readonly WeaponCombatSnapshot  Previous;
        public readonly WeaponCombatSnapshot  Next;

        public bool IsValid => ((ExpectedLoadoutRevision != uint.MaxValue) &&
            Previous.HasEquipment && Next.HasEquipment &&
            (Previous.InstanceId == Next.InstanceId) &&
            (Previous.WeaponKind == Next.WeaponKind));

        public EquippedEquipmentWrite(uint expectedLoadoutRevision,
            in WeaponCombatSnapshot previous, in WeaponCombatSnapshot next)
        {
            ExpectedLoadoutRevision = expectedLoadoutRevision;
            Previous                = previous;
            Next                    = next;
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
            ShopItemKind itemId, int amount, int carryLimit, int maxStack,
            uint expectedRevision, out InventoryWrite write)
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

            if (maxStack <= 0)
            {
                return InventoryResult.InvalidAmount;
            }

            int resolvedCarryLimit = ResolveCarryLimit(carryLimit);
            if ((amount > maxStack) ||
                (((long) GetConsumableCount(current, itemId) + amount) > resolvedCarryLimit))
            {
                return InventoryResult.StackFull;
            }

            if (TryFindStackable(current, itemId, amount, maxStack, out int stackSlot))
            {
                InventorySlotSnapshot merged = current.GetSlot(stackSlot);
                write = new InventoryWrite(stackSlot,
                    InventorySlotSnapshot.FromConsumable(itemId, merged.Count + amount));
                return InventoryResult.Success;
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

        /// <summary> 인벤토리 장비를 현재 장비와 원자적으로 교환할 값을 계산한다 </summary>
        public static InventoryResult TrySwapEquipment(InventorySnapshot current, int slot,
            uint instanceId, in WeaponCombatSnapshot equipped, uint expectedRevision,
            out EquipmentSwapWrite write)
        {
            write = default;

            InventoryResult guard = CheckCommon(current, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if (!InventorySnapshot.IsValidIndex(slot))
            {
                return InventoryResult.InvalidSlot;
            }

            InventorySlotSnapshot selected = current.GetSlot(slot);
            if (selected.IsEmpty)
            {
                return InventoryResult.SlotEmpty;
            }

            if (selected.Kind != InventoryItemKind.Equipment)
            {
                return InventoryResult.KindMismatch;
            }

            if ((instanceId == 0u) || (selected.Equipment.InstanceId != instanceId))
            {
                return InventoryResult.ItemChanged;
            }

            if (!IsValidEquippedWeapon(equipped))
            {
                return InventoryResult.InvalidItem;
            }

            // 현재 장비가 인벤토리에 있으면 이미 복제된 상태이므로 교환하지 않는다
            if (equipped.HasEquipment && TryFindEquipment(current, equipped.InstanceId, out _))
            {
                return InventoryResult.DuplicateItem;
            }

            InventorySlotSnapshot previousSlot = equipped.HasEquipment
                ? InventorySlotSnapshot.FromEquipment(equipped)
                : default;

            write = new EquipmentSwapWrite(slot, previousSlot, equipped, selected.Equipment);
            return InventoryResult.Success;
        }

        /// <summary> 장착 장비를 인벤토리로 옮기고 기본 무기로 돌아갈 교환 값을 계산한다 </summary>
        public static InventoryResult TryUnequipEquipment(InventorySnapshot current,
            in WeaponCombatSnapshot equipped, in WeaponCombatSnapshot basicWeapon,
            uint expectedRevision, out EquipmentSwapWrite write)
        {
            write = default;

            InventoryResult guard = CheckCommon(current, expectedRevision);
            if (guard != InventoryResult.Success)
            {
                return guard;
            }

            if (!equipped.HasEquipment)
            {
                return InventoryResult.NoEquippedWeapon;
            }

            if (!IsValidItem(equipped) || (!IsValidBasicWeapon(basicWeapon)) ||
                (equipped.WeaponKind != basicWeapon.WeaponKind))
            {
                return InventoryResult.InvalidItem;
            }

            if (TryFindEquipment(current, equipped.InstanceId, out _))
            {
                return InventoryResult.DuplicateItem;
            }

            if (!TryFindEmptySlot(current, out int slot))
            {
                return InventoryResult.Full;
            }

            write = new EquipmentSwapWrite(slot,
                InventorySlotSnapshot.FromEquipment(equipped), equipped, basicWeapon);
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

        /// <summary> 장비 ID가 그대로인 새 확정 값으로 지정 칸을 교체한다 </summary>
        public static InventoryResult TryReplaceEquipment(InventorySnapshot current, int slot,
            uint instanceId, in WeaponCombatSnapshot replacement, uint expectedRevision,
            out InventoryWrite write)
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

            if ((!IsValidItem(replacement)) || (replacement.InstanceId != instanceId) ||
                (replacement.WeaponKind != value.Equipment.WeaponKind))
            {
                return InventoryResult.InvalidItem;
            }

            write = new InventoryWrite(slot, InventorySlotSnapshot.FromEquipment(replacement));
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
            return (item.HasEquipment && IsValidWeaponSnapshot(item));
        }

        /// <summary> 기본 무기를 포함한 장착 snapshot 형식을 검사한다 </summary>
        public static bool IsValidEquippedWeapon(in WeaponCombatSnapshot item)
        {
            return IsValidWeaponSnapshot(item);
        }

        /// <summary> ShopSO의 음수 CarryLimit을 무제한 총 소지량으로 바꾼다 </summary>
        public static int ResolveCarryLimit(int carryLimit) => (carryLimit < 0 ? int.MaxValue : carryLimit);

        /// <summary> 같은 소모품을 모든 칸에서 합산한다 </summary>
        public static int GetConsumableCount(InventorySnapshot current, ShopItemKind itemId)
        {
            if (current == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                InventorySlotSnapshot value = current.GetSlot(i);
                if ((value.Kind == InventoryItemKind.Consumable) && (value.ItemId == itemId))
                {
                    total += value.Count;
                }
            }

            return total;
        }

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

        private static bool IsValidBasicWeapon(in WeaponCombatSnapshot item)
        {
            return ((!item.HasEquipment) && IsValidWeaponSnapshot(item));
        }

        private static bool IsValidWeaponSnapshot(in WeaponCombatSnapshot item)
        {
            return ((Enum.IsDefined(typeof(WeaponKind), item.WeaponKind)) &&
                    (Enum.IsDefined(typeof(ItemGrade), item.Grade)) && (IsFinite(item.AttackPower)) &&
                    (item.AttackPower > 0f) && (IsValidOption(item.Option0Kind, item.Option0Value)) &&
                    (IsValidOption(item.Option1Kind, item.Option1Value)) &&
                    ((item.Option1Kind == EquipmentOptionKind.None) ||
                     ((item.Option0Kind != EquipmentOptionKind.None) && (item.Option0Kind != item.Option1Kind))));
        }

        private static bool IsValidOption(EquipmentOptionKind kind, float value)
        {
            return ((Enum.IsDefined(typeof(EquipmentOptionKind), kind)) && (IsFinite(value)) &&
                    ((kind != EquipmentOptionKind.None) || (value == 0f)));
        }

        private static bool IsFinite(float value) => ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
    }
}
