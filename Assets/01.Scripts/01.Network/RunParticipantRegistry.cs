// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 이탈한 참가자의 런 상태를 맡아 두는 서버 보관소 </summary>
    public static class RunParticipantRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<ParticipantId, RunParticipantSnapshot> _snapshots =
            new Dictionary<ParticipantId, RunParticipantSnapshot>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 맡아 둔 참가자 수 </summary>
        public static int Count => _snapshots.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이탈 직전 상태를 맡아 둔다 </summary>
        public static bool ServerStore(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) || (!snapshot.Participant.IsValid))
            {
                return false;
            }

            _snapshots[snapshot.Participant] = snapshot;
            return true;
        }

        /// <summary> 맡아 둔 것이 있는지. 꺼내지는 않는다 </summary>
        public static bool Has(ParticipantId participant)
        {
            return ((participant.IsValid) && (_snapshots.ContainsKey(participant)));
        }

        /// <summary> 복구가 성공하기 전까지 보관 상태를 소비하지 않고 읽는다 </summary>
        public static bool TryPeek(RunId runId, ParticipantId participant, out RunParticipantSnapshot snapshot)
        {
            snapshot = null;
            if ((!participant.IsValid) ||
                (!_snapshots.TryGetValue(participant, out RunParticipantSnapshot stored)) ||
                (!stored.Matches(runId, participant)))
            {
                return false;
            }

            snapshot = stored;
            return true;
        }

        /// <summary> 복구에 사용한 상태가 여전히 원본일 때만 보관을 끝낸다 </summary>
        public static bool ServerCompleteRestore(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) ||
                (!_snapshots.TryGetValue(snapshot.Participant, out RunParticipantSnapshot stored)) ||
                (!ReferenceEquals(stored, snapshot)))
            {
                return false;
            }

            return _snapshots.Remove(snapshot.Participant);
        }

        /// <summary> 맡아 둔 상태를 꺼낸다 </summary>
        public static bool TryTake(RunId runId, ParticipantId participant, out RunParticipantSnapshot snapshot)
        {
            snapshot = null;

            if ((!participant.IsValid) || (!_snapshots.TryGetValue(participant, out RunParticipantSnapshot stored)))
            {
                return false;
            }

            if (!stored.Matches(runId, participant))
            {
                _snapshots.Remove(participant);
                Debug.LogWarning($"[RunParticipantRegistry] 다른 런의 상태라 버렸다: participant={participant}, 보관 런={stored.RunId}, 현재 런={runId}");
                return false;
            }

            _snapshots.Remove(participant);
            snapshot = stored;
            return true;
        }

        /// <summary> 런이 끝나 보관할 이유가 사라졌을 때 전부 버린다 </summary>
        public static void ServerClear()
        {
            _snapshots.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            ServerClear();
        }
    }
}
