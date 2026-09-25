// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 피해가 무적 때문에 거부됐을 때 그 무적이 어디서 왔는지. 극한 회피는 Roll만 인정한다 </summary>
    public enum InvincibilityCause : byte
    {
        None   = 0,
        Roll   = 1, // 구르기가 준 무적
        Revive = 2, // 부활 직후 무적
        Parry  = 3, // 패링 성공 무적
        Dev    = 4, // 개발용 무한 자원
        Other  = 5, // 원인을 가를 수 없는 나머지 일시 무적
    }

    /// <summary>
    /// 극한 회피 서버 판정(COMBAT_STYLE_DESIGN S7). PlayerStatus의 공용 무적 bool과 별개로 구르기가 준 무적만 기록해
    /// "양의 유효 공격이 충돌했으나 구르기 무적 앞쪽 구간에서 거부된 경우"를 가른다. 한 구르기당 한 번만 인정한다
    /// MonoBehaviour와 분리해 EditMode 테스트가 그대로 부른다. 시각은 모두 서버 시계(NetworkTime.time) 기준이다
    /// </summary>
    public sealed class PerfectDodgeTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private uint   _nextSerial    = 0u;
        private uint   _rollSerial    = 0u;  // 지금 기록된 구르기. 0이면 없음
        private uint   _awardedSerial = 0u;  // 이미 극한 회피를 인정한 구르기
        private double _startTime     = 0d;
        private double _endTime       = 0d;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 기록된 구르기 번호. 0이면 기록 없음 </summary>
        public uint CurrentRollSerial => _rollSerial;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 구르기가 무적을 준 순간을 기록하고 이 구르기의 번호를 돌려준다. 번호는 인스턴스 안에서 늘어나기만 한다 </summary>
        public uint BeginRoll(double startServerTime, double endServerTime)
        {
            ++_nextSerial;
            if (_nextSerial == 0u)
            {
                ++_nextSerial;
            }

            _rollSerial = _nextSerial;
            _startTime  = startServerTime;
            _endTime    = endServerTime;
            return _rollSerial;
        }

        /// <summary> 런 초기화·부활처럼 무적이 강제로 풀릴 때 구르기 기록을 지운다. 번호는 되감지 않는다 </summary>
        public void Clear()
        {
            _rollSerial    = 0u;
            _awardedSerial = 0u;
            _startTime     = 0d;
            _endTime       = 0d;
        }

        /// <summary> 이 시각의 무적이 기록된 구르기 무적 구간 안인지 </summary>
        public bool IsRollInvincibleAt(double serverTime)
        {
            if ((_rollSerial == 0u) || (!IsFinite(serverTime)) || (!IsFinite(_startTime)) || (!IsFinite(_endTime)))
            {
                return false;
            }

            return (serverTime >= _startTime) && (serverTime < _endTime);
        }

        /// <summary>
        /// 무적으로 거부된 피해 한 건을 보고 극한 회피인지 판정한다. 인정하면 이 구르기를 소비해 다시 인정하지 않는다
        /// 제외: 구르기 외 원인, 가해자 없는 환경 피해, 0·비유한 피해, 앞쪽 구간 밖, 이미 인정한 구르기
        /// </summary>
        public bool TryAward(InvincibilityCause cause, float amount, uint attackerNetId, double serverTime,
            float windowSeconds, out uint rollSerial)
        {
            rollSerial = 0u;

            if (cause != InvincibilityCause.Roll)
            {
                return false;
            }

            if (attackerNetId == DamageInfo.NO_ATTACKER)
            {
                return false;
            }

            if (float.IsNaN(amount) || float.IsInfinity(amount) || (amount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return false;
            }

            if ((float.IsNaN(windowSeconds)) || (windowSeconds <= 0f))
            {
                return false;
            }

            if ((_rollSerial == _awardedSerial) || (!IsRollInvincibleAt(serverTime)))
            {
                return false;
            }

            if ((serverTime - _startTime) > windowSeconds)
            {
                return false;
            }

            _awardedSerial = _rollSerial;
            rollSerial     = _rollSerial;
            return true;
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
