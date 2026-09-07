// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 로비의 준비 상태와 런 시작 게이트 </summary>
    public class LobbyController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("접속하면 자동으로 준비 상태로 만든다. 로비 UI가 아직 없는 Dev 씬 테스트 전용, Main.unity에서는 반드시 꺼 둔다")]
        [SerializeField] private bool _devAutoReady = false;

        [Tooltip("런을 시작하는 데 필요한 인원. 혼자 이동, 모션을 확인하려고 낮추는 Dev 씬 전용 값이고, Main.unity에서는 반드시 3으로 둔다")]
        [SerializeField] private int _devMinPlayers = Constants.Network.MAX_PLAYERS;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static LobbyController ServerInstance { get; private set; }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
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

        /// <summary> 접속한 플레이어의 슬롯을 만든다. GeumeumNetworkManager만 호출한다 </summary>
        [Server]
        public void ServerAddSlot(NetworkConnectionToClient conn)
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                Debug.LogError("[LobbyController] 로스터가 없어 슬롯을 만들지 못했다", this);
                return;
            }

            if (!roster.ServerAddSlot(conn))
            {
                return;
            }

            roster.ServerRestoreVacatedSeat(conn);

            if (TryResumePausedRun(conn))
            {
                return;
            }

            if (_devAutoReady)
            {
                SetSlotReady(conn.connectionId, true);
            }
        }

        /// <summary> 이탈한 플레이어의 슬롯을 지운다. GeumeumNetworkManager만 호출한다 </summary>
        [Server]
        public void ServerRemoveSlot(NetworkConnectionToClient conn)
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                return;
            }

            roster.ServerRemoveSlot(conn);
        }

        /// <summary> 런이 끝나 로비로 돌아올 때 준비 상태와 역할 확정을 되돌린다. RunManager만 호출한다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            LobbyRoleCoordinator coordinator = LobbyRoleCoordinator.ServerInstance;
            if (coordinator != null)
            {
                coordinator.ServerResetForNewRun();
            }

            if (!_devAutoReady)
            {
                return;
            }

            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                return;
            }

            for (int i = 0; i < roster.SlotCount; ++i)
            {
                SetSlotReady(roster.GetSlot(i).ConnectionId, true);
            }
        }

        /// <summary> 배분된 무기를 조회한다, 런 시작 전에는 false </summary>
        public bool TryGetWeapon(int connectionId, out WeaponKind weapon)
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster != null)
            {
                return roster.TryGetWeapon(connectionId, out weapon);
            }

            weapon = default;
            return false;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command(requiresAuthority = false)]
        public void CmdSetReady(bool isReady, NetworkConnectionToClient sender = null)
        {
            if ((sender == null) || (IsAdmissionLocked()))
            {
                return;
            }

            SetSlotReady(sender.connectionId, isReady);
        }

        /// <summary> 로비에서 원하는 캐릭터를 고른다. 확정이 아니라 희망이라 남과 겹쳐도 받는다 </summary>
        [Command(requiresAuthority = false)]
        public void CmdSelectCharacter(CharacterKind character, NetworkConnectionToClient sender = null)
        {
            if ((sender == null) || (IsAdmissionLocked()))
            {
                return;
            }

            if (!Enum.IsDefined(typeof(CharacterKind), character))
            {
                Debug.LogWarning($"[LobbyController] 정의되지 않은 캐릭터 선택을 무시했다: connectionId={sender.connectionId}, character={character}", this);
                return;
            }

            LobbyRoster roster = LobbyRoster.ServerInstance;
            if ((roster == null) || (!roster.TryFindSlotIndex(sender.connectionId, out int index)))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 캐릭터 선택을 무시했다: connectionId={sender.connectionId}", this);
                return;
            }

            LobbySlot slot = roster.GetSlot(index);
            if ((slot.HasPreference) && (slot.PreferredCharacter == character))
            {
                return;
            }

            slot.HasPreference      = true;
            slot.PreferredCharacter = character;

            // 준비를 마친 뒤 희망을 바꾸면 준비를 무른다. 그러지 않으면 남들이 본 편성과
            // 다른 조건으로 런이 시작될 수 있다
            slot.IsReady = false;

            roster.SetSlot(index, slot);
        }

        /****************************************
        *                Methods
        ****************************************/

        [Server]
        private void SetSlotReady(int connectionId, bool isReady)
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if ((roster == null) || (!roster.TryFindSlotIndex(connectionId, out int index)))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 준비 요청을 무시했다: connectionId={connectionId}", this);
                return;
            }

            LobbySlot slot = roster.GetSlot(index);
            if (slot.IsReady == isReady)
            {
                return;
            }

            slot.IsReady = isReady;
            roster.SetSlot(index, slot);

            TryStartRun();
        }

        [Server]
        private void TryStartRun()
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            if ((roster == null) || (RunManager.ServerInstance == null))
            {
                Debug.LogError("[LobbyController] 로스터나 RunManager가 없어 런을 시작할 수 없다", this);
                return;
            }

            if (RunManager.ServerInstance.Phase != RunPhase.Lobby)
            {
                return;
            }

            int requiredPlayers = ResolveRequiredPlayers();
            if (roster.SlotCount != requiredPlayers)
            {
                return;
            }

            for (int i = 0; i < roster.SlotCount; ++i)
            {
                if (!roster.GetSlot(i).IsReady)
                {
                    return;
                }
            }

            if ((LobbyRoleCoordinator.ServerInstance == null) || (!LobbyRoleCoordinator.ServerInstance.ServerAssignRoles()))
            {
                return;
            }

            if (requiredPlayers != Constants.Network.MAX_PLAYERS)
            {
                Debug.LogWarning($"[LobbyController] Dev 설정으로 {requiredPlayers}인 런을 시작한다. 3인 검증에는 쓸 수 없다", this);
            }

            LockAdmission();

            if (RunManager.ServerInstance.StartRun())
            {
                return;
            }

            Debug.LogError("[LobbyController] 런 시작에 실패해 입장 잠금과 역할 배분을 되돌린다", this);

            GeumeumNetworkManager manager = NetworkManager.singleton as GeumeumNetworkManager;
            if (manager != null)
            {
                manager.UnlockAdmission();
            }

            LobbyRoleCoordinator coordinator = LobbyRoleCoordinator.ServerInstance;
            if (coordinator != null)
            {
                coordinator.ServerResetForNewRun();
            }
        }

        [Server]
        private bool TryResumePausedRun(NetworkConnectionToClient conn)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.IsPaused))
            {
                return false;
            }

            LobbyRoleCoordinator coordinator = LobbyRoleCoordinator.ServerInstance;
            if (coordinator != null)
            {
                coordinator.ServerRestoreRole(conn);
            }

            LobbyRoster roster          = LobbyRoster.ServerInstance;
            int         requiredPlayers = ResolveRequiredPlayers();

            if ((roster == null) || (roster.SlotCount < requiredPlayers))
            {
                int missing = (roster != null) ? (requiredPlayers - roster.SlotCount) : requiredPlayers;
                Debug.Log($"[LobbyController] 아직 {missing}명을 더 기다린다");
                return true;
            }

            LockAdmission();
            run.ResumeRun();
            return true;
        }

        private int ResolveRequiredPlayers()
        {
            if ((_devMinPlayers <= 0) || (_devMinPlayers > Constants.Network.MAX_PLAYERS))
            {
                return Constants.Network.MAX_PLAYERS;
            }

            return _devMinPlayers;
        }

        private void LockAdmission()
        {
            GeumeumNetworkManager manager = NetworkManager.singleton as GeumeumNetworkManager;
            if (manager == null)
            {
                Debug.LogError("[LobbyController] GeumeumNetworkManager를 찾지 못해 입장을 잠그지 못했다", this);
                return;
            }

            manager.LockAdmission();
        }

        private bool IsAdmissionLocked()
        {
            GeumeumNetworkManager manager = NetworkManager.singleton as GeumeumNetworkManager;
            return ((manager != null) && (manager.IsAdmissionLocked));
        }
    }
}
