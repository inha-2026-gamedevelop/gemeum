// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈 표식 순환 처형(MarkedCircle, 패턴 8) 전용 실행 상태. Phase1의 동종 패턴과
    /// 달리 직전 지목자 제외가 없고, 실제 판정 원점도 이 클래스가 아니라 Phase2State의
    /// 공용 반복-대상 스냅샷(_repeatCenters, SnapshotRepeatTargets - ConeBurst/MeleeArc와
    /// 공유하는 인프라라 이번 파일럿에서 옮기지 않는다)이 갖고 있다. 그래서 Center는
    /// 원본 코드에서도 대입만 되고 어디서도 읽히지 않는 죽은 값이다 - 동작을 바꾸지
    /// 않기 위해 그대로 옮겼다. FSM 상태(_state),타이머,attackInstanceId,반복 스케줄은
    /// 전부 Phase2State 소유로 남는다
    /// </summary>
    internal sealed class Phase2MarkedCircleExecution
    {
        // 공격 1회성 - OnBeginAttack에서 매번 다시 계산한다. 원본과 동일하게 대입 전용이다
        private Vector3 _center;

        /// <summary> 거리 무관 - 선택기에 늘 후보로 넘긴다(기존 동작 그대로) </summary>
        public bool IsDistanceEligible(in BossPatternExecutionSpec spec, float distance)
        {
            return true;
        }

        /// <summary> 위치 확정 후 잠금 - 원본과 동일하게 이 값 자체는 이후 아무도 읽지 않는다 </summary>
        public void OnBeginAttack(Transform activeTarget)
        {
            _center = activeTarget.position;
        }

        /// <summary>
        /// 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
        /// impactCenter는 Phase2State가 공용 반복-대상 스냅샷(_repeatCenters[repeatIndex])에서
        /// 읽어 넘긴다 - 실제 판정 원점은 항상 그 값을 쓴다(원본 동작 그대로)
        /// </summary>
        public void ResolveImpact(Vector3 impactCenter, in BossPatternExecutionSpec spec,
            float damage, uint attackerNetId, int playerLayerMask, BossController controller)
        {
            BossPatternRunner.ExecuteMeleeArc(
                impactCenter,
                Vector3.forward,
                spec.ImpactRadius,
                360f,
                damage,
                attackerNetId,
                playerLayerMask,
                controller);
        }
    }
}
