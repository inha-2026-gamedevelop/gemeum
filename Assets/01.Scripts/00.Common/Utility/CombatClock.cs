// System
using System.Collections.Generic;

namespace Geumeum.Common
{
    /// <summary>
    /// 각 대상이 마지막으로 전투한 서버 시각을 기록한다.
    /// 게임 디자인 6.5의 "피해를 주거나 받은 지 5초"를 판정하는 단일 원본이며,
    /// 상호작용이 열렸는지 묻는 쪽은 이 값만 본다.
    ///
    /// 기록은 피해가 실제로 적용되는 세 지점(플레이어 · 잡귀 · 보스의 TakeDamage)에서만 한다.
    /// 공격 판정 지점마다 걸면 근접 · 화살 · 방울 중 하나를 빠뜨리게 된다
    /// </summary>
    public static class CombatClock
    {
        /****************************************
        *               Constants
        ****************************************/

        /// <summary> 한 번도 전투하지 않았다는 뜻. 음수라 어떤 유예와 비교해도 통과한다 </summary>
        public const double NEVER = -1d;

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<uint, double> _lastCombatByNetId = new Dictionary<uint, double>();

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 피해 한 건을 기록한다. 맞은 쪽과 때린 쪽 모두 전투한 것으로 친다.
        /// 결계나 환경 피해처럼 가해자가 없으면 맞은 쪽만 기록한다
        /// </summary>
        public static void ServerNotifyDamage(uint victimNetId, uint attackerNetId, double serverTime)
        {
            Record(victimNetId, serverTime);

            if (attackerNetId != victimNetId)
            {
                Record(attackerNetId, serverTime);
            }
        }

        /// <summary> 마지막 전투 시각. 기록이 없으면 NEVER를 돌려준다 </summary>
        public static double GetLastCombatTime(uint netId)
        {
            if ((netId == DamageInfo.NO_ATTACKER) ||
                (!_lastCombatByNetId.TryGetValue(netId, out double serverTime)))
            {
                return NEVER;
            }

            return serverTime;
        }

        /// <summary> 지금 전투 중인지. 유예는 CombatConfigSO에서 읽어 넘긴다 </summary>
        public static bool IsInCombat(uint netId, double serverTime, float outOfCombatSeconds)
        {
            double last = GetLastCombatTime(netId);
            if (last < 0d)
            {
                return false;
            }

            return ((serverTime - last) < outOfCombatSeconds);
        }

        /// <summary> 대상이 사라졌을 때 기록을 지운다. netId가 재사용돼도 이전 값이 남지 않는다 </summary>
        public static void Forget(uint netId)
        {
            _lastCombatByNetId.Remove(netId);
        }

        public static void ClearAll()
        {
            _lastCombatByNetId.Clear();
        }

        /// <summary> 도메인 리로드가 꺼져 있어도 다음 판에 이전 기록이 남지 않게 한다 </summary>
        public static void ResetStatic()
        {
            _lastCombatByNetId.Clear();
        }

        /****************************************
        *               Internals
        ****************************************/

        private static void Record(uint netId, double serverTime)
        {
            // 가해자가 없는 환경 피해를 거른다
            if (netId == DamageInfo.NO_ATTACKER)
            {
                return;
            }

            if ((double.IsNaN(serverTime)) || (double.IsInfinity(serverTime)) || (serverTime < 0d))
            {
                return;
            }

            // 늦게 도착한 기록이 최신 시각을 되돌리면 상호작용이 다시 열린다
            if ((_lastCombatByNetId.TryGetValue(netId, out double previous)) && (previous >= serverTime))
            {
                return;
            }

            _lastCombatByNetId[netId] = serverTime;
        }
    }
}
