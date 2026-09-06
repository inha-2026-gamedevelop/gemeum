// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 이탈한 참가자의 런 상태를 맡아 두는 서버 보관소.
    ///
    /// 지금까지 돌아온 사람이 되찾는 것은 자리와 역할뿐이라, 저체력으로 빠졌다가 돌아오면
    /// 1레벨 시작 상태로 되살아났다. 무료 회복이자 사망 상태 해제이고, 성장과 장비가 붙으면
    /// 반대로 획득물 손실이 된다.
    ///
    /// 씬 오브젝트로 두지 않은 것은 아직 어느 씬에도 자리를 만들 이유가 없기 때문이다.
    /// 담는 값은 서버 메모리뿐이고 런이 끝나면 버린다 - 디스크에 쓰는 세이브가 아니다
    /// </summary>
    public static class RunParticipantRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<ulong, RunParticipantSnapshot> _snapshots =
            new Dictionary<ulong, RunParticipantSnapshot>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 맡아 둔 참가자 수 </summary>
        public static int Count => _snapshots.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 이탈 직전 상태를 맡아 둔다. 같은 참가자의 이전 값은 덮는다 -
        /// 나중에 담은 것이 언제나 더 최근이다
        /// </summary>
        public static bool ServerStore(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) || (!snapshot.Participant.IsValid))
            {
                return false; // 신원이 없으면 돌아왔을 때 알아볼 방법이 없다
            }

            _snapshots[snapshot.Participant.Value] = snapshot;
            return true;
        }

        /// <summary> 맡아 둔 것이 있는지. 꺼내지는 않는다 </summary>
        public static bool Has(ParticipantId participant)
        {
            return ((participant.IsValid) && (_snapshots.ContainsKey(participant.Value)));
        }

        /// <summary>
        /// 맡아 둔 상태를 꺼낸다. 꺼내면 보관소에서 사라지므로 같은 값을 두 번 되붓지 않는다.
        ///
        /// 런이 다르면 돌려주지 않는다. 이전 판의 값을 새 판에 부으면 그것이 곧 치트다
        /// </summary>
        public static bool TryTake(RunId runId, ParticipantId participant, out RunParticipantSnapshot snapshot)
        {
            snapshot = null;

            if ((!participant.IsValid) || (!_snapshots.TryGetValue(participant.Value, out RunParticipantSnapshot stored)))
            {
                return false;
            }

            if (!stored.Matches(runId, participant))
            {
                // 남겨 두면 다음 판에서 또 검사에 걸린다. 쓸 수 없는 값은 여기서 버린다
                _snapshots.Remove(participant.Value);
                Debug.LogWarning($"[RunParticipantRegistry] 다른 런의 상태라 버렸다: participant={participant}, 보관 런={stored.RunId}, 현재 런={runId}");
                return false;
            }

            _snapshots.Remove(participant.Value);
            snapshot = stored;
            return true;
        }

        /// <summary> 런이 끝나 보관할 이유가 사라졌을 때 전부 버린다 </summary>
        public static void ServerClear()
        {
            _snapshots.Clear();
        }

        // 에디터에서 플레이를 반복하면 정적 값이 남는다. 도메인 리로드를 끄는 설정에서도 새 판이 깨끗해야 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _snapshots.Clear();
        }
    }
}
