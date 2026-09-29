// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈 순간 접근(TargetedDash, 패턴 3) 전용 실행 상태. Phase1State가 BeginAttack/
    /// TickWindup/ResolveImpact 시점마다 호출한다. FSM 상태(_state),타이머,attackInstanceId는
    /// 전부 Phase1State 소유로 남는다 - 이 클래스는 완료 여부를 판단하거나 상태를 바꾸지 않는다
    /// </summary>
    internal sealed class Phase1TargetedDashExecution
    {
        // 예고(TelegraphDuration) 앞 절반은 대상을 따라가고 뒤 절반은 방향을 확정한다
        // 완전 잠금이면 정지 표적을 지나쳐버리고, 완전 추적이면 회피 창이 없어진다 - 그 절충
        private const float DASH_TRACK_RATIO = 0.5f;

        // 공격 간에도 유지 - 다음 지목에서 제외할 직전 대상(연속 지목 방지)
        private Transform _lastDashTarget;

        // 공격 1회성 - OnBeginAttack에서 매번 다시 계산한다
        private Vector3 _dashStartPosition;
        private Vector3 _dashDestination;
        private Vector3 _trackedForward;

        /// <summary>
        /// 직전 지목자를 뺀 후보 중에서 다시 뽑는다. 후보가 없으면(생존자가 직전 지목자
        /// 하나뿐) 기본 대상(전역 어그로)을 쓴다. findNearestExcludingLast는 Phase1State가
        /// 갖고 있는 스캔 로직(SelectTargetExcludingLast)을 그대로 넘겨받는다 - MarkedCircle과
        /// 공유하는 물리 스캔이라 이 클래스가 중복 구현하지 않는다
        /// </summary>
        public Transform ResolveAttackTarget(Transform defaultTarget,
            Func<Transform, Transform> findNearestExcludingLast)
        {
            Transform selected = findNearestExcludingLast(_lastDashTarget) ?? defaultTarget;
            _lastDashTarget = selected;
            return selected;
        }

        public bool IsDistanceEligible(in BossPatternExecutionSpec spec, float distance)
        {
            return distance > spec.Range;
        }

        public void OnBeginAttack(Vector3 selfPosition, Vector3 lockedForward,
            in BossPatternExecutionSpec spec)
        {
            _dashStartPosition = selfPosition;
            _trackedForward    = lockedForward;
            _dashDestination   = selfPosition + (lockedForward * spec.MoveDistance);
        }

        /// <summary>
        /// 앞 절반은 대상 방향으로 매 틱 목적지를 다시 잡고(남은 거리 유지), 뒤 절반은
        /// 마지막으로 갱신된 목적지를 그대로 따라간다(방향 확정). 보스의 실제 회전
        /// (Transform.rotation)은 BeginAttack 시점에 고정된 채로 유지된다 - 기존 동작
        /// 그대로이며, 여기서 다시 돌리지 않는다
        /// </summary>
        public void TickWindup(float deltaTime, Transform self, Transform activeTarget,
            in BossPatternExecutionSpec spec, float telegraphDuration, float stateTimer)
        {
            float trackDuration = telegraphDuration * DASH_TRACK_RATIO;
            if (stateTimer < trackDuration)
            {
                Vector3 direction = activeTarget.position - self.position;
                direction.y = 0f;
                direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : _trackedForward;
                _trackedForward = direction;

                float traveled  = Vector3.Distance(_dashStartPosition, self.position);
                float remaining = Mathf.Max(0f, spec.MoveDistance - traveled);
                _dashDestination = self.position + (direction * remaining);
            }
            // 뒤 절반은 위에서 마지막으로 갱신된 _dashDestination을 그대로 따라간다(방향 확정)

            self.position = Vector3.MoveTowards(self.position, _dashDestination,
                spec.MoveSpeed * deltaTime);
        }

        /// <summary>
        /// 도착 지점 기준 전방위 판정이라 각도 제한이 필요 없어 360도로 둔다
        /// 러프 VFX 연결(이슈 검증용)
        /// </summary>
        public void ResolveImpact(Transform self, in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller,
            uint deflectedDefender)
        {
            controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.TargetedDash,
                self.position, self.forward);

            BossPatternRunner.ExecuteMeleeArc(
                self.position,
                self.forward,
                spec.ImpactRadius,
                360f,
                damage,
                attackerNetId,
                playerLayerMask,
                controller,
                deflectedDefender: deflectedDefender);
        }
    }
}
