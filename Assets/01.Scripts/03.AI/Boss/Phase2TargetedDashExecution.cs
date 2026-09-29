// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈 마수 돌진(TargetedDash, 패턴 7) 전용 실행 상태. Phase1의 동종 패턴과 달리
    /// 추적 구간,직전 지목자 제외가 없다 - 예고 시작 시점에 고정한 방향으로 한 번에
    /// 돌진한다. FSM 상태(_state),타이머,attackInstanceId는 전부 Phase2State 소유로 남는다
    /// </summary>
    internal sealed class Phase2TargetedDashExecution
    {
        private Vector3 _dashDestination; // 공격 1회성 - OnBeginAttack에서 매번 다시 계산한다

        public bool IsDistanceEligible(in BossPatternExecutionSpec spec, float distance)
        {
            return distance > spec.Range;
        }

        public void OnBeginAttack(Vector3 selfPosition, Vector3 lockedForward,
            in BossPatternExecutionSpec spec)
        {
            _dashDestination = selfPosition + (lockedForward * spec.MoveDistance);
        }

        public void TickWindup(float deltaTime, Transform self, in BossPatternExecutionSpec spec)
        {
            self.position = Vector3.MoveTowards(self.position, _dashDestination,
                spec.MoveSpeed * deltaTime);
        }

        /// <summary> 도착 지점 기준 전방위 판정이라 각도 제한이 필요 없어 360도로 둔다 </summary>
        public void ResolveImpact(Transform self, in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller)
        {
            BossPatternRunner.ExecuteMeleeArc(
                self.position,
                self.forward,
                spec.ImpactRadius,
                360f,
                damage,
                attackerNetId,
                playerLayerMask,
                controller);
        }
    }
}
