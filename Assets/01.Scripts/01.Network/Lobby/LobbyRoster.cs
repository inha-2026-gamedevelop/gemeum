// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 로비 한 자리. SyncList로 전송되므로 값 타입이고 참조를 담지 않는다 </summary>
    public struct LobbySlot
    {
        /// <summary> 재접속에도 유지되는 슬롯의 진짜 주인. connectionId는 매번 바뀐다 </summary>
        public ulong          PlatformId;

        public int            ConnectionId;
        public bool           IsReady;
        public bool           HasWeapon;
        public WeaponKind     Weapon;
        public bool           HasCharacter;
        public CharacterKind  Character;

        /// <summary> 로비에서 고른 희망. 확정된 Character와 달리 서로 겹칠 수 있다 </summary>
        public bool           HasPreference;
        public CharacterKind  PreferredCharacter;
    }

    /// <summary> 누가 로비에 있는지의 원본. 슬롯 목록과 이탈자가 두고 간 자리를 소유한다 </summary>
    public class LobbyRoster : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 이탈자가 두고 간 자리와 역할 </summary>
        private readonly struct VacatedSeat
        {
            public readonly Vector3        Position;
            public readonly bool           HasWeapon;
            public readonly WeaponKind     Weapon;
            public readonly bool           HasCharacter;
            public readonly CharacterKind  Character;

            public VacatedSeat(Vector3 position, in LobbySlot slot)
            {
                Position      = position;
                HasWeapon     = slot.HasWeapon;
                Weapon        = slot.Weapon;
                HasCharacter = slot.HasCharacter;
                Character    = slot.Character;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly SyncList<LobbySlot> _slots = new SyncList<LobbySlot>();

        private readonly Dictionary<ulong, VacatedSeat> _vacatedSeats = new Dictionary<ulong, VacatedSeat>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static LobbyRoster ServerInstance { get; private set; }

        public int SlotCount => _slots.Count;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
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

        /// <summary> 접속한 플레이어의 자리를 만든다. 이미 있으면 아무것도 하지 않는다 </summary>
        [Server]
        public bool ServerAddSlot(NetworkConnectionToClient conn)
        {
            if ((conn == null) || (TryFindSlotIndex(conn.connectionId, out int _)))
            {
                return false;
            }

            _slots.Add(new LobbySlot
            {
                PlatformId         = ResolvePlatformId(conn),
                ConnectionId       = conn.connectionId,
                IsReady            = false,
                HasWeapon          = false,
                Weapon             = default,
                HasCharacter       = false,
                Character          = default,
                HasPreference      = false,
                PreferredCharacter = default,
            });

            return true;
        }

        /// <summary> 이탈한 플레이어의 자리를 지우고, 돌아올 때를 대비해 자리와 역할을 남긴다 </summary>
        [Server]
        public void ServerRemoveSlot(NetworkConnectionToClient conn)
        {
            if ((conn == null) || (!TryFindSlotIndex(conn.connectionId, out int index)))
            {
                return;
            }

            RememberVacatedSeat(conn, _slots[index]);
            _slots.RemoveAt(index);
        }

        public bool TryFindSlotIndex(int connectionId, out int index)
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

        public LobbySlot GetSlot(int index)
        {
            return _slots[index];
        }

        [Server]
        public void SetSlot(int index, in LobbySlot slot)
        {
            _slots[index] = slot;
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

        /// <summary> 이 신원이 두고 간 자리가 있는가 </summary>
        public bool HasVacatedSeat(ulong platformId)
        {
            return (platformId != 0UL) && (_vacatedSeats.ContainsKey(platformId));
        }

        /// <summary> 돌아온 사람에게 두고 간 자리와 역할을 돌려준다. 처음 오는 사람이면 아무것도 하지 않는다 </summary>
        [Server]
        public bool ServerRestoreVacatedSeat(NetworkConnectionToClient conn)
        {
            if ((conn == null) || (conn.identity == null) || (!TryFindSlotIndex(conn.connectionId, out int index)))
            {
                return false;
            }

            ulong platformId = _slots[index].PlatformId;
            if ((platformId == 0UL) || (!_vacatedSeats.TryGetValue(platformId, out VacatedSeat seat)))
            {
                return false;
            }

            _vacatedSeats.Remove(platformId);

            LobbySlot slot     = _slots[index];
            slot.HasWeapon     = seat.HasWeapon;
            slot.Weapon        = seat.Weapon;
            slot.HasCharacter = seat.HasCharacter;
            slot.Character    = seat.Character;
            _slots[index]      = slot;

            if (conn.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle))
            {
                lifecycle.Teleport(seat.Position);

                RestoreRunState(lifecycle, platformId);
            }

            Debug.Log($"[LobbyRoster] platformId={platformId}에 이탈 직전 자리와 역할을 돌려줬다: {seat.Position}, {seat.Weapon}");
            return true;
        }

        /// <summary> 연결의 플레이어 오브젝트에서 컴포넌트를 찾는다. 로비 쪽 조회가 모두 여기를 지난다 </summary>
        public static bool TryFindPlayerComponent<T>(int connectionId, out T component) where T : class
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

        /****************************************
        *                Methods
        ****************************************/

        [Server]
        private void RememberRunState(IPlayerRunLifecycle lifecycle, in LobbySlot slot)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby))
            {
                return;
            }

            RunParticipantSnapshot snapshot = new RunParticipantSnapshot
            {
                RunId       = run.RunId,
                Participant = new ParticipantId(slot.PlatformId),
                SceneId     = RunSceneId.Field,

                Character   = slot.Character,
                Weapon      = slot.Weapon,
            };

            if (!lifecycle.CaptureRunState(snapshot))
            {
                Debug.LogWarning($"[LobbyRoster] platformId={slot.PlatformId}의 런 상태를 담지 못했다", this);
                return;
            }

            RunParticipantRegistry.ServerStore(snapshot);
        }

        [Server]
        private void RestoreRunState(IPlayerRunLifecycle lifecycle, ulong platformId)
        {
            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                return;
            }

            ParticipantId participant = new ParticipantId(platformId);
            if (!RunParticipantRegistry.TryTake(run.RunId, participant, out RunParticipantSnapshot snapshot))
            {
                return;
            }

            if (!lifecycle.RestoreRunState(snapshot))
            {
                Debug.LogError($"[LobbyRoster] platformId={platformId}의 런 상태를 되붓지 못했다, 시작 상태로 둔다", this);
                return;
            }

            Debug.Log($"[LobbyRoster] platformId={platformId}의 런 상태를 되돌렸다: HP={snapshot.Health:F1}, 관전 잔여={snapshot.SpectateRemainingSeconds:F1}초");
        }

        [Server]
        private ulong ResolvePlatformId(NetworkConnectionToClient conn)
        {
            if (GeumeumAuthenticator.ServerInstance == null)
            {
                return 0UL;
            }

            if (!GeumeumAuthenticator.ServerInstance.TryGetPlatformId(conn.connectionId, out PlayerPlatformId id))
            {
                Debug.LogWarning($"[LobbyRoster] connectionId={conn.connectionId}의 신원을 찾지 못했다, 자리 보존이 동작하지 않는다", this);
                return 0UL;
            }

            return id.Value;
        }

        [Server]
        private void RememberVacatedSeat(NetworkConnectionToClient conn, in LobbySlot slot)
        {
            if ((conn.identity == null) || (slot.PlatformId == 0UL))
            {
                return;
            }

            if (!conn.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle))
            {
                return;
            }

            _vacatedSeats[slot.PlatformId] = new VacatedSeat(lifecycle.Position, slot);

            RememberRunState(lifecycle, slot);

            Debug.Log($"[LobbyRoster] platformId={slot.PlatformId}의 이탈 직전 자리와 역할을 남겼다: {lifecycle.Position}, {slot.Weapon}");
        }
    }
}
