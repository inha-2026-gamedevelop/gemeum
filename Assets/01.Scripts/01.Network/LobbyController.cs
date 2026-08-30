// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
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

        /// <summary> 로비 슬롯 하나. 무기는 런 시작 시점에만 채워진다 </summary>
        public struct LobbySlot
        {
            public int        ConnectionId;
            public bool       IsReady;
            public bool       HasWeapon;
            public WeaponKind Weapon;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly SyncList<LobbySlot> _slots = new SyncList<LobbySlot>();

        private System.Random _serverRandom = null; // 서버 전용, 역할 배분의 유일한 난수원

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
                ConnectionId = conn.connectionId,
                IsReady      = false,
                HasWeapon    = false,
                Weapon       = default,
            });
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

            _slots.RemoveAt(index);
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

            if (!TryFindSlotIndex(sender.connectionId, out int index))
            {
                Debug.LogWarning($"[LobbyController] 슬롯이 없는 연결의 준비 요청을 무시했다: connectionId={sender.connectionId}", this);
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

        /****************************************
        *                Methods
        ****************************************/

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

            if (_slots.Count != Constants.Network.MAX_PLAYERS)
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

            // 무기가 세 명 모두에게 실제로 적용된 뒤에만 런을 연다
            if (!AssignWeapons())
            {
                return;
            }

            LockAdmission();
            RunManager.ServerInstance.StartRun();
        }

        // 접속 순서나 이름이 아니라 서버 난수로 순열을 만든다
        [Server]
        private bool AssignWeapons()
        {
            WeaponKind[] weapons = (WeaponKind[]) Enum.GetValues(typeof(WeaponKind));
            if (weapons.Length < _slots.Count)
            {
                Debug.LogError($"[LobbyController] 무기 종류가 인원보다 적다: {weapons.Length} < {_slots.Count}", this);
                return false;
            }

            // 한 명이라도 적용에 실패하면 아무에게도 배분하지 않는다
            PlayerWeapon[] targets = new PlayerWeapon[_slots.Count];
            for (int i = 0; i < _slots.Count; ++i)
            {
                if (!TryFindPlayerWeapon(_slots[i].ConnectionId, out targets[i]))
                {
                    Debug.LogError($"[LobbyController] connectionId={_slots[i].ConnectionId}의 PlayerWeapon을 찾지 못했다", this);
                    return false;
                }
            }

            for (int i = weapons.Length - 1; i > 0; --i)
            {
                int swapIndex = _serverRandom.Next(i + 1);

                WeaponKind temp    = weapons[i];
                weapons[i]         = weapons[swapIndex];
                weapons[swapIndex] = temp;
            }

            for (int i = 0; i < _slots.Count; ++i)
            {
                if (!targets[i].ServerSetInitialWeapon(weapons[i]))
                {
                    Debug.LogError($"[LobbyController] connectionId={_slots[i].ConnectionId}에 무기를 확정하지 못했다", this);
                    return false;
                }

                LobbySlot slot = _slots[i];
                slot.HasWeapon = true;
                slot.Weapon    = weapons[i];
                _slots[i]      = slot;
            }

            return true;
        }

        // 연결의 플레이어 오브젝트에서 무기 소유자를 찾는다
        private bool TryFindPlayerWeapon(int connectionId, out PlayerWeapon weapon)
        {
            weapon = null;

            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn))
            {
                return false;
            }

            if (conn.identity == null)
            {
                return false;
            }

            return conn.identity.TryGetComponent(out weapon);
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
