// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 이 클라이언트(공격 대상 본인)가 서버에서 받은 확정 패링 창 하나 </summary>
    public readonly struct ParryWindowSignal
    {
        public readonly ParryWindowKey Key;

        /// <summary> NetworkTime.time 기준 창 시작, 끝. 서버가 로드아웃 보너스까지 반영해 확정한 값이다 </summary>
        public readonly double StartServerTime;
        public readonly double EndServerTime;

        public ParryWindowSignal(ParryWindowKey key, double startServerTime, double endServerTime)
        {
            Key             = key;
            StartServerTime = startServerTime;
            EndServerTime   = endServerTime;
        }
    }

    /// <summary>
    /// 로컬 플레이어의 ParryController가 TargetRpc로 받은 패링 창을 표현 층에 나눠 준다(COMBAT_STYLE_DESIGN S4)
    /// 판정은 서버 ParryController가 한다. 여기는 이미 확정된 시각을 읽기만 한다
    /// 서버는 대상 1인에게만 창을 보내므로, 이 창구에 신호가 오면 로컬 플레이어가 그 공격의 대상이다
    /// </summary>
    public static class LocalParryWindowFeed
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버는 대상당 활성 창을 최대 1개만 둔다. 클라이언트도 마지막으로 열린 창 하나만 기억한다
        private static ParryWindowSignal _active;
        private static bool              _hasActive = false;

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 새 창이 열렸다(TargetOpenParryWindow 수신 시점). 창 시작은 보통 미래 시각이다 </summary>
        public static event Action<ParryWindowSignal> Opened;

        /// <summary> 서버가 창을 닫았다(취소, 교체, 패링 소비). 시간 경과로 끝난 창은 이 이벤트 없이 End에서 끝난다 </summary>
        public static event Action<ParryWindowKey> Closed;

        /// <summary> 로컬 플레이어가 사라져 남은 창 표현을 모두 버려야 할 때 </summary>
        public static event Action Cleared;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 아직 끝나지 않은 창이 있으면 돌려준다. 뷰가 뒤늦게 켜졌을 때 놓친 창을 줍는 용도 </summary>
        public static bool TryGetActive(double serverTime, out ParryWindowSignal signal)
        {
            signal = _active;
            return (_hasActive) && (serverTime < _active.EndServerTime);
        }

        /// <summary> ParryController.TargetOpenParryWindow가 부른다. 같은 키가 다시 오면 무시한다 </summary>
        public static void PublishOpened(ParryWindowKey key, double startServerTime, double endServerTime)
        {
            if ((!key.IsValid) || (!IsFinite(startServerTime)) || (!IsFinite(endServerTime)) ||
                (endServerTime <= startServerTime))
            {
                Debug.LogWarning($"[LocalParryWindowFeed] 잘못된 창을 받았다 {key} ({startServerTime:F3}~{endServerTime:F3})");
                return;
            }

            if ((_hasActive) && (_active.Key == key))
            {
                return;
            }

            _active    = new ParryWindowSignal(key, startServerTime, endServerTime);
            _hasActive = true;

            ObserverDispatch.Invoke(Opened, _active, null, nameof(LocalParryWindowFeed), nameof(Opened));
        }

        /// <summary> ParryController.TargetCloseParryWindow가 부른다 </summary>
        public static void PublishClosed(ParryWindowKey key)
        {
            if ((_hasActive) && (_active.Key == key))
            {
                _hasActive = false;
                _active    = default;
            }

            ObserverDispatch.Invoke(Closed, key, null, nameof(LocalParryWindowFeed), nameof(Closed));
        }

        /// <summary> 기억한 창을 버리고 구독자에게 알린다 </summary>
        public static void Clear()
        {
            _hasActive = false;
            _active    = default;

            Action handlers = Cleared;
            if (handlers == null)
            {
                return;
            }

            // 구독자 하나가 던져도 나머지 표현은 정리돼야 한다. 삼키지 않고 기록한다
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; ++i)
            {
                try
                {
                    ((Action)list[i])();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _hasActive = false;
            _active    = default;
            Opened     = null;
            Closed     = null;
            Cleared    = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
