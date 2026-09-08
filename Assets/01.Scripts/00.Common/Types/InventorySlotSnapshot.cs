// System
using System;

namespace Geumeum.Common
{
    /// <summary> 인벤토리 한 칸의 확정 값. 장비와 소모품을 같은 칸 타입으로 담는다 </summary>
    [Serializable]
    public struct InventorySlotSnapshot : IEquatable<InventorySlotSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        public InventoryItemKind Kind;

        /// <summary> 소모품 종류. Kind가 Consumable일 때만 유효하다 </summary>
        public ShopItemKind ItemId;

        /// <summary> 소모품 수량. 장비는 항상 1이고 빈 칸은 0이다 </summary>
        public int Count;

        /// <summary> 장비 확정 값. Kind가 Equipment일 때만 유효하다 </summary>
        public WeaponCombatSnapshot Equipment;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsEmpty => (Kind == InventoryItemKind.None);

        /****************************************
        *                Methods
        ****************************************/

        public static InventorySlotSnapshot FromEquipment(in WeaponCombatSnapshot equipment)
        {
            return new InventorySlotSnapshot
            {
                Kind      = InventoryItemKind.Equipment,
                ItemId    = ShopItemKind.None,
                Count     = 1,
                Equipment = equipment,
            };
        }

        public static InventorySlotSnapshot FromConsumable(ShopItemKind itemId, int count)
        {
            return new InventorySlotSnapshot
            {
                Kind      = InventoryItemKind.Consumable,
                ItemId    = itemId,
                Count     = count,
                Equipment = default,
            };
        }

        public bool Equals(InventorySlotSnapshot other)
        {
            return ((Kind == other.Kind) &&
                    (ItemId == other.ItemId) &&
                    (Count == other.Count) &&
                    (Equipment.Equals(other.Equipment)));
        }

        public override bool Equals(object obj) => ((obj is InventorySlotSnapshot other) && (Equals(other)));

        public override int GetHashCode()
        {
            return (((int) Kind) ^ (((int) ItemId) << 4) ^ (Count << 8) ^ Equipment.GetHashCode());
        }
    }
}
