// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 이 클라이언트의 캐릭터가 서버에서 극한 회피를 인정받았다는 알림을 표현 층에 나눠 준다(COMBAT_STYLE_DESIGN S7)
    /// 네트워크 층(PlayerStatus의 TargetRpc)이 Publish하고, 타격 표현(05.Art ImpactDirector)이 Received를 구독한다
    /// 판정은 서버가 끝냈다. 구독자는 표현만 재생하고 보상을 계산하지 않는다
    /// </summary>
    public static class LocalPerfectDodgeFeed
    {
        /****************************************
        *                Fields
        ****************************************/

        // 마지막으로 흘려보낸 구르기 번호. 같은 구르기의 알림이 두 번 와도 한 번만 흘린다. 0은 아직 없음
        private static uint _lastRollSerial = 0u;

        /****************************************
        *              Events
        ****************************************/

        /// <summary>
        /// 극한 회피 한 번. 인자는 서버가 인정한 시각(서버 NetworkTime.time). 수신 쪽이 NetworkTime.time과 비교해 늦은 알림을 버린다
        /// 게임 진행 시각은 일시정지, 필드 시간 상한에서 멈추고 클라이언트 값도 같이 늦게 오므로 쓰지 않는다
        /// </summary>
        public static event Action<double> Received;

        /// <summary> 연결·런이 바뀌어 남은 표현을 버려야 할 때 </summary>
        public static event Action Cleared;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 받은 알림을 흘려보낸다. 이미 본 구르기거나 번호가 0이면 false </summary>
        public static bool Publish(uint rollSerial, double serverTime)
        {
            if ((rollSerial == 0u) || (rollSerial == _lastRollSerial))
            {
                return false;
            }

            _lastRollSerial = rollSerial;

            Action<double> handlers = Received;
            if (handlers == null)
            {
                return true;
            }

            // 구독자 하나가 던져도 나머지 표현은 살아야 한다. 삼키지 않고 기록한다
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; ++i)
            {
                try
                {
                    ((Action<double>)list[i])(serverTime);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return true;
        }

        /// <summary> 기억한 구르기 번호와 재생 중인 표현을 모두 버린다 </summary>
        public static void Clear()
        {
            _lastRollSerial = 0u;

            Cleared?.Invoke();
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _lastRollSerial = 0u;
            Received        = null;
            Cleared         = null;
        }
    }
}
