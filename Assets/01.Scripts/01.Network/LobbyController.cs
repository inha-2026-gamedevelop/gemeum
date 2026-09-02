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
    /// <summary>
    /// 로비의 준비 상태와 런 시작 게이트. 정확히 3명이 준비되어야 시작한다.
    /// 슬롯 저장은 LobbyRoster가, 역할 배분은 LobbyRoleCoordinator가 맡고
    /// 여기서는 두 곳을 잇는 파사드 역할도 겸한다
    /// </summary>
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

            // 얼어 있는 런에 돌아온 것이면 로비 준비 절차를 거치지 않는다, 하던 런을 이어서 재개한다
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
            LobbyRoster.ServerInstance?.ServerRemoveSlot(conn);
        }

        /// <summary> 런이 끝나 로비로 돌아올 때 준비 상태와 역할 확정을 되돌린다. RunManager만 호출한다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            LobbyRoleCoordinator.ServerInstance?.ServerResetForNewRun();

            if (!_devAutoReady)
            {
                return;
            }

            LobbyRoster roster = LobbyRoster.ServerInstance;
            if (roster == null)
            {
                return;
            }

            // SetSlotReady가 매번 TryStartRun을 부르지만 정원이 찼을 때만 통과하므로 마지막 한 번만 연다
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

        // 로비 오브젝트는 어느 클라이언트의 소유도 아니므로 권한 대신 서버가 호출자를 확인한다
        [Command(requiresAuthority = false)]
        public void CmdSetReady(bool isReady, NetworkConnectionToClient sender = null)
        {
            if ((sender == null) || (IsAdmissionLocked()))
            {
                return;
            }

            SetSlotReady(sender.connectionId, isReady);
        }

        /// <summary>
        /// 로비에서 외형을 고른다. 무기 배분과는 무관하며, 이미 다른 사람이 고른 외형은 거부한다.
        /// 고르지 않은 채 런이 시작되면 서버가 남은 외형을 배정한다
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdSelectAppearance(AppearanceKind appearance, NetworkConnectionToClient sender = null)
        {
            if ((sender == null) || (IsAdmissionLocked()))
            {
                return;
            }

            // 클라이언트가 보낸 값은 신뢰하지 않는다, 정의되지 않은 enum 값이 슬롯에 들어가면 모델 바인딩이 어긋난다
            if (!Enum.IsDefined(typeof(AppearanceKind), appearance))
            {
                Debug.LogWarning($"[LobbyController] 정의되지 않은 외형 선택을 무시했다: connectionId={sender.connectionId}, appearance={appearance}", this);
                return;
            }

            LobbyRoster roster = LobbyRoster.ServerInstance;
            if ((roster == null) || (!roster.TryFindSlotIndex(sender.connectionId, out int index)))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 외형 선택을 무시했다: connectionId={sender.connectionId}", this);
                return;
            }

            // 3인칭 오버숄더라 같은 외형이 둘이면 파티원을 구분할 수 없다
            for (int i = 0; i < roster.SlotCount; ++i)
            {
                if (i == index)
                {
                    continue;
                }

                LobbySlot other = roster.GetSlot(i);
                if ((other.HasAppearance) && (other.Appearance == appearance))
                {
                    return;
                }
            }

            LobbySlot slot     = roster.GetSlot(index);
            slot.HasAppearance = true;
            slot.Appearance    = appearance;
            roster.SetSlot(index, slot);
        }

        /****************************************
        *                Methods
        ****************************************/

        // CmdSetReady와 _devAutoReady 둘 다 여기로 모은다, 준비 상태를 바꾸는 경로는 하나뿐이어야 한다
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

        // 3명이 모두 준비된 순간을 한 번의 서버 트랜잭션으로 처리한다
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

            // 무기와 외형이 세 명 모두에게 실제로 적용된 뒤에만 런을 연다
            if ((LobbyRoleCoordinator.ServerInstance == null) || (!LobbyRoleCoordinator.ServerInstance.ServerAssignRoles()))
            {
                return;
            }

            // 정원 미만으로 시작한 런은 기획된 3인 조건이 아니다, 나중에 결과를 오해하지 않도록 남긴다
            if (requiredPlayers != Constants.Network.MAX_PLAYERS)
            {
                Debug.LogWarning($"[LobbyController] Dev 설정으로 {requiredPlayers}인 런을 시작한다. 3인 검증에는 쓸 수 없다", this);
            }

            LockAdmission();
            RunManager.ServerInstance.StartRun();
        }

        // 일시정지된 런에 인원이 다시 찼으면 역할을 되돌려 주고 재개한다
        [Server]
        private bool TryResumePausedRun(NetworkConnectionToClient conn)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.IsPaused))
            {
                return false;
            }

            LobbyRoleCoordinator.ServerInstance?.ServerRestoreRole(conn);

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

        // 접속 정원(MAX_PLAYERS)은 그대로 두고 런 시작 조건만 낮춘다.
        // 잘못된 값이 들어오면 조용히 따르지 않고 기획값으로 되돌린다
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
