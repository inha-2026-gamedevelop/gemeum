// System
using System.Collections.Generic;

namespace Geumeum.Common
{
    /// <summary> 현재 서버에 존재하는 참가자 성장 컴포넌트의 공용 창구 </summary>
    public static class GrowthParticipantLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<ulong, IGrowthParticipant> _participants =
            new Dictionary<ulong, IGrowthParticipant>();

        /****************************************
        *              Properties
        ****************************************/

        public static int Count => _participants.Count;

        /****************************************
        *                Methods
        ****************************************/

        public static bool Register(IGrowthParticipant participant)
        {
            if ((participant == null) || (!participant.Participant.IsValid))
            {
                return false;
            }

            _participants[participant.Participant.Value] = participant;
            return true;
        }

        public static void Unregister(IGrowthParticipant participant)
        {
            if ((participant == null) || (!participant.Participant.IsValid))
            {
                return;
            }

            if (_participants.TryGetValue(participant.Participant.Value, out IGrowthParticipant registered) &&
                object.ReferenceEquals(registered, participant))
            {
                _participants.Remove(participant.Participant.Value);
            }
        }

        public static bool TryGet(ParticipantId participant, out IGrowthParticipant growthParticipant)
        {
            growthParticipant = null;
            return (participant.IsValid) && _participants.TryGetValue(participant.Value, out growthParticipant);
        }

        /// <summary> 현재 접속 중인 참가자를 수정 없이 순회할 수 있게 복사한다 </summary>
        public static void CopyTo(List<IGrowthParticipant> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            foreach (KeyValuePair<ulong, IGrowthParticipant> pair in _participants)
            {
                if (pair.Value != null)
                {
                    destination.Add(pair.Value);
                }
            }
        }

        public static void Clear()
        {
            _participants.Clear();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }
    }
}
