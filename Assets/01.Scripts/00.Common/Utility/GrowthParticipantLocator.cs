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

        private static readonly Dictionary<ParticipantId, IGrowthParticipant> _participants =
            new Dictionary<ParticipantId, IGrowthParticipant>();

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

            _participants[participant.Participant] = participant;
            return true;
        }

        public static void Unregister(IGrowthParticipant participant)
        {
            if ((participant == null) || (!participant.Participant.IsValid))
            {
                return;
            }

            if (_participants.TryGetValue(participant.Participant, out IGrowthParticipant registered) &&
                object.ReferenceEquals(registered, participant))
            {
                _participants.Remove(participant.Participant);
            }
        }

        public static bool TryGet(ParticipantId participant, out IGrowthParticipant growthParticipant)
        {
            growthParticipant = null;
            return (participant.IsValid) && _participants.TryGetValue(participant, out growthParticipant);
        }

        /// <summary> 현재 접속 중인 참가자를 수정 없이 순회할 수 있게 복사한다 </summary>
        public static void CopyTo(List<IGrowthParticipant> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            foreach (KeyValuePair<ParticipantId, IGrowthParticipant> pair in _participants)
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
