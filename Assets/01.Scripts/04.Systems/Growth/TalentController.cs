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
        // SyncList callback이 실행되어 절반짜리 loadout을 관찰하지 않게 한다.
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
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            InitializeState();

            if ((_loadoutRevisionTarget == null) ||
                (!_loadoutRevisionTarget.ServerRegisterTalentRevision(_revision)))
            {
                Debug.LogError("[TalentController] 장비 전투 수치 revision 창구를 초기화하지 못했다", this);
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

        /// <summary> 장비 교체 트랜잭션이 원본을 건드리기 전에 초기화 가능 여부를 검사한다 </summary>
        [Server]
        public bool ServerCanResetForWeapon(WeaponKind weapon)
        {
            if ((!Enum.IsDefined(typeof(WeaponKind), weapon)) || (_publishPending))
            {
                return false;
            }

            return ((!_hasWeapon) || (_weapon == weapon) || (_revision < uint.MaxValue));
        }

        /// <summary> 장비 교체가 loadout revision을 이미 올린 뒤 특성 원본만 초기화한다 </summary>
        [Server]
        public void ServerApplyWeaponReset(WeaponKind weapon)
        {
            if (!ServerCanResetForWeapon(weapon))
            {
                throw new InvalidOperationException("특성 무기 초기화 계획이 현재 원본과 다릅니다.");
            }

            if (_hasWeapon && (_weapon == weapon))
            {
                return;
            }

            bool initializesWeapon = !_hasWeapon;
            _serverNodeIds.Clear();
            _hasWeapon   = true;
            _weapon      = weapon;
            _spentPoints = 0;
            _revision    = (_revision == 0u)
                ? 1u
                : (initializesWeapon ? _revision : _revision + 1u);
            _publishPending = true;
        }

        /// <summary> 관련 장비 원본과 특성 원본이 모두 확정된 뒤 변경을 알린다 </summary>
        [Server]
        public void ServerPublishAllocation()
        {
            if (!_publishPending)
            {
                return;
            }

            _publishPending = false;
            PublishNodeIds();
            NotifyRevisionChanged(_revision);
        }

        /****************************************
        *              Server API
        ****************************************/

        [Server]
        public TalentAllocationResult ServerTryAllocate(int nodeId,
            uint expectedRevision)
        {
            TalentAllocationResult mutation = ValidateMutation(expectedRevision);
            if ((mutation != TalentAllocationResult.Success) ||
                (!GameDB.IsInitialized) || (GameDB.Talent == null))
            {
                return (mutation == TalentAllocationResult.Success)
                    ? TalentAllocationResult.InvalidState
                    : mutation;
            }

            TalentAllocationSnapshot current = BuildSnapshot();
            TalentAllocationResult result = TalentRules.CanAllocate(
                GameDB.Talent,
                current,
                _growthParticipant.Growth.TalentPoints,
                nodeId,
                out TalentTreeSO.NodeEntry node);
            if (result != TalentAllocationResult.Success)
            {
                return result;
            }

            uint nextRevision = _revision + 1u;
            if (!_loadoutRevisionTarget.ServerTryCommitTalentRevision(
                    _revision, nextRevision))
            {
                return TalentAllocationResult.InvalidState;
            }

            // revision 창구가 먼저 새 loadout 세대를 예약한다. 이후 원본 변경은
            // List 추가와 값 대입뿐이라 observer 예외나 재진입이 끼어들지 않는다.
            _serverNodeIds.Add(node.NodeId);
            _spentPoints += node.PointCost;
            _revision = nextRevision;
            PublishNodeIds();
            NotifyRevisionChanged(_revision);
            return TalentAllocationResult.Success;
        }

        [Server]
        public TalentAllocationResult ServerTryReset(uint expectedRevision)
        {
            TalentAllocationResult mutation = ValidateMutation(expectedRevision, false);
            if (mutation != TalentAllocationResult.Success)
            {
                return mutation;
            }

            if (_serverNodeIds.Count == 0)
            {
                return TalentAllocationResult.Success;
            }

            if (_revision == uint.MaxValue)
            {
                return TalentAllocationResult.RevisionExhausted;
            }

            uint nextRevision = _revision + 1u;
            if (!_loadoutRevisionTarget.ServerTryCommitTalentRevision(
                    _revision, nextRevision))
            {
                return TalentAllocationResult.InvalidState;
            }

            _serverNodeIds.Clear();
            _spentPoints = 0;
            _revision    = nextRevision;
            PublishNodeIds();
            NotifyRevisionChanged(_revision);
            return TalentAllocationResult.Success;
        }

        [Server]
        public void ResetForNewRun()
        {
            _serverNodeIds.Clear();
            _nodeIds.Clear();
            _hasWeapon     = false;
            _weapon        = default;
            _spentPoints   = 0;
            _revision      = 1u;
            _publishPending = false;

            if (_loadoutRevisionTarget != null)
            {
                _loadoutRevisionTarget.ServerRegisterTalentRevision(_revision);
            }

            NotifyRevisionChanged(_revision);
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

        [Server]
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (!Validate(snapshot, out _))
            {
                return false;
            }

            TalentAllocationSnapshot restored = snapshot.TalentAllocation;
            _serverNodeIds.Clear();
            for (int i = 0; i < restored.NodeIds.Length; ++i)
            {
                _serverNodeIds.Add(restored.NodeIds[i]);
            }

            _hasWeapon     = restored.HasWeapon;
            _weapon        = restored.Weapon;
            _spentPoints   = restored.SpentPoints;
            _revision      = restored.Revision;
            _publishPending = false;
            PublishNodeIds();
            NotifyRevisionChanged(_revision);
            return true;
        }

        /****************************************
        *               Internals
        ****************************************/

        private void InitializeState()
        {
            _serverNodeIds.Clear();
            _nodeIds.Clear();
            _hasWeapon     = (_weaponTarget != null) && _weaponTarget.HasWeapon;
            _weapon        = _hasWeapon ? _weaponTarget.Weapon : default;
            _spentPoints   = 0;
            _revision      = 1u;
            _publishPending = false;
        }

        private TalentAllocationResult ValidateMutation(uint expectedRevision,
            bool requiresNextRevision = true)
        {
            if ((_revision == 0u) || (!_hasWeapon) || (_publishPending) ||
                (_loadoutRevisionTarget == null) || (_growthParticipant == null) ||
                (!_growthParticipant.Participant.IsValid))
            {
                return TalentAllocationResult.InvalidState;
            }

            if (expectedRevision != _revision)
            {
                return TalentAllocationResult.StaleRevision;
            }

            if (requiresNextRevision && (_revision == uint.MaxValue))
            {
                return TalentAllocationResult.RevisionExhausted;
            }

            return TalentAllocationResult.Success;
        }

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

        private void PublishNodeIds()
        {
            try
            {
                _nodeIds.Clear();
                for (int i = 0; i < _serverNodeIds.Count; ++i)
                {
                    _nodeIds.Add(_serverNodeIds[i]);
                }
            }
            catch (Exception e)
            {
                // SyncList 원본 변경은 이미 끝난 서버 원본을 되돌리지 않는다.
                Debug.LogError($"[TalentController] 특성 배분 공개 실패: {e}", this);
            }
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
    }
}
