// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 서버 장비 보관함. 소유 클라이언트에게만 두 칸을 함께 전송한다 </summary>
    [DisallowMultipleComponent]
    public class InventoryController : NetworkBehaviour, IRunStateCarrier, IRunResettable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private EquipmentInventorySnapshot _inventory;

        /****************************************
        *              Properties
        ****************************************/

        public EquipmentInventorySnapshot Snapshot => _inventory;
        public int CarrierOrder => 12;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;
        }

        public override void OnStartServer()
        {
            _inventory = default;
        }

        /****************************************
        *              Server API
        ****************************************/

        // 거리, 생명, 비전투, 드랍 소유권 검증을 마친 서버 상호작용 처리기만 호출한다
        [Server]
        public InventoryResult ServerTryStore(in WeaponCombatSnapshot item, uint expectedRevision)
        {
            InventoryResult result = EquipmentInventoryRules.TryStore(_inventory, item, expectedRevision, out var next);
            if (result != InventoryResult.Success)
            {
                return result;
            }

            _inventory = next;
            return InventoryResult.Success;
        }

        [Server]
        public InventoryResult ServerTryRemove(int slot, uint instanceId, uint expectedRevision)
        {
            InventoryResult result = EquipmentInventoryRules.TryRemove(_inventory, slot, instanceId, expectedRevision, out var next);
            if (result != InventoryResult.Success)
            {
                return result;
            }

            _inventory = next;
            return InventoryResult.Success;
        }

        [Server]
        public void ResetForNewRun()
        {
            _inventory = default;
        }

        [Server]
        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.Inventory = _inventory;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) || (snapshot.SchemaVersion != RunParticipantSnapshot.CURRENT_SCHEMA_VERSION) ||
                (!EquipmentInventoryRules.IsValid(snapshot.Inventory)))
            {
                failure = "장비 보관함 형식이나 슬롯 값이 유효하지 않다";
                return false;
            }

            uint equippedId = snapshot.EquippedWeapon.InstanceId;
            if ((equippedId != 0u) &&
                ((snapshot.Inventory.Slot0.InstanceId == equippedId) || (snapshot.Inventory.Slot1.InstanceId == equippedId)))
            {
                failure = "같은 장비가 장착 무기와 보관함에 함께 있다";
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

            _inventory = snapshot.Inventory;
            return true;
        }
    }
}
