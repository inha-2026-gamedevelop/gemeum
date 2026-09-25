// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 처치 연속 감쇠(COMBAT_STYLE_DESIGN S3). 잡귀 떼에서 T4가 이어지면 화면이 쉬지 않으므로
    /// 직전 처치로부터 창 안에 들어온 처치는 T2로 낮춘다. 시각은 서버 결과의 ServerTime(서버 NetworkTime.time)이다
    /// 게임 시각(GameplayTime)은 Dev 씬, 필드 시간 상한, 보스 시계 시작 전에 멈춰 있어 모든 처치가 연쇄로 묶여 T4가 영영 나오지 않았다
    /// </summary>
    public sealed class KillChainDamper
    {
        /****************************************
        *                Fields
        ****************************************/

        private bool   _hasLastKill  = false;
        private double _lastKillTime = 0d;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 처치 단계면 결과의 ServerTime으로 연속 여부를 보고 낮춘다. 처치가 아니면 그대로 돌려준다 </summary>
        public ImpactTier Apply(ImpactTier tier, in DamageResult result, float windowSeconds)
        {
            if (tier != ImpactTier.Kill)
            {
                return tier;
            }

            // 시각이 망가진 결과는 ImpactRules.IsLate가 먼저 버린다
            double serverTime = result.ServerTime;
            bool   chained    = _hasLastKill && (Math.Abs(serverTime - _lastKillTime) <= windowSeconds);

            // 창은 마지막 처치부터 다시 잰다. 떼가 계속 쓰러지는 동안은 계속 낮춘다
            if (!_hasLastKill || (serverTime > _lastKillTime))
            {
                _lastKillTime = serverTime;
            }

            _hasLastKill = true;

            return chained ? ImpactTier.Medium : ImpactTier.Kill;
        }

        /// <summary> 런, 씬이 바뀌면 이전 처치를 잊는다 </summary>
        public void Reset()
        {
            _hasLastKill  = false;
            _lastKillTime = 0d;
        }
    }
}
