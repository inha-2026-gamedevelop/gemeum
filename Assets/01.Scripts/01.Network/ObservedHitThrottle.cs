// System
using System.Collections.Generic;

namespace Geumeum.Network
{
    /// <summary>
    /// 동료 화면 움찔 전송을 대상별로 한 간격에 한 번으로 줄이는 순수 규칙(COMBAT_STYLE_DESIGN 8.1, S2)
    /// 셋이 한 잡귀를 연타해도 관찰자 RPC가 타격 수만큼 나가지 않게 한다. 시계는 호출부(서버 NetworkTime.time)가 넘긴다
    /// NetworkServer 없이 동작해 EditMode에서 검증한다
    /// </summary>
    public sealed class ObservedHitThrottle
    {
        /****************************************
        *                Fields
        ****************************************/

        // 대상 netId -> 마지막으로 보낸 서버 시각
        private readonly Dictionary<uint, double> _lastSentByTarget = new Dictionary<uint, double>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기억하고 있는 대상 수 </summary>
        public int TrackedCount => _lastSentByTarget.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 보내도 되면 기록하고 true. 같은 대상의 직전 전송에서 intervalSeconds가 지나지 않았으면 false </summary>
        public bool TryAccept(uint targetNetId, double now, float intervalSeconds)
        {
            if (double.IsNaN(now) || double.IsInfinity(now))
            {
                return false;
            }

            // 시계가 되돌아간 경우(새 세션)는 막지 않고 새로 잰다
            if (_lastSentByTarget.TryGetValue(targetNetId, out double last) &&
                (now >= last) && ((now - last) < intervalSeconds))
            {
                return false;
            }

            _lastSentByTarget[targetNetId] = now;
            return true;
        }

        /// <summary> 대상이 사라졌다. 씬마다 netId가 새로 발급되므로 지워 지난 netId가 쌓이지 않게 한다 </summary>
        public void Forget(uint targetNetId)
        {
            _lastSentByTarget.Remove(targetNetId);
        }

        public void Clear()
        {
            _lastSentByTarget.Clear();
        }
    }
}
