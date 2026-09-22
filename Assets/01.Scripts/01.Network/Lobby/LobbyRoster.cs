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
    [System.Serializable]
    public struct LobbySlot
    {
        /// <summary> 재접속에도 유지되는 슬롯의 진짜 주인. connectionId는 매번 바뀐다 </summary>
        public ulong          PlatformId;

        public int            ConnectionId;
        public uint           PlayerNetId;
        public bool           IsRoomOwner;
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

            /// <summary> 로비에서 고른 희망도 같이 들고 있는다. 돌아온 뒤 다음 런의 편성이 이것을 본다 </summary>
            public readonly bool           HasPreference;
            public readonly CharacterKind  PreferredCharacter;

            public VacatedSeat(Vector3 position, in LobbySlot slot)
            {
                Position      = position;
                HasWeapon     = slot.HasWeapon;
                Weapon        = slot.Weapon;
                HasCharacter = slot.HasCharacter;
                Character    = slot.Character;
                HasPreference      = slot.HasPreference;
                PreferredCharacter = slot.PreferredCharacter;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly SyncList<LobbySlot> _slots = new SyncList<LobbySlot>();

        private readonly Dictionary<ulong, VacatedSeat> _vacatedSeats = new Dictionary<ulong, VacatedSeat>();
        private readonly HashSet<int> _failedRecoveryConnections = new HashSet<int>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스, 클라이언트에서는 null이다 </summary>
        public static LobbyRoster ServerInstance { get; private set; }

        public int SlotCount => _slots.Count;
        public bool HasPendingRecovery => (_vacatedSeats.Count > 0) || (_failedRecoveryConnections.Count > 0);

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _slots.Clear();
            _vacatedSeats.Clear();
            _failedRecoveryConnections.Clear();
        }

        public override void OnStopServer()
        {
            _vacatedSeats.Clear();
            _failedRecoveryConnections.Clear();
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
                PlayerNetId        = (conn.identity == null) ? 0u : conn.identity.netId,
                IsRoomOwner        = LobbyController.ServerCanManageRoom(conn),
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
            _failedRecoveryConnections.Remove(conn.connectionId);
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

        /// <summary> 씬 이동으로 교체된 플레이어와 로비 슬롯의 표시용 연결을 갱신한다 </summary>
        [Server]
        public void ServerRefreshPlayerBindings()
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                LobbySlot slot = _slots[i];
                uint currentId = ((NetworkServer.connections.TryGetValue(slot.ConnectionId, out NetworkConnectionToClient connection))
                    && (connection.identity != null)) ? connection.identity.netId : 0u;
                if (slot.PlayerNetId == currentId)
                {
                    continue;
                }
                slot.PlayerNetId = currentId;
                _slots[i] = slot;
            }
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

        /// <summary> 돌아온 사람의 상태를 복구한다. 복구에 실패해 연결을 끊었을 때만 false다 </summary>
        [Server]
        public bool ServerRestoreVacatedSeat(NetworkConnectionToClient conn)
        {
            if ((conn == null) || (conn.identity == null) || (!TryFindSlotIndex(conn.connectionId, out int index)))
            {
                return true;
            }

            // 신원을 모르면 자리 보존만 못 한다. 접속 처리 자체는 그대로 이어간다
            ulong platformId = _slots[index].PlatformId;
            if (platformId == 0UL)
            {
                return true;
            }

            if (!_vacatedSeats.TryGetValue(platformId, out VacatedSeat seat))
            {
                return true;
            }

            LobbySlot slot     = _slots[index];
            slot.HasWeapon     = seat.HasWeapon;
            slot.Weapon        = seat.Weapon;
            slot.HasCharacter = seat.HasCharacter;
            slot.Character    = seat.Character;
            slot.HasPreference      = seat.HasPreference;
            slot.PreferredCharacter = seat.PreferredCharacter;
            _slots[index]      = slot;

            ParticipantId participant = new ParticipantId(platformId);
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((director != null) && (director.OwnsParticipantSnapshot(participant)))
            {
                return true;
            }

            if ((!conn.identity.TryGetComponent(out IPlayerRunLifecycle lifecycle)) ||
                (!RestoreRunState(lifecycle, platformId)))
            {
                _failedRecoveryConnections.Add(conn.connectionId);
                RunManager run = RunManager.ServerInstance;
                if (run != null)
                {
                    run.PauseRun("참가자 상태 복구 실패, 재접속 대기");
                }

                Debug.LogError($"[LobbyRoster] platformId={platformId}의 복구에 실패해 상태를 보존하고 연결을 끊는다", this);
                conn.Disconnect();
                return false;
            }

            lifecycle.Teleport(seat.Position);
            ServerCompleteRecovery(participant);
            Debug.Log($"[LobbyRoster] platformId={platformId}에 이탈 직전 자리와 역할을 돌려줬다: {seat.Position}, {seat.Weapon}");
            return true;
        }

        /// <summary> 해당 연결의 복구 실패가 처리되기 전에는 런 재개에 포함하지 않는다 </summary>
        public bool HasFailedRecovery(int connectionId)
        {
            return _failedRecoveryConnections.Contains(connectionId);
        }

        /// <summary> 상태 복구가 확정된 참가자의 이전 자리를 정리한다 </summary>
        [Server]
        public void ServerCompleteRecovery(ParticipantId participant)
        {
            _vacatedSeats.Remove(participant.Value);
        }

        /// <summary> 새 런에서는 이전 참가자의 복구 권한과 실패 상태를 이어받지 않는다 </summary>
        [Server]
        public void ServerClearRecovery()
        {
            _vacatedSeats.Clear();
            _failedRecoveryConnections.Clear();
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
        private bool RestoreRunState(IPlayerRunLifecycle lifecycle, ulong platformId)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result))
            {
                return true;
            }

            ParticipantId participant = new ParticipantId(platformId);
            if (!RunParticipantRegistry.TryPeek(run.RunId, participant, out RunParticipantSnapshot snapshot))
            {
                return false;
            }

            if (!lifecycle.RestoreRunState(snapshot))
            {
                return false;
            }

            RunParticipantRegistry.ServerCompleteRestore(snapshot);
            Debug.Log($"[LobbyRoster] platformId={platformId}의 런 상태를 되돌렸다: HP={snapshot.Health:F1}, 관전 잔여={snapshot.SpectateRemainingSeconds:F1}초");
            return true;
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
                Debug.LogWarning($"[LobbyRoster] {NetworkPeerLabel.Describe(conn)}의 신원을 찾지 못했다, 자리 보존이 동작하지 않는다", this);
                return 0UL;
            }

            return id.Value;
        }

        [Server]
        private void RememberVacatedSeat(NetworkConnectionToClient conn, in LobbySlot slot)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result) ||
                (slot.PlatformId == 0UL) || (_failedRecoveryConnections.Contains(conn.connectionId)))
            {
                return;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            ParticipantId participant = new ParticipantId(slot.PlatformId);
            if ((director != null) && director.OwnsParticipantSnapshot(participant))
            {
                if (RunParticipantRegistry.TryPeek(run.RunId, participant,
                        out RunParticipantSnapshot snapshot))
                {
                    _vacatedSeats[slot.PlatformId] = new VacatedSeat(snapshot.Position, slot);
                }
                return;
            }

            if (conn.identity == null)
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
