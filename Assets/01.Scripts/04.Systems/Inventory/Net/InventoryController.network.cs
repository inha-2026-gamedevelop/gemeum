// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class InventoryController
    {
        /****************************************
        *                Fields
        ****************************************/

        // 전송은 바뀐 칸만 나가는 SyncList로, 서버 판정은 아래 _inventory 원본으로 한다
        private readonly SyncList<InventorySlotSnapshot> _slots = new SyncList<InventorySlotSnapshot>();

        [SyncVar] private uint _revision;

        // 현재 장비는 슬롯과 별도로 보관한다. 기본 무기도 ID 0 snapshot으로 확정한다
        [SyncVar(hook = nameof(OnEquippedWeaponChanged))]
        private WeaponCombatSnapshot _equippedWeapon;

        [SyncVar] private bool _hasEquippedWeapon;

        /****************************************
        *               Commands
        ****************************************/

        /// <summary> 서버 요청을 받아 장착하고 결과를 소유 클라이언트에 돌려준다 </summary>
        [Command]
        private void CmdRequestEquip(int slot, uint instanceId, uint expectedRevision, uint requestId)
        {
            if (connectionToClient == null)
            {
                return;
            }

            InventoryResult result = ServerTryEquip(netId, slot, instanceId, expectedRevision,
                requestId, out WeaponCombatSnapshot previousWeapon);
            TargetEquipResult(connectionToClient, requestId, result, _revision, previousWeapon);
        }

        [Command]
        private void CmdRequestUnequip(uint expectedRevision, uint requestId)
        {
            if (connectionToClient == null)
            {
                return;
            }

            InventoryResult result = ServerTryUnequip(netId, expectedRevision, requestId,
                out WeaponCombatSnapshot removedWeapon);
            TargetEquipResult(connectionToClient, requestId, result, _revision, removedWeapon);
        }

        /****************************************
        *               TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetEquipResult(NetworkConnection target, uint requestId, InventoryResult result,
            uint revision, WeaponCombatSnapshot previousWeapon)
        {
            EquipResult?.Invoke(requestId, result, revision);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnEquippedWeaponChanged(WeaponCombatSnapshot oldWeapon,
            WeaponCombatSnapshot newWeapon)
        {
            Action<WeaponCombatSnapshot> handlers = EquippedWeaponChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<WeaponCombatSnapshot>) observers[i])(newWeapon);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[InventoryController] 장착 장비 observer 실패: {e}", this);
                }
            }
        }
    }
}
