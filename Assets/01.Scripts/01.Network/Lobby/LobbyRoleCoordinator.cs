// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 역할 배분의 실행자. 플레이어에게 역할을 확정하고 슬롯에 기록한다 </summary>
    public class LobbyRoleCoordinator : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("무기를 무작위 배분하지 않고 전원에게 검을 준다. 패링처럼 검 전용 기능을 혼자 테스트할 때 쓰는 Dev 씬 전용 값이고, Main.unity에서는 반드시 꺼 둔다")]
        [SerializeField] private bool _devForceSword = false;

        private System.Random _serverRandom = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static LobbyRoleCoordinator ServerInstance { get; private set; }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _serverRandom  = new System.Random();
        }

        public override void OnStopServer()
        {
            if (ServerInstance == this)
            {
                ServerInstance = null;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 로스터 전원에게 무기와 캐릭터를 확정한다 </summary>
        [Server]
        public bool ServerAssignRoles()
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                Debug.LogError("[LobbyRoleCoordinator] 로스터가 없어 역할을 배분할 수 없다", this);
                return false;
            }

            int count = roster.SlotCount;

            var targets  = new IPlayerRoleAssignment[count];
            var requests = new LobbyRoleAllocator.RoleRequest[count];

            for (int i = 0; i < count; ++i)
            {
                LobbySlot slot = roster.GetSlot(i);

                if (!LobbyRoster.TryFindPlayerComponent(slot.ConnectionId, out targets[i]))
                {
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={slot.ConnectionId}의 역할 확정 창구를 찾지 못했다", this);
                    return false;
                }

                if (targets[i].HasRole)
                {
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={slot.ConnectionId}는 이미 역할이 확정되어 있다", this);
                    return false;
                }

                requests[i] = new LobbyRoleAllocator.RoleRequest(
                    slot.HasWeapon,
                    slot.Weapon,
                    slot.HasCharacter,
                    slot.Character);
            }

            if (!LobbyRoleAllocator.TryAllocate(
                    requests,
                    _serverRandom,
                    _devForceSword,
                    out WeaponKind[]     weapons,
                    out CharacterKind[] characters,
                    out LobbyRoleAllocator.FailureKind failure,
                    out int              failedIndex))
            {
                LogAllocationFailure(roster, failure, failedIndex);
                return false;
            }

            for (int i = 0; i < count; ++i)
            {
                LobbySlot slot = roster.GetSlot(i);

                if (!targets[i].TryCommitRole(weapons[i], characters[i]))
                {
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={slot.ConnectionId}에 역할을 확정하지 못했다", this);
                    return false;
                }

                slot.HasWeapon     = true;
                slot.Weapon        = weapons[i];
                slot.HasCharacter = true;
                slot.Character    = characters[i];
                roster.SetSlot(i, slot);
            }

            return true;
        }

        /// <summary> 런이 끝나 로비로 돌아올 때 남아 있는 사람의 준비 상태와 확정을 되돌린다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                return;
            }

            for (int i = 0; i < roster.SlotCount; ++i)
            {
                LobbySlot slot = roster.GetSlot(i);

                slot.IsReady = false;
                roster.SetSlot(i, slot);

                if (LobbyRoster.TryFindPlayerComponent(slot.ConnectionId, out IPlayerRoleAssignment role))
                {
                    role.ClearRole();
                }

                if (LobbyRoster.TryFindPlayerComponent(slot.ConnectionId, out IPlayerRunLifecycle lifecycle))
                {
                    lifecycle.ResetForNewRun();
                }
            }
        }

        /// <summary> 얼어 있는 런에 돌아온 사람에게 역할을 즉시 되돌려 준다 </summary>
        [Server]
        public bool ServerRestoreRole(NetworkConnectionToClient conn)
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if ((roster == null) || (conn == null) || (conn.identity == null))
            {
                return false;
            }

            if (!roster.TryFindSlotIndex(conn.connectionId, out int index))
            {
                return false;
            }

            LobbySlot slot = roster.GetSlot(index);
            if ((!slot.HasWeapon) || (!slot.HasCharacter))
            {
                return false;
            }

            if ((!conn.identity.TryGetComponent(out IPlayerRoleAssignment role)) || (role.HasRole))
            {
                return false;
            }

            if (!role.TryCommitRole(slot.Weapon, slot.Character))
            {
                Debug.LogError($"[LobbyRoleCoordinator] connectionId={conn.connectionId}의 역할을 되돌려 주지 못했다", this);
                return false;
            }

            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void LogAllocationFailure(LobbyRoster roster, LobbyRoleAllocator.FailureKind failure, int failedIndex)
        {
            bool hasSlot      = ((failedIndex >= 0) && (failedIndex < roster.SlotCount));
            int  connectionId = hasSlot ? roster.GetSlot(failedIndex).ConnectionId : -1;

            switch (failure)
            {
                case LobbyRoleAllocator.FailureKind.NotEnoughWeapons:
                    Debug.LogError($"[LobbyRoleCoordinator] 무기 종류가 인원({roster.SlotCount})보다 적다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NotEnoughCharacters:
                    Debug.LogError($"[LobbyRoleCoordinator] 캐릭터 종류가 인원({roster.SlotCount})보다 적다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.InvalidCharacter:
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={connectionId}의 캐릭터 선택이 유효하지 않다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NoFreeCharacter:
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={connectionId}에 줄 남은 캐릭터가 없다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.InvalidWeapon:
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={connectionId}가 이어받은 무기가 유효하지 않다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NoFreeWeapon:
                    Debug.LogError($"[LobbyRoleCoordinator] connectionId={connectionId}에 줄 남은 무기가 없다", this);
                    break;

                default:
                    Debug.LogError("[LobbyRoleCoordinator] 역할 배분에 실패했다", this);
                    break;
            }
        }
    }
}
