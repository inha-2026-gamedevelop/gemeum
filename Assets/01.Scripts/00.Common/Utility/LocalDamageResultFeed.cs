// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 이 클라이언트가 받은 피해 결과(내가 준 피해)를 표현 층에 나눠 준다
    /// 네트워크 층이 Publish하고, 타격 표현(05.Art)과 데미지 숫자(06.UI)가 Received를 구독한다
    /// 같은 사건이 두 번 와도(호스트 이중 경로·재전송) 한 번만 흘려보낸다
    /// </summary>
    public static class LocalDamageResultFeed
    {
        /****************************************
        *              Constants
        ****************************************/

        // 최근 사건 번호를 기억하는 칸 수. 한 프레임에 이보다 많이 오지 않는다
        private const int RECENT_EVENT_CAPACITY = 128;

        /****************************************
        *                Fields
        ****************************************/

        private static readonly uint[] _recent = new uint[RECENT_EVENT_CAPACITY];
        private static int _recentCount = 0;
        private static int _recentNext  = 0;

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 새 피해 결과 하나. 구독자는 판정을 다시 하지 않는다 </summary>
        public static event Action<DamageResult> Received;

        /// <summary> 런·씬이 바뀌어 남은 표현을 모두 버려야 할 때 </summary>
        public static event Action Cleared;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 받은 결과를 흘려보낸다. 이미 본 사건이면 false </summary>
        public static bool Publish(in DamageResult result)
        {
            if (!IsFresh(result.EventId))
            {
                return false;
            }

            Remember(result.EventId);

            // 구독자 하나가 던져도 나머지 표현은 살아야 한다. 삼키지 않고 기록한다
            ObserverDispatch.Invoke(Received, result, null, nameof(LocalDamageResultFeed), nameof(Received));
            return true;
        }

        /// <summary> 기억한 사건과 대기 중인 표현을 모두 버린다 </summary>
        public static void Clear()
        {
            Array.Clear(_recent, 0, _recent.Length);
            _recentCount = 0;
            _recentNext  = 0;

            Cleared?.Invoke();
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Array.Clear(_recent, 0, _recent.Length);
            _recentCount = 0;
            _recentNext  = 0;
            Received     = null;
            Cleared      = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool IsFresh(uint eventId)
        {
            for (int i = 0; i < _recentCount; ++i)
            {
                if (_recent[i] == eventId)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Remember(uint eventId)
        {
            _recent[_recentNext] = eventId;
            _recentNext          = (_recentNext + 1) % RECENT_EVENT_CAPACITY;
            _recentCount         = Math.Min(_recentCount + 1, RECENT_EVENT_CAPACITY);
        }
    }
}
