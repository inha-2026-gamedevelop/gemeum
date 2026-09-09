// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems.Tests
{
    public class InventoryRulesTests
    {
        /****************************************
        *               Constants
        ****************************************/

        private const int STACK_LIMIT = 3;
        private const int UNLIMITED   = -1;

        /****************************************
        *                Helpers
        ****************************************/

        private static WeaponCombatSnapshot MakeItem(uint instanceId)
        {
            return new WeaponCombatSnapshot
            {
                InstanceId   = instanceId,
                WeaponKind   = WeaponKind.Sword,
                Grade        = ItemGrade.Common,
                AttackPower  = 100f,
                Option0Kind  = EquipmentOptionKind.None,
                Option0Value = 0f,
                Option1Kind  = EquipmentOptionKind.None,
                Option1Value = 0f,
            };
        }

        /// <summary> 성공한 계산 결과를 실제로 반영한다. 컨트롤러가 하는 일과 같다 </summary>
        private static void Apply(InventorySnapshot inventory, in InventoryWrite write)
        {
            inventory.SetSlot(write.SlotIndex, write.Value);
            ++inventory.Revision;
        }

        private static void StoreEquipment(InventorySnapshot inventory, uint instanceId)
        {
            InventoryResult result =
                InventoryRules.TryStoreEquipment(inventory, MakeItem(instanceId), inventory.Revision, out var write);
            Assert.AreEqual(InventoryResult.Success, result);
            Apply(inventory, write);
        }

        private static void StoreConsumable(InventorySnapshot inventory, ShopItemKind itemId,
            int amount, int carryLimit, int maxStack = STACK_LIMIT)
        {
            InventoryResult result =
                InventoryRules.TryStoreConsumable(inventory, itemId, amount, carryLimit,
                    maxStack, inventory.Revision, out var write);
            Assert.AreEqual(InventoryResult.Success, result);
            Apply(inventory, write);
        }

        /****************************************
        *                Equipment
        ****************************************/

        [Test]
        public void StoreEquipmentFillsLowestEmptySlot()
        {
            var inventory = new InventorySnapshot();

            StoreEquipment(inventory, 1u);
            StoreEquipment(inventory, 2u);

            Assert.AreEqual(1u, inventory.GetSlot(0).Equipment.InstanceId);
            Assert.AreEqual(2u, inventory.GetSlot(1).Equipment.InstanceId);
            Assert.AreEqual(2, inventory.UsedCount);
            Assert.AreEqual(2u, inventory.Revision);
        }

        [Test]
        public void StoreEquipmentRejectsDuplicateInstanceId()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 7u);

            InventoryResult result =
                InventoryRules.TryStoreEquipment(inventory, MakeItem(7u), inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.DuplicateItem, result);
        }

        [Test]
        public void StoreEquipmentRejectsBasicWeapon()
        {
            var inventory = new InventorySnapshot();

            // 기본 무기(InstanceId 0)는 인벤토리에 들어가지 않는다 - D01
            InventoryResult result =
                InventoryRules.TryStoreEquipment(inventory, MakeItem(0u), inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.InvalidItem, result);
        }

        [Test]
        public void StoreEquipmentReportsFullAtTwelve()
        {
            var inventory = new InventorySnapshot();
            for (uint i = 1u; i <= InventorySnapshot.SLOT_COUNT; ++i)
            {
                StoreEquipment(inventory, i);
            }

            Assert.AreEqual(InventorySnapshot.SLOT_COUNT, inventory.UsedCount);

            InventoryResult result = InventoryRules.TryStoreEquipment(inventory,
                MakeItem(InventorySnapshot.SLOT_COUNT + 1u), inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.Full, result);
        }

        [Test]
        public void StaleRevisionIsRejected()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 1u);

            InventoryResult result =
                InventoryRules.TryStoreEquipment(inventory, MakeItem(2u), inventory.Revision - 1u, out _);

            Assert.AreEqual(InventoryResult.StaleRevision, result);
        }

        [Test]
        public void TakeEquipmentClearsSlotAndChecksInstanceId()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 5u);

            InventoryResult mismatched =
                InventoryRules.TryTakeEquipment(inventory, 0, 6u, inventory.Revision, out _);
            Assert.AreEqual(InventoryResult.ItemChanged, mismatched);

            InventoryResult result =
                InventoryRules.TryTakeEquipment(inventory, 0, 5u, inventory.Revision, out var write);
            Assert.AreEqual(InventoryResult.Success, result);

            Apply(inventory, write);
            Assert.IsTrue(inventory.GetSlot(0).IsEmpty);
            Assert.AreEqual(0, inventory.UsedCount);
        }

        [Test]
        public void SlotRequestsValidateIndexAndEmptiness()
        {
            var inventory = new InventorySnapshot();

            Assert.AreEqual(InventoryResult.InvalidSlot,
                InventoryRules.TryTakeEquipment(inventory, -1, 1u, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.InvalidSlot,
                InventoryRules.TryTakeEquipment(inventory, InventorySnapshot.SLOT_COUNT, 1u, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.SlotEmpty,
                InventoryRules.TryTakeEquipment(inventory, 0, 1u, inventory.Revision, out _));
        }

        [Test]
        public void TakeEquipmentRejectsConsumableSlot()
        {
            var inventory = new InventorySnapshot();
            StoreConsumable(inventory, ShopItemKind.HealthPotion, 1, STACK_LIMIT);

            InventoryResult result =
                InventoryRules.TryTakeEquipment(inventory, 0, 1u, inventory.Revision, out _);

            Assert.AreEqual(InventoryResult.KindMismatch, result);
        }

        [Test]
        public void ReplaceEquipmentRequiresSameIdentityAndWeaponKind()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 15u);
            WeaponCombatSnapshot replacement = MakeItem(15u);
            replacement.Grade = ItemGrade.Talisman;
            replacement.AttackPower = 125f;

            Assert.AreEqual(InventoryResult.Success,
                InventoryRules.TryReplaceEquipment(inventory, 0, 15u, replacement,
                    inventory.Revision, out InventoryWrite write));
            Assert.AreEqual(15u, write.Value.Equipment.InstanceId);
            Assert.AreEqual(ItemGrade.Talisman, write.Value.Equipment.Grade);

            replacement.InstanceId = 16u;
            Assert.AreEqual(InventoryResult.InvalidItem,
                InventoryRules.TryReplaceEquipment(inventory, 0, 15u, replacement,
                    inventory.Revision, out _));

            replacement.InstanceId = 15u;
            replacement.WeaponKind = WeaponKind.Bow;
            Assert.AreEqual(InventoryResult.InvalidItem,
                InventoryRules.TryReplaceEquipment(inventory, 0, 15u, replacement,
                    inventory.Revision, out _));
        }

        /****************************************
        *               Consumable
        ****************************************/

        [Test]
        public void ConsumableStacksIntoExistingSlot()
        {
            var inventory = new InventorySnapshot();

            for (int i = 0; i < STACK_LIMIT; ++i)
            {
                StoreConsumable(inventory, ShopItemKind.HealthPotion, 1, STACK_LIMIT);
            }

            // 세 번 샀지만 칸은 하나만 쓴다
            Assert.AreEqual(1, inventory.UsedCount);
            Assert.AreEqual(STACK_LIMIT, inventory.GetSlot(0).Count);
        }

        [Test]
        public void ConsumableBeyondCarryLimitFailsEvenWithEmptySlots()
        {
            var inventory = new InventorySnapshot();
            for (int i = 0; i < STACK_LIMIT; ++i)
            {
                StoreConsumable(inventory, ShopItemKind.HealthPotion, 1, STACK_LIMIT);
            }

            InventoryResult result = InventoryRules.TryStoreConsumable(inventory, ShopItemKind.HealthPotion, 1,
                STACK_LIMIT, STACK_LIMIT, inventory.Revision, out _);

            // 빈 칸이 11개 남아 있어도 소지 제한이 우선이다
            Assert.AreEqual(InventoryResult.StackFull, result);
            Assert.AreEqual(1, inventory.UsedCount);
        }

        [Test]
        public void DifferentConsumableKindsUseSeparateSlots()
        {
            var inventory = new InventorySnapshot();

            StoreConsumable(inventory, ShopItemKind.HealthPotion, 1, STACK_LIMIT);
            StoreConsumable(inventory, ShopItemKind.RerollTicket, 1, UNLIMITED);

            Assert.AreEqual(2, inventory.UsedCount);
            Assert.AreEqual(ShopItemKind.RerollTicket, inventory.GetSlot(1).ItemId);
        }

        [Test]
        public void UnlimitedCarryLimitStillHonorsPerSlotMaxStack()
        {
            var inventory = new InventorySnapshot();

            for (int i = 0; i < 5; ++i)
            {
                StoreConsumable(inventory, ShopItemKind.RerollTicket, 1, UNLIMITED, 1);
            }

            Assert.AreEqual(5, inventory.UsedCount);
            for (int i = 0; i < 5; ++i)
            {
                Assert.AreEqual(1, inventory.GetSlot(i).Count);
            }
        }

        [Test]
        public void ConsumeReducesCountThenClearsSlot()
        {
            var inventory = new InventorySnapshot();
            StoreConsumable(inventory, ShopItemKind.HealthPotion, 2, STACK_LIMIT);

            InventoryResult first = InventoryRules.TryConsume(inventory, 0, ShopItemKind.HealthPotion, 1,
                inventory.Revision, out var write);
            Apply(inventory, write);
            Assert.AreEqual(InventoryResult.Success, first);
            Assert.AreEqual(1, inventory.GetSlot(0).Count);

            InventoryResult second = InventoryRules.TryConsume(inventory, 0, ShopItemKind.HealthPotion, 1,
                inventory.Revision, out write);
            Apply(inventory, write);
            Assert.AreEqual(InventoryResult.Success, second);
            Assert.IsTrue(inventory.GetSlot(0).IsEmpty);
        }

        [Test]
        public void ConsumeRejectsWrongItemAndOverdraw()
        {
            var inventory = new InventorySnapshot();
            StoreConsumable(inventory, ShopItemKind.HealthPotion, 1, STACK_LIMIT);

            Assert.AreEqual(InventoryResult.ItemChanged,
                InventoryRules.TryConsume(inventory, 0, ShopItemKind.SpiritPotion, 1, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.InvalidAmount,
                InventoryRules.TryConsume(inventory, 0, ShopItemKind.HealthPotion, 2, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.InvalidAmount,
                InventoryRules.TryConsume(inventory, 0, ShopItemKind.HealthPotion, 0, inventory.Revision, out _));

            // 실패한 요청은 아무것도 바꾸지 않는다
            Assert.AreEqual(1, inventory.GetSlot(0).Count);
        }

        [Test]
        public void StoreConsumableRejectsInvalidKindAndAmount()
        {
            var inventory = new InventorySnapshot();

            Assert.AreEqual(InventoryResult.InvalidItem, InventoryRules.TryStoreConsumable(inventory,
                ShopItemKind.None, 1, STACK_LIMIT, STACK_LIMIT, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.InvalidAmount, InventoryRules.TryStoreConsumable(inventory,
                ShopItemKind.HealthPotion, 0, STACK_LIMIT, STACK_LIMIT, inventory.Revision, out _));
            Assert.AreEqual(InventoryResult.StackFull, InventoryRules.TryStoreConsumable(inventory,
                ShopItemKind.HealthPotion, STACK_LIMIT + 1, STACK_LIMIT,
                STACK_LIMIT, inventory.Revision, out _));
        }

        /****************************************
        *          Validation / Copying
        ****************************************/

        [Test]
        public void ValidRejectsDuplicatedEquipmentAcrossSlots()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 3u);
            Assert.IsTrue(InventoryRules.IsValid(inventory));

            // 복구 데이터가 같은 장비를 두 칸에 담고 있으면 복제다
            inventory.SetSlot(4, InventorySlotSnapshot.FromEquipment(MakeItem(3u)));

            Assert.IsFalse(InventoryRules.IsValid(inventory));
        }

        [Test]
        public void ValidRejectsMalformedSlots()
        {
            var inventory = new InventorySnapshot();

            inventory.SetSlot(0, new InventorySlotSnapshot
            {
                Kind      = InventoryItemKind.Consumable,
                ItemId    = ShopItemKind.HealthPotion,
                Count     = 0, // 소모품인데 수량이 0이면 빈 칸이어야 한다
                Equipment = default,
            });

            Assert.IsFalse(InventoryRules.IsValid(inventory));
            Assert.IsFalse(InventoryRules.IsValid(null));
        }

        [Test]
        public void CloneDoesNotShareSlots()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 9u);

            InventorySnapshot copy = inventory.Clone();
            StoreEquipment(inventory, 10u);

            Assert.AreEqual(1, copy.UsedCount);
            Assert.AreEqual(2, inventory.UsedCount);
            Assert.AreNotEqual(inventory.Revision, copy.Revision);
        }

        [Test]
        public void FindEquipmentIgnoresBasicWeaponId()
        {
            var inventory = new InventorySnapshot();
            StoreEquipment(inventory, 4u);

            Assert.IsTrue(InventoryRules.TryFindEquipment(inventory, 4u, out int slot));
            Assert.AreEqual(0, slot);

            // 장착 무기가 기본 무기(0)일 때 인벤토리와 충돌한다고 판정하면 안 된다
            Assert.IsFalse(InventoryRules.TryFindEquipment(inventory, 0u, out _));
        }

        [Test]
        public void ResolveCarryLimitTreatsNegativeAsUnlimited()
        {
            Assert.AreEqual(int.MaxValue, InventoryRules.ResolveCarryLimit(UNLIMITED));
            Assert.AreEqual(5, InventoryRules.ResolveCarryLimit(5));
        }
    }
}
