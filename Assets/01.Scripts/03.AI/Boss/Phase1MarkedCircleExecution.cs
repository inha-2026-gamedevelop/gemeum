// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈 여우불 낙인(MarkedCircle, 패턴 4) 전용 실행 상태. Phase1State가 BeginAttack/
    /// ResolveImpact 시점마다 호출한다. FSM 상태(_state),타이머,attackInstanceId,반복
    /// 스케줄은 전부 Phase1State 소유로 남는다 - 이 클래스는 완료 여부를 판단하거나
    /// 상태를 바꾸지 않는다
    /// </summary>
    internal sealed class Phase1MarkedCircleExecution
    {
        // 여우불 낙인 전용 - VFX 원점이 지면에 살짝 묻혀서 그만큼 띄운다. 판정 반경(Center)은
        // 그대로 두고 표현만 올린다
        private const float VFX_HEIGHT_OFFSET = 0.1f;

        // 공격 간에도 유지 - 다음 지목에서 제외할 직전 낙인 대상(연속 지목 방지)
        private Transform _lastMarkedTarget;

        // 공격 1회성 - OnBeginAttack에서 매번 다시 계산한다(예고 시작 시점에 고정)
        private Vector3 _center;

        /// <summary> 텔레그래프 큐 원점 계산(PublishRepeatTelegraphs)이 읽는 판정 중심 </summary>
        public Vector3 Center => _center;

        /// <summary>
        /// 직전 지목자를 뺀 후보 중에서 다시 뽑는다. 후보가 없으면(생존자가 직전 지목자
        /// 하나뿐) 기본 대상(전역 어그로)을 쓴다. findNearestExcludingLast는 Phase1State가
        /// 갖고 있는 스캔 로직(SelectTargetExcludingLast)을 그대로 넘겨받는다 - TargetedDash와
        /// 공유하는 물리 스캔이라 이 클래스가 중복 구현하지 않는다
        /// </summary>
        public Transform ResolveAttackTarget(Transform defaultTarget,
            Func<Transform, Transform> findNearestExcludingLast)
        {
            Transform selected = findNearestExcludingLast(_lastMarkedTarget) ?? defaultTarget;
            _lastMarkedTarget = selected;
            return selected;
        }

        /// <summary> 거리 무관 - 선택기에 늘 후보로 넘긴다(기존 동작 그대로) </summary>
        public bool IsDistanceEligible(in BossPatternExecutionSpec spec, float distance)
        {
            return true;
        }

        /// <summary>
        /// 위치 확정 후 잠금 - 예고 중 대상이 움직여도 낙인 중심은 안 따라간다
        /// 러프 VFX 연결(이슈 검증용) - 낙인은 예고 시작 시점에도 신호를 쏜다
        /// </summary>
        public void OnBeginAttack(Transform activeTarget, BossController controller)
        {
            _center = activeTarget.position;

            controller.ServerPlayPatternTelegraphVfx(BossPatternSO.PatternTemplate.MarkedCircle,
                _center + (Vector3.up * VFX_HEIGHT_OFFSET), Vector3.forward);
        }

        /// <summary>
        /// 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
        /// 러프 VFX 연결(이슈 검증용) - 판정 원점(Center)은 그대로 두고 VFX만 띄운다
        /// </summary>
        public void ResolveImpact(in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller,
            uint deflectedDefender)
        {
            controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MarkedCircle,
                _center + (Vector3.up * VFX_HEIGHT_OFFSET), Vector3.forward);

            BossPatternRunner.ExecuteMeleeArc(
                _center,
                Vector3.forward,
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
