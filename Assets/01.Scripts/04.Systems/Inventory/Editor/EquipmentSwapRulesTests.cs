// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems.Tests
{
    public class EquipmentSwapRulesTests
    {
        /****************************************
        *                Helpers
        ****************************************/

        private static WeaponCombatSnapshot MakeWeapon(uint instanceId, WeaponKind kind = WeaponKind.Sword,
            float attackPower = 100f)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId   = instanceId,
                WeaponKind   = kind,
                Grade        = ItemGrade.Common,
                AttackPower  = attackPower,
                Option0Kind  = EquipmentOptionKind.None,
                Option0Value = 0f,
                Option1Kind  = EquipmentOptionKind.None,
                Option1Value = 0f,
            };
        }

        private static void Apply(InventorySnapshot inventory, in EquipmentSwapWrite write)
        {
            inventory.SetSlot(write.SlotIndex, write.SlotValue);
            ++inventory.Revision;
        }

        private static void Store(InventorySnapshot inventory, in WeaponCombatSnapshot weapon)
        {
            InventoryResult result = InventoryRules.TryStoreEquipment(inventory, weapon,
                inventory.Revision, out InventoryWrite write);
            Assert.AreEqual(InventoryResult.Success, result);
            inventory.SetSlot(write.SlotIndex, write.Value);
            ++inventory.Revision;
        }

        /****************************************
        *                Equipment
        ****************************************/

        [Test]
        public void EquipBasicWeaponAtomicallySwapsSelectedSlot()
        {
            var inventory = new InventorySnapshot();
            WeaponCombatSnapshot basic = MakeWeapon(0u);
            WeaponCombatSnapshot equipment = MakeWeapon(11u, WeaponKind.Bow, 145f);
            Store(inventory, equipment);

            InventoryResult result = InventoryRules.TrySwapEquipment(inventory, 0, equipment.InstanceId,
                basic, inventory.Revision, out EquipmentSwapWrite write);

            Assert.AreEqual(InventoryResult.Success, result);
            Assert.AreEqual(0u, write.SlotValue.Equipment.InstanceId);
            Assert.AreEqual(0u, write.PreviousWeapon.InstanceId);
            Assert.AreEqual(equipment, write.NextWeapon);

            Apply(inventory, write);
            Assert.IsTrue(inventory.GetSlot(0).IsEmpty);
            Assert.AreEqual(2u, inventory.Revision);
        }

        [Test]
        public void EquipExistingWeaponReturnsPreviousEquipmentToSameSlot()
        {
            var inventory = new InventorySnapshot();
            WeaponCombatSnapshot equipped = MakeWeapon(3u);
            WeaponCombatSnapshot replacement = MakeWeapon(9u, WeaponKind.Bow, 125f);
            Store(inventory, replacement);

            InventoryResult result = InventoryRules.TrySwapEquipment(inventory, 0,
                replacement.InstanceId, equipped, inventory.Revision, out EquipmentSwapWrite write);

            Assert.AreEqual(InventoryResult.Success, result);
            Assert.AreEqual(equipped, write.SlotValue.Equipment);
            Assert.AreEqual(equipped, write.PreviousWeapon);
            Assert.AreEqual(replacement, write.NextWeapon);
        }

        [Test]
        public void EquipRejectsEquippedInstanceDuplicatedInAnySlot()
        {
            var inventory = new InventorySnapshot();
            WeaponCombatSnapshot duplicated = MakeWeapon(7u);
            Store(inventory, duplicated);

            InventoryResult result = InventoryRules.TrySwapEquipment(inventory, 0,
                duplicated.InstanceId, duplicated, inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.DuplicateItem, result);
        }

        [Test]
        public void EquipRejectsStaleRevisionWithoutWrite()
        {
            var inventory = new InventorySnapshot();
            WeaponCombatSnapshot basic = MakeWeapon(0u);
            WeaponCombatSnapshot equipment = MakeWeapon(4u);
            Store(inventory, equipment);
            uint revision = inventory.Revision;

            InventoryResult result = InventoryRules.TrySwapEquipment(inventory, 0,
                equipment.InstanceId, basic, revision - 1u, out EquipmentSwapWrite write);

            Assert.AreEqual(InventoryResult.StaleRevision, result);
            Assert.AreEqual(default(EquipmentSwapWrite), write);
            Assert.AreEqual(revision, inventory.Revision);
            Assert.AreEqual(equipment, inventory.GetSlot(0).Equipment);
        }

        [Test]
        public void UnequipMovesEquipmentToLowestEmptySlotAndBuildsBasicWeapon()
        {
            var inventory = new InventorySnapshot();
            WeaponCombatSnapshot existing = MakeWeapon(13u);
            Store(inventory, MakeWeapon(2u));
            WeaponCombatSnapshot basic = MakeWeapon(0u);

            InventoryResult result = InventoryRules.TryUnequipEquipment(inventory, existing, basic,
                inventory.Revision, out EquipmentSwapWrite write);

            Assert.AreEqual(InventoryResult.Success, result);
            Assert.AreEqual(1, write.SlotIndex);
            Assert.AreEqual(existing, write.SlotValue.Equipment);
            Assert.AreEqual(basic, write.NextWeapon);

            Apply(inventory, write);
            Assert.AreEqual(existing, inventory.GetSlot(1).Equipment);
            Assert.AreEqual(2, inventory.UsedCount);
        }

        [Test]
        public void UnequipRejectsWhenInventoryHasNoEmptySlot()
        {
            var inventory = new InventorySnapshot();
            for (uint i = 1u; i <= InventorySnapshot.SLOT_COUNT; ++i)
            {
                Store(inventory, MakeWeapon(i));
            }

            WeaponCombatSnapshot equipped = MakeWeapon(99u);
            WeaponCombatSnapshot basic = MakeWeapon(0u);

            InventoryResult result = InventoryRules.TryUnequipEquipment(inventory, equipped, basic,
                inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.Full, result);
            Assert.AreEqual(InventorySnapshot.SLOT_COUNT, inventory.UsedCount);
        }
    }
}
