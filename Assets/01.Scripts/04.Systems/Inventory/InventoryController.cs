// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 서버 인벤토리. 소유 클라이언트에게만 12칸을 바뀐 칸 단위로 보낸다 </summary>
    [DisallowMultipleComponent]
    public class InventoryController : NetworkBehaviour, IRunStateCarrier, IRunResettable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 전송은 바뀐 칸만 나가는 SyncList로, 서버 판정은 아래 _inventory 원본으로 한다
        private readonly SyncList<InventorySlotSnapshot> _slots = new SyncList<InventorySlotSnapshot>();

        [SyncVar] private uint _revision;

        /// <summary> 서버 원본. 규칙 계산과 carrier 캡처가 이 값을 본다 </summary>
        private readonly InventorySnapshot _inventory = new InventorySnapshot();

        /****************************************
        *              Properties
        ****************************************/

        public uint Revision     => _revision;
        public int  CarrierOrder => 12;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;
        }

        public override void OnStartServer()
        {
            ServerResetSlots();
        }

        /****************************************
        *              Server API
        ****************************************/

        // 거리, 생명, 비전투, 드랍 소유권 검증을 마친 서버 상호작용 처리기만 호출한다
        [Server]
        public InventoryResult ServerTryStoreEquipment(in WeaponCombatSnapshot item, uint expectedRevision)
        {
            InventoryResult result =
                InventoryRules.TryStoreEquipment(_inventory, item, expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        // carryLimit은 ShopSO.ItemEntry.CarryLimit을 그대로 넘긴다. -1이면 무제한이다
        [Server]
        public InventoryResult ServerTryStoreConsumable(ShopItemKind itemId, int amount, int carryLimit,
            uint expectedRevision)
        {
            InventoryResult result = InventoryRules.TryStoreConsumable(_inventory, itemId, amount, carryLimit,
                expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        /// <summary> 장착이나 양도로 장비를 꺼낸다. 꺼낸 값은 호출자가 받아 처리한다 </summary>
        [Server]
        public InventoryResult ServerTryTakeEquipment(int slot, uint instanceId, uint expectedRevision,
            out WeaponCombatSnapshot taken)
        {
            taken = default;

            InventoryResult result = InventoryRules.TryTakeEquipment(_inventory, slot, instanceId,
                expectedRevision, out InventoryWrite write);
            if (result != InventoryResult.Success)
            {
                return result;
            }

            // 칸을 비우기 전에 꺼낸 값을 확보한다
            taken = _inventory.GetSlot(slot).Equipment;
            return ApplyIfSuccess(result, write);
        }

        [Server]
        public InventoryResult ServerTryConsume(int slot, ShopItemKind expectedItem, int amount, uint expectedRevision)
        {
            InventoryResult result = InventoryRules.TryConsume(_inventory, slot, expectedItem, amount,
                expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        [Server]
        public void ResetForNewRun()
        {
            ServerResetSlots();
        }

        /****************************************
        *            Capture / Restore
        ****************************************/

        [Server]
        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            // 참조를 넘기면 이후 서버 변경이 그릇에 새어 들어간다
            snapshot.Inventory = _inventory.Clone();
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) || (snapshot.SchemaVersion != RunParticipantSnapshot.CURRENT_SCHEMA_VERSION) ||
                (!InventoryRules.IsValid(snapshot.Inventory)))
            {
                failure = "인벤토리 형식이나 칸 값이 유효하지 않다";
                return false;
            }

            if (InventoryRules.TryFindEquipment(snapshot.Inventory, snapshot.EquippedWeapon.InstanceId, out _))
            {
                failure = "같은 장비가 장착 무기와 인벤토리에 함께 있다";
                return false;
            }

            failure = null;
            return true;
        }

        [Server]
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (!Validate(snapshot, out _))
            {
                return false;
            }

            _inventory.CopyFrom(snapshot.Inventory);
            _revision = _inventory.Revision;

            EnsureSlotCount();
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                _slots[i] = _inventory.GetSlot(i);
            }

            return true;
        }

        /****************************************
        *               Internals
        ****************************************/

        [Server]
        private InventoryResult ApplyIfSuccess(InventoryResult result, in InventoryWrite write)
        {
            if (result != InventoryResult.Success)
            {
                return result;
            }

            _inventory.SetSlot(write.SlotIndex, write.Value);
            ++_inventory.Revision;

            _slots[write.SlotIndex] = write.Value;
            _revision = _inventory.Revision;

            return InventoryResult.Success;
        }

        [Server]
        private void ServerResetSlots()
        {
            _inventory.Clear();
            _revision = 0u;

            EnsureSlotCount();
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                _slots[i] = default;
            }
        }

        /// <summary> 칸 수는 런 내내 고정이다. 처음 한 번만 채우고 이후에는 값만 바꾼다 </summary>
        [Server]
        private void EnsureSlotCount()
        {
            if (_slots.Count == InventorySnapshot.SLOT_COUNT)
            {
                return;
            }

            _slots.Clear();
            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                _slots.Add(default);
            }
        }
    }
}
