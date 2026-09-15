// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    public partial class InventoryController
    {
        /****************************************
        *              Unity Event
        ****************************************/

        public override void OnStartServer()
        {
            ServerResetSlots();
            TryBindGrowthParticipant();

            // NetworkBehaviour 실행 순서와 무관하게 TalentController의 초기 세대를 맞춘다.
            if ((_talentProvider != null) && (_talentProvider.Revision != 0u))
            {
                ServerRegisterTalentRevision(_talentProvider.Revision);
            }

            if ((_buffProvider != null) && (_buffProvider.Revision != 0u))
            {
                _lastBuffRevision = _buffProvider.Revision;
            }
        }

        public override void OnStopServer()
        {
            UnbindGrowthParticipant();
        }

        /****************************************
        *              Server API
        ****************************************/

        // 거리, 생명, 비전투, 드랍 소유권 검증을 마친 서버 상호작용 처리기만 호출한다
        [Server]
        public InventoryResult ServerTryStoreEquipment(in WeaponCombatSnapshot item, uint expectedRevision)
        {
            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

            if ((_serverHasEquippedWeapon) && item.HasEquipment &&
                (item.InstanceId == _serverEquippedWeapon.InstanceId))
            {
                return InventoryResult.DuplicateItem;
            }

            InventoryResult result =
                InventoryRules.TryStoreEquipment(_inventory, item, expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        // carryLimit은 ShopSO.ItemEntry.CarryLimit을 그대로 넘긴다. -1이면 무제한이다
        [Server]
        public InventoryResult ServerTryStoreConsumable(ShopItemKind itemId, int amount, int carryLimit,
            int maxStack, uint expectedRevision)
        {
            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

            InventoryResult result = InventoryRules.TryStoreConsumable(_inventory, itemId, amount,
                carryLimit, maxStack, expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        /// <summary> 장착이나 양도로 장비를 꺼낸다. 꺼낸 값은 호출자가 받아 처리한다 </summary>
        [Server]
        public InventoryResult ServerTryTakeEquipment(int slot, uint instanceId, uint expectedRevision,
            out WeaponCombatSnapshot taken)
        {
            taken = default;

            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

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

        /// <summary> 서버에서 장착 교환을 처리한다. 호출자 netId는 서버가 가진 플레이어와 같아야 한다 </summary>
        [Server]
        public InventoryResult ServerTryEquip(uint requesterNetId, int slot, uint instanceId,
            uint expectedRevision, uint requestId, out WeaponCombatSnapshot previousWeapon)
        {
            previousWeapon = default;

            if (!IsOwnerRequest(requesterNetId))
            {
                return InventoryResult.InvalidRequest;
            }

            if (requestId == 0u)
            {
                return InventoryResult.InvalidRequest;
            }

            if (TryGetCachedEquipResult(requestId, slot, instanceId, expectedRevision,
                    out InventoryResult cachedResult, out previousWeapon))
            {
                return cachedResult;
            }

            InventoryResult stateResult = ValidateEquipState();
            if (stateResult != InventoryResult.Success)
            {
                RememberEquipResult(requestId, slot, instanceId, expectedRevision,
                    stateResult, previousWeapon);
                return stateResult;
            }

            InventoryResult revisionResult = ValidateInventoryRevision(expectedRevision);
            if (revisionResult != InventoryResult.Success)
            {
                RememberEquipResult(requestId, slot, instanceId, expectedRevision,
                    revisionResult, previousWeapon);
                return revisionResult;
            }

            if (!ServerTryResolveEquippedWeapon(out WeaponCombatSnapshot currentWeapon))
            {
                InventoryResult result = InventoryResult.InvalidState;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision, result, previousWeapon);
                return result;
            }

            InventoryResult swapResult = InventoryRules.TrySwapEquipment(
                _inventory, slot, instanceId, currentWeapon, expectedRevision,
                out EquipmentSwapWrite write);

            if (swapResult != InventoryResult.Success)
            {
                RememberEquipResult(requestId, slot, instanceId, expectedRevision, swapResult, previousWeapon);
                return swapResult;
            }

            if (_loadoutRevision == uint.MaxValue)
            {
                InventoryResult result = InventoryResult.RevisionExhausted;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision, result, previousWeapon);
                return result;
            }

            var inventoryPlan = new InventoryCommitPlan(expectedRevision,
                new InventoryWrite(write.SlotIndex, write.SlotValue));
            if (!ServerTryAcquireTransaction(inventoryPlan))
            {
                InventoryResult result = InventoryResult.StaleRevision;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision, result, previousWeapon);
                return result;
            }

            if ((_weaponTarget == null) ||
                (!_weaponTarget.ServerTryAcquireEquippedWeapon(write.NextWeapon,
                    out EquipmentWeaponWrite weaponPlan)))
            {
                ServerReleaseTransaction();
                InventoryResult result = InventoryResult.InvalidState;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision,
                    result, previousWeapon);
                return result;
            }

            if ((_talentProvider == null) || (_talentProvider.Revision == 0u) ||
                (_talentProvider.Revision != _lastTalentRevision) ||
                (!_talentProvider.ServerCanResetForWeapon(write.NextWeapon.WeaponKind)))
            {
                _weaponTarget.ServerReleaseEquippedWeapon();
                ServerReleaseTransaction();
                InventoryResult result = InventoryResult.InvalidState;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision,
                    result, previousWeapon);
                return result;
            }

            try
            {
                ServerApplyTransaction(inventoryPlan);
                ServerApplyEquipmentSwapSource(write.NextWeapon);
                _weaponTarget.ServerApplyEquippedWeapon(weaponPlan);
                _talentProvider.ServerApplyWeaponReset(write.NextWeapon.WeaponKind);
                _lastTalentRevision = _talentProvider.Revision;

                previousWeapon = write.PreviousWeapon;
                RememberEquipResult(requestId, slot, instanceId, expectedRevision,
                    InventoryResult.Success, previousWeapon);

                TryPublishWeaponTarget(weaponPlan);
                ServerPublishEquippedWeapon();
                ServerPublishTransaction(inventoryPlan);
                _talentProvider.ServerPublishAllocation();
                return InventoryResult.Success;
            }
            finally
            {
                _weaponTarget.ServerReleaseEquippedWeapon();
                ServerReleaseTransaction();
            }
        }

        /// <summary> 서버에서 장착 교환을 처리한다. 플레이어 오브젝트 내부 호출용 편의 API </summary>
        [Server]
        public InventoryResult ServerTryEquip(int slot, uint instanceId, uint expectedRevision,
            out WeaponCombatSnapshot previousWeapon)
        {
            uint requestId = TakeNextEquipRequestId();
            return ServerTryEquip(netId, slot, instanceId, expectedRevision, requestId, out previousWeapon);
        }

        /// <summary> 서버에서 장착 교환을 처리한다. 결과가 필요 없는 호출용 편의 API </summary>
        [Server]
        public InventoryResult ServerTryEquip(int slot, uint instanceId, uint expectedRevision)
        {
            return ServerTryEquip(slot, instanceId, expectedRevision, out _);
        }

        /// <summary> 장착 교환의 이름을 명시한 별칭. 실제 처리는 ServerTryEquip과 같다 </summary>
        [Server]
        public InventoryResult ServerTrySwapEquipment(uint requesterNetId, int slot, uint instanceId,
            uint expectedRevision, uint requestId, out WeaponCombatSnapshot previousWeapon)
        {
            return ServerTryEquip(requesterNetId, slot, instanceId, expectedRevision,
                requestId, out previousWeapon);
        }

        /// <summary> 서버에서 장착 장비를 기본 무기로 되돌리고 빈 슬롯에 보관한다 </summary>
        [Server]
        public InventoryResult ServerTryUnequip(uint requesterNetId, uint expectedRevision,
            uint requestId, out WeaponCombatSnapshot removedWeapon)
        {
            removedWeapon = default;

            if (!IsOwnerRequest(requesterNetId) || (requestId == 0u))
            {
                return InventoryResult.InvalidRequest;
            }

            if (TryGetCachedUnequipResult(requestId, expectedRevision, out InventoryResult cachedResult,
                    out removedWeapon))
            {
                return cachedResult;
            }

            InventoryResult stateResult = ValidateEquipState();
            if (stateResult != InventoryResult.Success)
            {
                RememberUnequipResult(requestId, expectedRevision, stateResult, removedWeapon);
                return stateResult;
            }

            InventoryResult revisionResult = ValidateInventoryRevision(expectedRevision);
            if (revisionResult != InventoryResult.Success)
            {
                RememberUnequipResult(requestId, expectedRevision, revisionResult, removedWeapon);
                return revisionResult;
            }

            if (!ServerTryResolveEquippedWeapon(out WeaponCombatSnapshot currentWeapon))
            {
                InventoryResult invalidStateResult = InventoryResult.InvalidState;
                RememberUnequipResult(requestId, expectedRevision, invalidStateResult, removedWeapon);
                return invalidStateResult;
            }

            if ((_weaponTarget == null) ||
                (!TryBuildBasicWeapon(_weaponTarget.Weapon, out WeaponCombatSnapshot basicWeapon)))
            {
                InventoryResult invalidStateResult = InventoryResult.InvalidState;
                RememberUnequipResult(requestId, expectedRevision, invalidStateResult, removedWeapon);
                return invalidStateResult;
            }

            InventoryResult unequipResult = InventoryRules.TryUnequipEquipment(
                _inventory, currentWeapon, basicWeapon, expectedRevision,
                out EquipmentSwapWrite write);
            if (unequipResult != InventoryResult.Success)
            {
                RememberUnequipResult(requestId, expectedRevision, unequipResult, removedWeapon);
                return unequipResult;
            }

            if (_loadoutRevision == uint.MaxValue)
            {
                unequipResult = InventoryResult.RevisionExhausted;
                RememberUnequipResult(requestId, expectedRevision, unequipResult, removedWeapon);
                return unequipResult;
            }

            var inventoryPlan = new InventoryCommitPlan(expectedRevision,
                new InventoryWrite(write.SlotIndex, write.SlotValue));
            if (!ServerTryAcquireTransaction(inventoryPlan))
            {
                unequipResult = InventoryResult.StaleRevision;
                RememberUnequipResult(requestId, expectedRevision, unequipResult, removedWeapon);
                return unequipResult;
            }

            if (!_weaponTarget.ServerTryAcquireEquippedWeapon(write.NextWeapon,
                    out EquipmentWeaponWrite weaponPlan))
            {
                ServerReleaseTransaction();
                unequipResult = InventoryResult.InvalidState;
                RememberUnequipResult(requestId, expectedRevision, unequipResult, removedWeapon);
                return unequipResult;
            }

            if ((_talentProvider == null) || (_talentProvider.Revision == 0u) ||
                (_talentProvider.Revision != _lastTalentRevision) ||
                (!_talentProvider.ServerCanResetForWeapon(write.NextWeapon.WeaponKind)))
            {
                _weaponTarget.ServerReleaseEquippedWeapon();
                ServerReleaseTransaction();
                unequipResult = InventoryResult.InvalidState;
                RememberUnequipResult(requestId, expectedRevision, unequipResult, removedWeapon);
                return unequipResult;
            }

            try
            {
                ServerApplyTransaction(inventoryPlan);
                ServerApplyEquipmentSwapSource(write.NextWeapon);
                _weaponTarget.ServerApplyEquippedWeapon(weaponPlan);
                _talentProvider.ServerApplyWeaponReset(write.NextWeapon.WeaponKind);
                _lastTalentRevision = _talentProvider.Revision;

                removedWeapon = write.PreviousWeapon;
                RememberUnequipResult(requestId, expectedRevision,
                    InventoryResult.Success, removedWeapon);

                TryPublishWeaponTarget(weaponPlan);
                ServerPublishEquippedWeapon();
                ServerPublishTransaction(inventoryPlan);
                _talentProvider.ServerPublishAllocation();
                return InventoryResult.Success;
            }
            finally
            {
                _weaponTarget.ServerReleaseEquippedWeapon();
                ServerReleaseTransaction();
            }
        }

        /// <summary> 서버에서 장착 해제를 처리한다. 플레이어 오브젝트 내부 호출용 편의 API </summary>
        [Server]
        public InventoryResult ServerTryUnequip(uint expectedRevision, out WeaponCombatSnapshot removedWeapon)
        {
            uint requestId = TakeNextEquipRequestId();
            return ServerTryUnequip(netId, expectedRevision, requestId, out removedWeapon);
        }

        [Server]
        public InventoryResult ServerTryConsume(int slot, ShopItemKind expectedItem, int amount, uint expectedRevision)
        {
            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

            InventoryResult result = InventoryRules.TryConsume(_inventory, slot, expectedItem, amount,
                expectedRevision, out InventoryWrite write);
            return ApplyIfSuccess(result, write);
        }

        /// <summary> 거래 조정자가 현재 장비를 확인한다. 원본은 바꾸지 않는다 </summary>
        [Server]
        internal InventoryResult ServerTryGetEquipment(int slot, uint instanceId,
            uint expectedRevision, out WeaponCombatSnapshot equipment)
        {
            equipment = default;

            InventoryResult result = InventoryRules.TryTakeEquipment(_inventory, slot,
                instanceId, expectedRevision, out _);
            if (result != InventoryResult.Success)
            {
                return result;
            }

            equipment = _inventory.GetSlot(slot).Equipment;
            return InventoryResult.Success;
        }

        /// <summary> 거래 조정자가 장착 중인 실제 장비를 확인한다. 기본 무기는 거부한다 </summary>
        [Server]
        internal InventoryResult ServerTryGetEquippedEquipment(uint instanceId,
            uint expectedInventoryRevision, out WeaponCombatSnapshot equipment)
        {
            equipment = default;

            InventoryResult revisionResult = ValidateInventoryRevision(expectedInventoryRevision);
            if (revisionResult != InventoryResult.Success)
            {
                return revisionResult;
            }

            if ((!_serverHasEquippedWeapon) || (!_serverEquippedWeapon.HasEquipment))
            {
                return InventoryResult.NoEquippedWeapon;
            }

            if ((instanceId == 0u) || (_serverEquippedWeapon.InstanceId != instanceId))
            {
                return InventoryResult.ItemChanged;
            }

            equipment = _serverEquippedWeapon;
            return InventoryResult.Success;
        }

        /// <summary> 소모품 소비 계획을 만들고 원본은 바꾸지 않는다 </summary>
        [Server]
        internal InventoryResult ServerPrepareConsume(int slot, ShopItemKind expectedItem,
            int amount, uint expectedRevision, out InventoryCommitPlan plan)
        {
            plan = default;
            InventoryResult result = InventoryRules.TryConsume(_inventory, slot, expectedItem,
                amount, expectedRevision, out InventoryWrite write);
            if (result == InventoryResult.Success)
            {
                plan = new InventoryCommitPlan(expectedRevision, write);
            }

            return result;
        }

        /// <summary> 소모품 저장 계획을 만들고 원본은 바꾸지 않는다 </summary>
        [Server]
        internal InventoryResult ServerPrepareStoreConsumable(ShopItemKind itemId, int amount,
            int carryLimit, int maxStack, uint expectedRevision, out InventoryCommitPlan plan)
        {
            plan = default;
            InventoryResult result = InventoryRules.TryStoreConsumable(_inventory, itemId, amount,
                carryLimit, maxStack, expectedRevision, out InventoryWrite write);
            if (result == InventoryResult.Success)
            {
                plan = new InventoryCommitPlan(expectedRevision, write);
            }

            return result;
        }

        /// <summary> 장비 저장 계획을 만들고 원본은 바꾸지 않는다 </summary>
        [Server]
        internal InventoryResult ServerPrepareStoreEquipment(in WeaponCombatSnapshot equipment,
            uint expectedRevision, out InventoryCommitPlan plan)
        {
            plan = default;

            if ((_serverHasEquippedWeapon) && equipment.HasEquipment &&
                (_serverEquippedWeapon.InstanceId == equipment.InstanceId))
            {
                return InventoryResult.DuplicateItem;
            }

            InventoryResult result = InventoryRules.TryStoreEquipment(_inventory, equipment,
                expectedRevision, out InventoryWrite write);
            if (result == InventoryResult.Success)
            {
                plan = new InventoryCommitPlan(expectedRevision, write);
            }

            return result;
        }

        /// <summary> 장비 반출 계획을 만들고 원본은 바꾸지 않는다 </summary>
        [Server]
        internal InventoryResult ServerPrepareTakeEquipment(int slot, uint instanceId,
            uint expectedRevision, out WeaponCombatSnapshot equipment, out InventoryCommitPlan plan)
        {
            equipment = default;
            plan      = default;

            InventoryResult result = InventoryRules.TryTakeEquipment(_inventory, slot,
                instanceId, expectedRevision, out InventoryWrite write);
            if (result != InventoryResult.Success)
            {
                return result;
            }

            equipment = _inventory.GetSlot(slot).Equipment;
            plan      = new InventoryCommitPlan(expectedRevision, write);
            return InventoryResult.Success;
        }

        /// <summary> 티켓 하나와 장비 재생성 결과를 같은 revision에서 묶는다 </summary>
        [Server]
        internal InventoryResult ServerPrepareTicketUse(int ticketSlot, ShopItemKind ticketKind,
            int equipmentSlot, uint instanceId, in WeaponCombatSnapshot replacement,
            uint expectedRevision, out InventoryCommitPlan plan)
        {
            plan = default;

            InventoryResult consumeResult = InventoryRules.TryConsume(_inventory, ticketSlot,
                ticketKind, 1, expectedRevision, out InventoryWrite consumeWrite);
            if (consumeResult != InventoryResult.Success)
            {
                return consumeResult;
            }

            InventoryResult replaceResult = InventoryRules.TryReplaceEquipment(_inventory,
                equipmentSlot, instanceId, replacement, expectedRevision,
                out InventoryWrite replaceWrite);
            if (replaceResult != InventoryResult.Success)
            {
                return replaceResult;
            }

            if (ticketSlot == equipmentSlot)
            {
                return InventoryResult.InvalidRequest;
            }

            plan = new InventoryCommitPlan(expectedRevision, consumeWrite, replaceWrite);
            return InventoryResult.Success;
        }

        /// <summary> 티켓 소비와 장착 장비 수치 갱신 계획을 함께 만든다 </summary>
        [Server]
        internal InventoryResult ServerPrepareEquippedTicketUse(int ticketSlot,
            ShopItemKind ticketKind, uint instanceId, in WeaponCombatSnapshot replacement,
            uint expectedInventoryRevision, out InventoryCommitPlan inventoryPlan,
            out EquippedEquipmentWrite equippedPlan)
        {
            inventoryPlan = default;
            equippedPlan  = default;

            InventoryResult queryResult = ServerTryGetEquippedEquipment(instanceId,
                expectedInventoryRevision, out WeaponCombatSnapshot current);
            if (queryResult != InventoryResult.Success)
            {
                return queryResult;
            }

            if ((!InventoryRules.IsValidItem(replacement)) ||
                (replacement.InstanceId != current.InstanceId) ||
                (replacement.WeaponKind != current.WeaponKind))
            {
                return InventoryResult.InvalidItem;
            }

            if (_loadoutRevision == uint.MaxValue)
            {
                return InventoryResult.RevisionExhausted;
            }

            InventoryResult consumeResult = InventoryRules.TryConsume(_inventory, ticketSlot,
                ticketKind, 1, expectedInventoryRevision, out InventoryWrite consumeWrite);
            if (consumeResult != InventoryResult.Success)
            {
                return consumeResult;
            }

            inventoryPlan = new InventoryCommitPlan(expectedInventoryRevision, consumeWrite);
            equippedPlan = new EquippedEquipmentWrite(_loadoutRevision, current, replacement);
            return InventoryResult.Success;
        }

        /// <summary> 계획이 여전히 현재 원본에 적용 가능한지 확인한다 </summary>
        [Server]
        internal bool ServerCanApplyTransaction(in InventoryCommitPlan plan)
        {
            if (_transactionLocked || (!plan.IsValid) ||
                (_inventory.Revision != plan.ExpectedRevision) ||
                (_revision != plan.ExpectedRevision) || (plan.ExpectedRevision == uint.MaxValue))
            {
                return false;
            }

            if ((!InventorySnapshot.IsValidIndex(plan.First.SlotIndex)) ||
                ((plan.WriteCount == 2) && (!InventorySnapshot.IsValidIndex(plan.Second.SlotIndex))))
            {
                return false;
            }

            return true;
        }

        /// <summary> 커밋부터 모든 통지가 끝날 때까지 다른 인벤토리 변경을 막는다 </summary>
        [Server]
        internal bool ServerTryAcquireTransaction(in InventoryCommitPlan plan)
        {
            if (!ServerCanApplyTransaction(plan))
            {
                return false;
            }

            _transactionLocked = true;
            return true;
        }

        [Server]
        internal void ServerReleaseTransaction()
        {
            _transactionLocked = false;
        }

        /// <summary> 사전 검증된 계획을 서버 원본에만 반영한다 </summary>
        [Server]
        internal void ServerApplyTransaction(in InventoryCommitPlan plan)
        {
            _inventory.SetSlot(plan.First.SlotIndex, plan.First.Value);
            if (plan.WriteCount == 2)
            {
                _inventory.SetSlot(plan.Second.SlotIndex, plan.Second.Value);
            }

            _inventory.Revision = plan.ExpectedRevision + 1u;
        }

        /// <summary> 원본 일괄 반영이 끝난 뒤 SyncList와 revision을 공개한다 </summary>
        [Server]
        internal void ServerPublishTransaction(in InventoryCommitPlan plan)
        {
            ServerPublishSlot(plan.First);
            if (plan.WriteCount == 2)
            {
                ServerPublishSlot(plan.Second);
            }

            _revision = _inventory.Revision;
        }

        private void ServerPublishSlot(in InventoryWrite write)
        {
            try
            {
                _slots[write.SlotIndex] = write.Value;
            }
            catch (Exception e)
            {
                // SyncList는 값을 바꾼 뒤 observer를 호출한다. 한 observer의 실패가
                // 다음 칸과 revision 공개를 끊지 않도록 슬롯 단위로 격리한다.
                Debug.LogError($"[InventoryController] {write.SlotIndex}번 슬롯 observer 실패: {e}",
                    this);
            }
        }

        /// <summary> 장착 장비 계획이 생성 뒤 바뀌지 않았는지 검사한다 </summary>
        [Server]
        internal bool ServerCanApplyEquippedTransaction(in EquippedEquipmentWrite plan)
        {
            return (plan.IsValid && (_loadoutRevision == plan.ExpectedLoadoutRevision) &&
                _serverEquippedWeapon.Equals(plan.Previous));
        }

        /// <summary> 인벤토리 원본 변경과 같은 커밋 구간에서 장착 수치를 갱신한다 </summary>
        [Server]
        internal void ServerApplyEquippedTransaction(in EquippedEquipmentWrite plan)
        {
            _serverHasEquippedWeapon = true;
            ++_loadoutRevision;
            _serverEquippedWeapon = plan.Next;
        }

        /// <summary> 장착 티켓 원본과 결과 캐시가 확정된 뒤 SyncVar를 공개한다 </summary>
        [Server]
        internal void ServerPublishEquippedTransaction(in EquippedEquipmentWrite plan)
        {
            ServerPublishEquippedWeapon();
        }

        /****************************************
        *       Combat Loadout Revision API
        ****************************************/

        /// <summary> 특성 컴포넌트의 현재 세대를 초기 등록한다 </summary>
        [Server]
        public bool ServerRegisterTalentRevision(uint revision)
        {
            if (_transactionLocked || (revision == 0u))
            {
                return false;
            }

            if (_lastTalentRevision == revision)
            {
                return true;
            }

            if (_lastTalentRevision != 0u)
            {
                return false;
            }

            _lastTalentRevision = revision;
            return true;
        }

        /// <summary> 특성 원본 커밋보다 먼저 loadout 세대를 무통지로 예약한다 </summary>
        [Server]
        public bool ServerTryCommitTalentRevision(uint previousRevision,
            uint nextRevision)
        {
            if (_transactionLocked || (previousRevision == 0u) ||
                (previousRevision == uint.MaxValue) ||
                (nextRevision != (previousRevision + 1u)) ||
                (_lastTalentRevision != previousRevision))
            {
                return false;
            }

            // 특성 UI가 첫 공격보다 먼저 열려도 기본 무기 loadout을 여기서 확정한다.
            if ((_loadoutRevision == 0u) && (!ServerEnsureEquippedWeapon()))
            {
                return false;
            }

            if (_loadoutRevision == uint.MaxValue)
            {
                return false;
            }

            _lastTalentRevision = nextRevision;
            ++_loadoutRevision;
            return true;
        }

        [Server]
        public void ResetForNewRun()
        {
            if (_transactionLocked)
            {
                Debug.LogError("[InventoryController] 거래 통지 중 런 초기화를 시도했다", this);
                return;
            }

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

            if (_serverHasEquippedWeapon)
            {
                snapshot.EquippedWeapon = _serverEquippedWeapon;
            }
            else if ((_weaponTarget != null) && _weaponTarget.HasWeapon &&
                TryBuildBasicWeapon(_weaponTarget.Weapon, out WeaponCombatSnapshot basicWeapon))
            {
                snapshot.EquippedWeapon = basicWeapon;
            }
            else
            {
                snapshot.EquippedWeapon = default;
            }
        }

        [Server]
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (_transactionLocked)
            {
                return false;
            }

            if (!Validate(snapshot, out _))
            {
                return false;
            }

            // 무기가 없는 상태로 담긴 snapshot은 역할 무기와 맞출 대상이 없다.
            // 이때 무기 일치를 요구하면 로비 전환이 항상 실패한다
            bool isUnarmed = IsUnarmedSnapshot(snapshot.EquippedWeapon);

            if ((!isUnarmed) && (_weaponTarget != null) && (!_weaponTarget.HasWeapon ||
                (_weaponTarget.Weapon != snapshot.EquippedWeapon.WeaponKind)))
            {
                return false;
            }

            TryBindGrowthParticipant(snapshot.Participant);

            _inventory.CopyFrom(snapshot.Inventory);
            _revision = _inventory.Revision;

            _serverEquippedWeapon    = snapshot.EquippedWeapon;
            _serverHasEquippedWeapon = !isUnarmed;
            _equippedWeapon          = _serverEquippedWeapon;
            _hasEquippedWeapon       = _serverHasEquippedWeapon;
            _loadoutRevision   = 1u;
            _lastGrowthRevision = ResolveGrowthRevision(snapshot.Participant);
            _lastTalentRevision = snapshot.TalentAllocation.Revision;
            _lastBuffRevision = (_buffProvider == null) ? 0u : _buffProvider.Revision;

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

            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

            var plan = new InventoryCommitPlan(_inventory.Revision, write);
            if (!ServerCanApplyTransaction(plan))
            {
                return InventoryResult.StaleRevision;
            }

            ServerApplyTransaction(plan);
            ServerPublishTransaction(plan);

            return InventoryResult.Success;
        }

        [Server]
        private void ServerApplyEquipmentSwapSource(in WeaponCombatSnapshot nextWeapon)
        {
            if ((!_serverHasEquippedWeapon) && (_loadoutRevision == 0u))
            {
                // The basic weapon is loadout revision one even when the first equip request
                // replaces it before it has ever been published.
                _loadoutRevision = 1u;
            }

            _serverEquippedWeapon    = nextWeapon;
            _serverHasEquippedWeapon = true;
            ++_loadoutRevision;
        }

        [Server]
        private void ServerPublishEquippedWeapon()
        {
            try
            {
                _hasEquippedWeapon = _serverHasEquippedWeapon;
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventoryController] 장착 보유 상태 공개 실패: {e}", this);
            }

            try
            {
                _equippedWeapon = _serverEquippedWeapon;
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventoryController] 장착 snapshot 공개 실패: {e}", this);
            }
        }

        [Server]
        private void TryPublishWeaponTarget(in EquipmentWeaponWrite plan)
        {
            try
            {
                _weaponTarget.ServerPublishEquippedWeapon(plan);
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventoryController] 역할 무기 공개 실패: {e}", this);
            }
        }

        [Server]
        private bool ServerTryResolveEquippedWeapon(out WeaponCombatSnapshot weapon)
        {
            weapon = default;
            if (_weaponTarget == null)
            {
                return false;
            }

            if (_serverHasEquippedWeapon)
            {
                if ((!_weaponTarget.HasWeapon) ||
                    (_weaponTarget.Weapon != _serverEquippedWeapon.WeaponKind) ||
                    (!InventoryRules.IsValidEquippedWeapon(_serverEquippedWeapon)))
                {
                    return false;
                }

                weapon = _serverEquippedWeapon;
                return true;
            }

            return (_weaponTarget.HasWeapon &&
                TryBuildBasicWeapon(_weaponTarget.Weapon, out weapon));
        }

        [Server]
        private bool ServerEnsureEquippedWeapon()
        {
            if (!ServerTryResolveEquippedWeapon(out WeaponCombatSnapshot weapon))
            {
                return false;
            }

            if (_serverHasEquippedWeapon)
            {
                return true;
            }

            _serverEquippedWeapon    = weapon;
            _serverHasEquippedWeapon = true;
            if (_loadoutRevision == 0u)
            {
                _loadoutRevision = 1u;
            }

            ServerPublishEquippedWeapon();
            return true;
        }

        private static bool TryBuildBasicWeapon(WeaponKind weapon, out WeaponCombatSnapshot snapshot)
        {
            snapshot = default;

            if ((!GameDB.IsInitialized) || (!Enum.IsDefined(typeof(WeaponKind), weapon)))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.Combat;
            if (combat == null)
            {
                return false;
            }

            float attackPower;
            switch (weapon.ToReach())
            {
                case AttackReach.Melee:
                    attackPower = combat.MeleeBasePower;
                    break;
                case AttackReach.Ranged:
                    attackPower = combat.RangedBasePower;
                    break;
                case AttackReach.Area:
                    attackPower = combat.BellBasePower;
                    break;
                default:
                    return false;
            }

            snapshot = new WeaponCombatSnapshot
            {
                InstanceId   = 0u,
                WeaponKind   = weapon,
                Grade        = ItemGrade.Common,
                AttackPower  = attackPower,
                Option0Kind  = EquipmentOptionKind.None,
                Option0Value = 0f,
                Option1Kind  = EquipmentOptionKind.None,
                Option1Value = 0f,
            };

            return InventoryRules.IsValidEquippedWeapon(snapshot);
        }

        private InventoryResult ValidateEquipState()
        {
            if (_transactionLocked)
            {
                return InventoryResult.InvalidState;
            }

            if (!GameDB.IsInitialized)
            {
                return InventoryResult.InvalidState;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result))
            {
                return InventoryResult.InvalidState;
            }

            if (run.IsPaused || RunSceneDirector.IsServerTransitioning)
            {
                return InventoryResult.InvalidState;
            }

            CombatConfigSO combat = GameDB.Combat;
            if ((combat == null) || CombatClock.IsInCombat(netId,
                    RunTimeLocator.Current.GameplayElapsedSeconds,
                    combat.InteractOutOfCombatSeconds))
            {
                return InventoryResult.InvalidState;
            }

            if ((_damageable == null) || _damageable.IsDead)
            {
                return InventoryResult.InvalidState;
            }

            return InventoryResult.Success;
        }

        private InventoryResult ValidateInventoryRevision(uint expectedRevision)
        {
            if (_revision != expectedRevision)
            {
                return InventoryResult.StaleRevision;
            }

            if ((_revision == uint.MaxValue) || (_inventory.Revision == uint.MaxValue))
            {
                return InventoryResult.RevisionExhausted;
            }

            return InventoryResult.Success;
        }

        private bool IsOwnerRequest(uint requesterNetId)
        {
            if ((requesterNetId == 0u) || (netId == 0u) || (requesterNetId != netId))
            {
                return false;
            }

            if (!NetworkServer.spawned.TryGetValue(requesterNetId, out NetworkIdentity identity))
            {
                return false;
            }

            return (identity == netIdentity);
        }

        private bool TryGetCachedEquipResult(uint requestId, int slot, uint instanceId,
            uint expectedRevision, out InventoryResult result, out WeaponCombatSnapshot previousWeapon)
        {
            result = InventoryResult.Success;
            previousWeapon = default;

            if (!_hasLastEquipRequest)
            {
                return false;
            }

            if (requestId == _lastEquipRequestId)
            {
                if ((_lastEquipRequestKind != EquipmentRequestKind.Equip) ||
                    (_lastEquipSlot != slot) || (_lastEquipInstanceId != instanceId) ||
                    (_lastEquipExpectedRevision != expectedRevision))
                {
                    result = InventoryResult.DuplicateRequest;
                    return true;
                }

                result = _lastEquipResult;
                previousWeapon = _lastEquipPreviousWeapon;
                return true;
            }

            if (!IsNewerRequest(requestId, _lastEquipRequestId))
            {
                result = InventoryResult.DuplicateRequest;
                return true;
            }

            return false;
        }

        private void RememberEquipResult(uint requestId, int slot, uint instanceId,
            uint expectedRevision, InventoryResult result, in WeaponCombatSnapshot previousWeapon)
        {
            _hasLastEquipRequest       = true;
            _lastEquipRequestKind      = EquipmentRequestKind.Equip;
            _lastEquipRequestId        = requestId;
            _lastEquipSlot             = slot;
            _lastEquipInstanceId       = instanceId;
            _lastEquipExpectedRevision = expectedRevision;
            _lastEquipResult           = result;
            _lastEquipPreviousWeapon   = previousWeapon;
        }

        private bool TryGetCachedUnequipResult(uint requestId, uint expectedRevision,
            out InventoryResult result, out WeaponCombatSnapshot removedWeapon)
        {
            result = InventoryResult.Success;
            removedWeapon = default;

            if (!_hasLastEquipRequest)
            {
                return false;
            }

            if (requestId == _lastEquipRequestId)
            {
                if ((_lastEquipRequestKind != EquipmentRequestKind.Unequip) ||
                    (_lastEquipExpectedRevision != expectedRevision))
                {
                    result = InventoryResult.DuplicateRequest;
                    return true;
                }

                result = _lastEquipResult;
                removedWeapon = _lastEquipPreviousWeapon;
                return true;
            }

            if (!IsNewerRequest(requestId, _lastEquipRequestId))
            {
                result = InventoryResult.DuplicateRequest;
                return true;
            }

            return false;
        }

        private void RememberUnequipResult(uint requestId, uint expectedRevision,
            InventoryResult result, in WeaponCombatSnapshot removedWeapon)
        {
            _hasLastEquipRequest       = true;
            _lastEquipRequestKind      = EquipmentRequestKind.Unequip;
            _lastEquipRequestId        = requestId;
            _lastEquipSlot             = -1;
            _lastEquipInstanceId       = 0u;
            _lastEquipExpectedRevision = expectedRevision;
            _lastEquipResult           = result;
            _lastEquipPreviousWeapon   = removedWeapon;
        }

        private static bool IsNewerRequest(uint requestId, uint previousRequestId)
        {
            uint delta = requestId - previousRequestId;
            return ((delta != 0u) && (delta < REQUEST_SERIAL_HALF_RANGE));
        }

        [Server]
        private void ServerResetSlots()
        {
            _inventory.Clear();
            _revision = 0u;
            _serverEquippedWeapon    = default;
            _serverHasEquippedWeapon = false;
            _equippedWeapon          = default;
            _hasEquippedWeapon       = false;
            _loadoutRevision   = 0u;
            _lastGrowthRevision = 0u;
            _lastTalentRevision = 0u;
            _lastBuffRevision = 0u;
            _transactionLocked = false;

            _hasLastEquipRequest = false;
            _lastEquipRequestKind = EquipmentRequestKind.Equip;
            _lastEquipRequestId  = 0u;
            _nextEquipRequestId  = 1u;

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

        private uint ResolveGrowthRevision(ParticipantId participant)
        {
            if (GrowthProviderLocator.HasProvider &&
                GrowthProviderLocator.Current.TryGetState(participant, out GrowthState providerGrowth))
            {
                return providerGrowth.Revision;
            }

            if (GrowthParticipantLocator.TryGet(participant, out IGrowthParticipant localParticipant))
            {
                return localParticipant.Growth.Revision;
            }

            if ((_growthParticipant != null) &&
                (_growthParticipant.Participant == participant))
            {
                return _growthParticipant.Growth.Revision;
            }

            return 1u;
        }

        private void TryBindGrowthParticipant()
        {
            BindGrowthParticipant(_growthParticipant);
        }
    }
}
