// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    public partial class TalentController
    {
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
        *              Provider API
        ****************************************/

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
    }
}
