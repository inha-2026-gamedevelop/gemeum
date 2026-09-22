// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 서버 참가자 객체 목록. 네트워크 연결이 없는 참가자도 포함한다 </summary>
    public static class ParticipantDirectory
    {
        private static readonly Dictionary<ParticipantId, NetworkIdentity> _actors =
            new Dictionary<ParticipantId, NetworkIdentity>();
        private static ulong _lastBotId;

        public static int Count => _actors.Count;
        public static IReadOnlyDictionary<ParticipantId, NetworkIdentity> Actors => _actors;

        public static bool TryAllocateBot(out ParticipantId participant)
        {
            participant = ParticipantId.None;
            if ((!NetworkServer.active) || (_lastBotId == ulong.MaxValue))
            {
                return false;
            }

            participant = new ParticipantId(ParticipantKind.Bot, ++_lastBotId);
            return true;
        }

        public static bool Register(ParticipantId participant, NetworkIdentity actor)
        {
            if ((!NetworkServer.active) || (!participant.IsValid) || (actor == null))
            {
                return false;
            }

            if (_actors.TryGetValue(participant, out NetworkIdentity current) &&
                (current != null) && (current != actor))
            {
                return false;
            }

            _actors[participant] = actor;
            return true;
        }

        public static bool TryGet(ParticipantId participant, out NetworkIdentity actor)
        {
            actor = null;
            return participant.IsValid && _actors.TryGetValue(participant, out actor) &&
                (actor != null);
        }

        public static void Unregister(ParticipantId participant, NetworkIdentity actor)
        {
            if (_actors.TryGetValue(participant, out NetworkIdentity current) &&
                ReferenceEquals(current, actor))
            {
                _actors.Remove(participant);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _actors.Clear();
            _lastBotId = 0UL;
        }
    }
}
