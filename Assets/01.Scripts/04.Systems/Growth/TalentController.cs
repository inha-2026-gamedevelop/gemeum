// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 한 참가자의 특성 배분과 무료 초기화를 서버에서 소유한다 </summary>
    [DisallowMultipleComponent]
    public partial class TalentController : NetworkBehaviour, ITalentAllocationProvider,
        IRunStateCarrier, IRunResettable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버 원본과 공개 SyncList(_nodeIds, Net/TalentController.network.cs)를 분리한다. 장비 거래 원본이 모두 확정되기 전에
        // SyncList callback이 실행되어 절반짜리 loadout을 관찰하지 않게 한다
        private readonly List<int>     _serverNodeIds = new List<int>();

        private IEquipmentWeaponTarget      _weaponTarget;
        private ICombatLoadoutRevisionTarget _loadoutRevisionTarget;
        private IGrowthParticipant          _growthParticipant;
        private bool                        _publishPending;

        /****************************************
        *              Properties
        ****************************************/

        public uint Revision => _revision;

        public int CarrierOrder => 13;

        public int AvailablePoints
        {
            get
            {
                int total = (_growthParticipant == null) ? 0 :
                    _growthParticipant.Growth.TalentPoints;
                return Mathf.Max(0, total - _spentPoints);
            }
        }

        /****************************************
        *                Events
        ****************************************/

        public event Action<uint> RevisionChanged;
        public event Action<TalentAllocationResult> AllocationResult;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;

            _weaponTarget          = GetComponent<IEquipmentWeaponTarget>();
            _loadoutRevisionTarget = GetComponent<ICombatLoadoutRevisionTarget>();
            _growthParticipant     = GetComponent<IGrowthParticipant>();

            if (_weaponTarget != null)
            {
                _weaponTarget.WeaponChanged += HandleWeaponChanged;
            }
        }

        private void OnDestroy()
        {
            if (_weaponTarget != null)
            {
                _weaponTarget.WeaponChanged -= HandleWeaponChanged;
            }
        }

        /****************************************
        *              Client API
        ****************************************/

        public void RequestAllocate(int nodeId)
        {
            if (!isOwned)
            {
                return;
            }

            CmdAllocate(nodeId, _revision);
        }

        public void RequestReset()
        {
            if (!isOwned)
            {
                return;
            }

            CmdReset(_revision);
        }

        /// <summary> 소유 클라이언트에게 복제된 특성 배분을 읽기 전용 snapshot으로 복사한다 </summary>
        public bool TryGetOwnerAllocation(out TalentAllocationSnapshot allocation)
        {
            allocation = default;
            if ((!isOwned) || (_revision == 0u))
            {
                return false;
            }

            int[] nodeIds = new int[_nodeIds.Count];
            for (int i = 0; i < _nodeIds.Count; ++i)
            {
                nodeIds[i] = _nodeIds[i];
            }

            allocation = new TalentAllocationSnapshot(_revision, _hasWeapon,
                _weapon, _spentPoints, nodeIds);
            return allocation.IsValid;
        }

        /****************************************
        *              Provider API
        ****************************************/

        public bool TryGetAllocation(ParticipantId participant,
            out TalentAllocationSnapshot allocation)
        {
            allocation = default;
            if ((!isServer) || (!participant.IsValid) ||
                (_growthParticipant == null) ||
                (_growthParticipant.Participant != participant) || (_revision == 0u))
            {
                return false;
            }

            allocation = BuildSnapshot();
            return allocation.IsValid;
        }

        /****************************************
        *           State Carrier API
        ****************************************/

        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (snapshot != null)
            {
                snapshot.TalentAllocation = BuildSnapshot();
            }
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) ||
                (snapshot.SchemaVersion != RunParticipantSnapshot.CURRENT_SCHEMA_VERSION) ||
                (!GameDB.IsInitialized) || (GameDB.Talent == null) ||
                (GameDB.LevelCurve == null) ||
                (!Enum.IsDefined(typeof(WeaponKind), snapshot.Weapon)) ||
                (!snapshot.TalentAllocation.HasWeapon) ||
                (snapshot.TalentAllocation.Weapon != snapshot.Weapon))
            {
                failure = "특성 복구 헤더가 유효하지 않다";
                return false;
            }

            long totalPointsValue = (long)(snapshot.Level - Constants.Combat.START_LEVEL) *
                GameDB.LevelCurve.TalentPointPerLevel;
            if ((totalPointsValue < 0L) || (totalPointsValue > int.MaxValue))
            {
                failure = "복구할 특성 포인트가 범위를 넘었다";
                return false;
            }

            return TalentRules.TryValidateAllocation(
                GameDB.Talent,
                snapshot.TalentAllocation,
                (int)totalPointsValue,
                out failure);
        }

        /****************************************
        *               Internals
        ****************************************/

        private TalentAllocationSnapshot BuildSnapshot()
        {
            int[] nodeIds = new int[_serverNodeIds.Count];
            for (int i = 0; i < _serverNodeIds.Count; ++i)
            {
                nodeIds[i] = _serverNodeIds[i];
            }

            return new TalentAllocationSnapshot(
                _revision,
                _hasWeapon,
                _weapon,
                _spentPoints,
                nodeIds);
        }

        private void HandleWeaponChanged(WeaponKind weapon)
        {
            if ((!isServer) || (_hasWeapon && (_weapon == weapon)))
            {
                return;
            }

            if ((_loadoutRevisionTarget == null) || (!ServerCanResetForWeapon(weapon)))
            {
                Debug.LogError("[TalentController] 무기 변경에 맞춰 특성을 초기화하지 못했다", this);
                return;
            }

            bool initializesWeapon = !_hasWeapon;
            uint nextRevision = (_revision == 0u)
                ? 1u
                : (initializesWeapon ? _revision : _revision + 1u);
            bool registered = initializesWeapon
                ? _loadoutRevisionTarget.ServerRegisterTalentRevision(nextRevision)
                : _loadoutRevisionTarget.ServerTryCommitTalentRevision(
                    _revision, nextRevision);
            if (!registered)
            {
                Debug.LogError("[TalentController] 장비 거래 중 특성 초기화가 누락됐다", this);
                return;
            }

            ServerApplyWeaponReset(weapon);
            ServerPublishAllocation();
        }

        private void NotifyRevisionChanged(uint revision)
        {
            Action<uint> handlers = RevisionChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<uint>)observers[i])(revision);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TalentController] 특성 observer 실패: {e}", this);
                }
            }
        }

        /****************************************
        *               TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetAllocationResult(TalentAllocationResult result)
        {
            AllocationResult?.Invoke(result);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnRevisionChanged(uint _, uint next)
        {
            if (!isServer)
            {
                NotifyRevisionChanged(next);
            }
        }
    }
}
