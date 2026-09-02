// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 로비의 준비 상태와 런 시작 게이트.
    /// 정확히 3명이 준비되어야 시작하고, 시작 순간 입장을 잠근 뒤 무기를 중복 없이 배분한다
    /// </summary>
    public class LobbyController : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 로비 슬롯 하나. 외형은 로비에서 고르고, 무기는 런 시작 시점에만 채워진다 </summary>
        public struct LobbySlot
        {
            public int            ConnectionId;
            public bool           IsReady;
            public bool           HasWeapon;
            public WeaponKind     Weapon;
            public bool           HasAppearance;
            public AppearanceKind Appearance;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("접속하면 자동으로 준비 상태로 만든다. 로비 UI가 아직 없는 Dev 씬 테스트 전용, Main.unity에서는 반드시 꺼 둔다")]
        [SerializeField] private bool _devAutoReady = false;

        [Tooltip("런을 시작하는 데 필요한 인원. 혼자 이동, 모션을 확인하려고 낮추는 Dev 씬 전용 값이고, Main.unity에서는 반드시 3으로 둔다")]
        [SerializeField] private int _devMinPlayers = Constants.Network.MAX_PLAYERS;

        [Tooltip("무기를 무작위 배분하지 않고 전원에게 검을 준다. 패링처럼 검 전용 기능을 혼자 테스트할 때 쓰는 Dev 씬 전용 값이고, Main.unity에서는 반드시 꺼 둔다")]
        [SerializeField] private bool _devForceSword = false;

        private readonly SyncList<LobbySlot> _slots = new SyncList<LobbySlot>();

        /// <summary> 이탈자가 두고 간 자리와 역할. 재접속자가 물려받는다 </summary>
        private readonly struct VacatedSeat
        {
            public readonly Vector3        Position;
            public readonly bool           HasWeapon;
            public readonly WeaponKind     Weapon;
            public readonly bool           HasAppearance;
            public readonly AppearanceKind Appearance;

            public VacatedSeat(Vector3 position, in LobbySlot slot)
            {
                Position      = position;
                HasWeapon     = slot.HasWeapon;
                Weapon        = slot.Weapon;
                HasAppearance = slot.HasAppearance;
                Appearance    = slot.Appearance;
            }
        }

        private System.Random _serverRandom = null; // 서버 전용, 역할 배분의 유일한 난수원

        // 이탈한 사람이 두고 간 자리와 역할이다. 재접속한 플레이어에게 그대로 물려줘
        // 잠깐 끊겼다 돌아와도 있던 자리에서 같은 직업으로 이어지게 한다.
        // connectionId는 재접속 때 새로 발급되어 같은 사람을 알아볼 수 없으므로 선입선출로 물려준다,
        // 두 명 이상이 동시에 빠지면 누가 어느 것을 받을지는 보장하지 않는다
        private readonly Queue<VacatedSeat> _vacatedSeats = new Queue<VacatedSeat>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static LobbyController ServerInstance { get; private set; }

        public int SlotCount => _slots.Count;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _serverRandom  = new System.Random();
            _slots.Clear();
            _vacatedSeats.Clear();
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
            if (conn == null)
            {
                return;
            }

            if (TryFindSlotIndex(conn.connectionId, out int _))
            {
                return;
            }

            _slots.Add(new LobbySlot
            {
                ConnectionId  = conn.connectionId,
                IsReady       = false,
                HasWeapon     = false,
                Weapon        = default,
                HasAppearance = false,
                Appearance    = default,
            });

            RestoreVacatedSeat(conn);

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

        // 일시정지된 런에 인원이 다시 찼으면 역할을 되돌려 주고 재개한다
        [Server]
        private bool TryResumePausedRun(NetworkConnectionToClient conn)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.IsPaused))
            {
                return false;
            }

            // 런이 이미 돌고 있으므로 AssignRoles를 기다릴 수 없다, 돌아온 사람만 즉시 확정한다
            if (!TryFindSlotIndex(conn.connectionId, out int index))
            {
                return false;
            }

            LobbySlot slot = _slots[index];
            if ((slot.HasWeapon) && (slot.HasAppearance) && (conn.identity != null)
                && (conn.identity.TryGetComponent(out IPlayerRoleAssignment role)) && (!role.HasRole))
            {
                if (!role.TryCommitRole(slot.Weapon, slot.Appearance))
                {
                    Debug.LogError($"[LobbyController] connectionId={conn.connectionId}의 역할을 되돌려 주지 못했다", this);
                }
            }

            if (_slots.Count < ResolveRequiredPlayers())
            {
                Debug.Log($"[LobbyController] 아직 {ResolveRequiredPlayers() - _slots.Count}명을 더 기다린다");
                return true;
            }

            LockAdmission();
            run.ResumeRun();
            return true;
        }

        /// <summary> 이탈한 플레이어의 슬롯을 지운다. GeumeumNetworkManager만 호출한다 </summary>
        [Server]
        public void ServerRemoveSlot(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                return;
            }

            if (!TryFindSlotIndex(conn.connectionId, out int index))
            {
                return;
            }

            RememberVacatedSeat(conn, _slots[index]);
            _slots.RemoveAt(index);
        }

        /// <summary>
        /// 런이 끝나 로비로 돌아올 때 남아 있는 슬롯의 준비, 무기, 외형을 비운다.
        /// 슬롯 자체는 지우지 않는다, 접속을 유지한 사람은 그대로 다음 런에 참가한다.
        /// RunManager만 호출한다
        /// </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                LobbySlot slot = _slots[i];

                slot.IsReady = false;
                _slots[i]    = slot;

                // 슬롯의 무기와 외형은 일부러 남긴다. 다음 배분이 같은 값을 그대로 재확정해
                // 잠깐 끊겼다 돌아와도 직업이 바뀌지 않는다.
                // 반대로 플레이어 쪽 확정은 반드시 풀어야 한다, 안 그러면
                // 다음 배분이 "이미 역할이 확정되어 있다"로 거부하고 런이 열리지 않는다
                if (TryFindPlayerComponent(slot.ConnectionId, out IPlayerRoleAssignment role))
                {
                    role.ClearRole();
                }

                // 직전 런에서 쓰러진 사람은 이걸 안 하면 다음 런에서도 Downed로 남아 아무 행동도 못 한다
                if (TryFindPlayerComponent(slot.ConnectionId, out IPlayerRunLifecycle lifecycle))
                {
                    lifecycle.ResetForNewRun();
                }
            }

            if (!_devAutoReady)
            {
                return;
            }

            // SetSlotReady가 매번 TryStartRun을 부르지만 정원이 찼을 때만 통과하므로 마지막 한 번만 연다
            for (int i = 0; i < _slots.Count; ++i)
            {
                SetSlotReady(_slots[i].ConnectionId, true);
            }
        }

        /// <summary> 배분된 무기를 조회한다, 런 시작 전에는 false </summary>
        public bool TryGetWeapon(int connectionId, out WeaponKind weapon)
        {
            if ((TryFindSlotIndex(connectionId, out int index)) && (_slots[index].HasWeapon))
            {
                weapon = _slots[index].Weapon;
                return true;
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
            if (sender == null)
            {
                return;
            }

            if (IsAdmissionLocked())
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
            if (sender == null)
            {
                return;
            }

            if (IsAdmissionLocked())
            {
                return;
            }

            // 클라이언트가 보낸 값은 신뢰하지 않는다, 정의되지 않은 enum 값이 슬롯에 들어가면 모델 바인딩이 어긋난다
            if (!Enum.IsDefined(typeof(AppearanceKind), appearance))
            {
                Debug.LogWarning($"[LobbyController] 정의되지 않은 외형 선택을 무시했다: connectionId={sender.connectionId}, appearance={appearance}", this);
                return;
            }

            if (!TryFindSlotIndex(sender.connectionId, out int index))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 외형 선택을 무시했다: connectionId={sender.connectionId}", this);
                return;
            }

            // 3인칭 오버숄더라 같은 외형이 둘이면 파티원을 구분할 수 없다
            for (int i = 0; i < _slots.Count; ++i)
            {
                if (i == index)
                {
                    continue;
                }

                if ((_slots[i].HasAppearance) && (_slots[i].Appearance == appearance))
                {
                    return;
                }
            }

            LobbySlot slot     = _slots[index];
            slot.HasAppearance = true;
            slot.Appearance    = appearance;
            _slots[index]      = slot;
        }

        /****************************************
        *                Methods
        ****************************************/

        // CmdSetReady와 _devAutoReady 둘 다 여기로 모은다, 준비 상태를 바꾸는 경로는 하나뿐이어야 한다
        [Server]
        private void SetSlotReady(int connectionId, bool isReady)
        {
            if (!TryFindSlotIndex(connectionId, out int index))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 준비 요청을 무시했다: connectionId={connectionId}", this);
                return;
            }

            LobbySlot slot = _slots[index];
            if (slot.IsReady == isReady)
            {
                return;
            }

            slot.IsReady  = isReady;
            _slots[index] = slot;

            TryStartRun();
        }

        // 3명이 모두 준비된 순간을 한 번의 서버 트랜잭션으로 처리한다
        [Server]
        private void TryStartRun()
        {
            if (RunManager.ServerInstance == null)
            {
                Debug.LogError("[LobbyController] RunManager가 없어 런을 시작할 수 없다", this);
                return;
            }

            if (RunManager.ServerInstance.Phase != RunPhase.Lobby)
            {
                return;
            }

            int requiredPlayers = ResolveRequiredPlayers();
            if (_slots.Count != requiredPlayers)
            {
                return;
            }

            for (int i = 0; i < _slots.Count; ++i)
            {
                if (!_slots[i].IsReady)
                {
                    return;
                }
            }

            // 무기와 외형이 세 명 모두에게 실제로 적용된 뒤에만 런을 연다
            if (!AssignRoles())
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

        // 배분 계산은 LobbyRoleAllocator가 하고, 여기서는 적용과 슬롯 갱신만 한다.
        // 한 명이라도 확정에 실패하면 아무에게도 적용하지 않는다
        [Server]
        private bool AssignRoles()
        {
            IPlayerRoleAssignment[] targets = new IPlayerRoleAssignment[_slots.Count];
            LobbyRoleAllocator.RoleRequest[] requests = new LobbyRoleAllocator.RoleRequest[_slots.Count];

            for (int i = 0; i < _slots.Count; ++i)
            {
                int connectionId = _slots[i].ConnectionId;

                if (!TryFindPlayerComponent(connectionId, out targets[i]))
                {
                    Debug.LogError($"[LobbyController] connectionId={connectionId}의 역할 확정 창구를 찾지 못했다", this);
                    return false;
                }

                // 적용 도중에 실패하면 되돌릴 수 없으므로 확정 가능 여부를 먼저 전부 확인한다
                if (targets[i].HasRole)
                {
                    Debug.LogError($"[LobbyController] connectionId={connectionId}는 이미 역할이 확정되어 있다", this);
                    return false;
                }

                requests[i] = new LobbyRoleAllocator.RoleRequest(
                    _slots[i].HasWeapon,
                    _slots[i].Weapon,
                    _slots[i].HasAppearance,
                    _slots[i].Appearance);
            }

            if (!LobbyRoleAllocator.TryAllocate(
                    requests,
                    _serverRandom,
                    _devForceSword,
                    out WeaponKind[]     weapons,
                    out AppearanceKind[] appearances,
                    out LobbyRoleAllocator.FailureKind failure,
                    out int              failedIndex))
            {
                LogAllocationFailure(failure, failedIndex);
                return false;
            }

            for (int i = 0; i < _slots.Count; ++i)
            {
                // 위에서 확정 가능 여부를 이미 확인했으므로 여기서 실패하면 상태가 어긋난 것이다.
                // 절반만 적용된 채 런을 열지 않도록 막는다
                if (!targets[i].TryCommitRole(weapons[i], appearances[i]))
                {
                    Debug.LogError($"[LobbyController] connectionId={_slots[i].ConnectionId}에 역할을 확정하지 못했다", this);
                    return false;
                }

                LobbySlot slot     = _slots[i];
                slot.HasWeapon     = true;
                slot.Weapon        = weapons[i];
                slot.HasAppearance = true;
                slot.Appearance    = appearances[i];
                _slots[i]          = slot;
            }

            return true;
        }

        // 배분기는 UnityEngine을 모르므로 실패 사유만 돌려준다. 로그 문구는 여기서 정한다
        private void LogAllocationFailure(LobbyRoleAllocator.FailureKind failure, int failedIndex)
        {
            bool hasSlot        = ((failedIndex >= 0) && (failedIndex < _slots.Count));
            int  connectionId   = hasSlot ? _slots[failedIndex].ConnectionId : -1;

            switch (failure)
            {
                case LobbyRoleAllocator.FailureKind.NotEnoughWeapons:
                    Debug.LogError($"[LobbyController] 무기 종류가 인원({_slots.Count})보다 적다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NotEnoughLooks:
                    Debug.LogError($"[LobbyController] 외형 종류가 인원({_slots.Count})보다 적다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.InvalidAppearance:
                    Debug.LogError($"[LobbyController] connectionId={connectionId}의 외형 선택이 유효하지 않다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NoFreeAppearance:
                    Debug.LogError($"[LobbyController] connectionId={connectionId}에 줄 남은 외형이 없다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.InvalidWeapon:
                    Debug.LogError($"[LobbyController] connectionId={connectionId}가 이어받은 무기가 유효하지 않다", this);
                    break;

                case LobbyRoleAllocator.FailureKind.NoFreeWeapon:
                    Debug.LogError($"[LobbyController] connectionId={connectionId}에 줄 남은 무기가 없다", this);
                    break;

                default:
                    Debug.LogError("[LobbyController] 역할 배분에 실패했다", this);
                    break;
            }
        }

        // 이탈 직전의 자리와 역할을 남긴다.
        // Mirror는 OnServerDisconnect보다 먼저 NetworkServer.connections에서 연결을 지우므로
        // connectionId로 되찾을 수 없다. 아직 identity가 살아 있는 conn을 직접 받아 읽는다
        [Server]
        private void RememberVacatedSeat(NetworkConnectionToClient conn, in LobbySlot slot)
        {
            if ((conn == null) || (conn.identity == null))
            {
                return;
            }

            if (!conn.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle))
            {
                return;
            }

            _vacatedSeats.Enqueue(new VacatedSeat(lifecycle.Position, slot));
            Debug.Log($"[LobbyController] connectionId={conn.connectionId}의 이탈 직전 자리와 역할을 남겼다: {lifecycle.Position}, {slot.Weapon}");
        }

        // 새로 들어온 사람에게 비어 있던 자리와 역할을 물려준다. 남은 것이 없으면 스폰 지점에 그대로 두고
        // 역할은 배분기가 새로 정한다
        [Server]
        private void RestoreVacatedSeat(NetworkConnectionToClient conn)
        {
            if (_vacatedSeats.Count == 0)
            {
                return;
            }

            if ((conn == null) || (conn.identity == null))
            {
                Debug.LogWarning("[LobbyController] 플레이어 오브젝트가 없어 이탈 직전 자리를 물려주지 못했다", this);
                return;
            }

            if (!TryFindSlotIndex(conn.connectionId, out int index))
            {
                return;
            }

            VacatedSeat seat = _vacatedSeats.Dequeue();

            // 역할은 슬롯에만 적어 둔다. 실제 확정은 런이 열릴 때 AssignRoles가 한 번에 한다
            LobbySlot slot     = _slots[index];
            slot.HasWeapon     = seat.HasWeapon;
            slot.Weapon        = seat.Weapon;
            slot.HasAppearance = seat.HasAppearance;
            slot.Appearance    = seat.Appearance;
            _slots[index]      = slot;

            if (conn.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle))
            {
                lifecycle.Teleport(seat.Position);
            }

            Debug.Log($"[LobbyController] connectionId={conn.connectionId}에 이탈 직전 자리와 역할을 물려줬다: {seat.Position}, {seat.Weapon}");
        }

        // 연결의 플레이어 오브젝트에서 컴포넌트를 찾는다
        private bool TryFindPlayerComponent<T>(int connectionId, out T component) where T : class
        {
            component = null;

            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn))
            {
                return false;
            }

            if (conn.identity == null)
            {
                return false;
            }

            return conn.identity.TryGetComponent(out component);
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

        private bool TryFindSlotIndex(int connectionId, out int index)
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                if (_slots[i].ConnectionId == connectionId)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }
    }
}
