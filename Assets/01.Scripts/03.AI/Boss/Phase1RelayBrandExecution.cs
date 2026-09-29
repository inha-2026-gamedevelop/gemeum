// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈 여우불 낙인 전이(RelayBrand, 패턴 16) 전용 실행 상태. Phase1MarkedCircleExecution과
    /// 같은 원칙 - FSM 상태(_state), 타이머, attackInstanceId, 반복 스케줄은 전부 Phase1State
    /// 소유로 남고, 이 클래스는 3회 릴레이(대상 전이 -> 폭발)에 필요한 전용 상태만 갖는다.
    ///
    /// SO Template은 검증 통과용으로 MarkedCircle(3)을 그대로 쓴다(RotatingBeam/RandomBarrage가
    /// ConeBurst를 빌려 쓰는 것과 같은 이유) - PatternKind 구분은 patternId로만 한다
    ///
    /// Pattern Execution 구조가 신규 패턴 추가에 실제로 맞는지 검증하는 파일럿이다
    /// (2026-09-27, RelayBrand 구조 검증 작업). 반복별 반경·피해 배율은 코드 상수가 아니라
    /// BossPatternSO.PatternEntry.RepeatDamageMultipliers/RepeatImpactRadiusMultipliers,
    /// 즉 GameDB 값이다(밸런스 수치를 코드에 박지 않는다는 코딩 컨벤션 3장을 따른다)
    /// </summary>
    internal sealed class Phase1RelayBrandExecution
    {
        public const int REPEAT_COUNT = 3;

        // 여우불 낙인 전용 - VFX 원점이 지면에 살짝 묻혀서 그만큼 띄운다(MarkedCircle과 동일)
        private const float VFX_HEIGHT_OFFSET = 0.1f;

        // ServerCollectAliveTargets 호출용 재사용 버퍼 - 할당 없음. 생존자 수만큼만 채워진다
        private readonly Transform[] _aliveTargetsBuffer = new Transform[Constants.Network.MAX_PLAYERS];

        // 공격 1회성 - ResolveAttackTarget에서 매번 다시 계산한다(예고 시작 시점에 고정,
        // MarkedCircle의 "위치 확정 후 잠금"과 동일 원칙). 예고가 BeginAttack 시점에 전부
        // 나가야 하는 구조라 반복 중간에 다시 스캔하지 않는다
        private readonly Vector3[] _repeatCenters = new Vector3[REPEAT_COUNT];

        // 반복별 지목 대상의 netId - BossTelegraphView가 TargetNetId로 "당신이 지목됨" 개인
        // UI를 켠다(594줄). Origin만 반복별로 바꾸고 TargetNetId를 1번 대상 것으로 고정하면
        // 2·3번째 폭발 때 실제 대상이 아닌 1번 대상에게 잘못된 개인 경고가 뜬다
        private readonly uint[] _repeatTargetNetIds = new uint[REPEAT_COUNT];

        /// <summary> 거리 무관 - MarkedCircle과 동일하게 늘 후보로 넘긴다 </summary>
        public bool IsDistanceEligible(in BossPatternExecutionSpec spec, float distance)
        {
            return true;
        }

        /// <summary> PublishRepeatTelegraphs가 반복별로 다른 원점을 그리기 위해 읽는 값 </summary>
        public Vector3 GetCenter(int repeatIndex)
        {
            return _repeatCenters[Mathf.Clamp(repeatIndex, 0, REPEAT_COUNT - 1)];
        }

        /// <summary> PublishRepeatTelegraphs가 반복별 개인 경고 대상을 채우기 위해 읽는 값 </summary>
        public uint GetTargetNetId(int repeatIndex)
        {
            return _repeatTargetNetIds[Mathf.Clamp(repeatIndex, 0, REPEAT_COUNT - 1)];
        }

        /// <summary>
        /// 3회 릴레이 대상을 전부 예고 시작 시점에 미리 뽑고 위치를 고정한다. Phase2State의
        /// 표식 순환 처형(패턴 8, AllPlayers)이 쓰는 것과 같은 API(ServerCollectAliveTargets)로
        /// 생존자 전원을 모은 뒤 라운드로빈으로 나눈다 - 새 Targeting 시스템을 만들지 않는다.
        /// "직전 1명만 제외"하는 방식(TargetedDash/MarkedCircle이 쓰는 SelectTargetExcludingLast)은
        /// 인접한 반복끼리만 겹치지 않을 뿐 3명이 다 살아있어도 한 명이 두 번 걸릴 수 있어
        /// 처음엔 그 방식을 썼다가, "3명이면 3명 다 맞아야 한다"는 요구를 확인하고
        /// 이 방식으로 바꿨다(2026-09-27). 생존자가 REPEAT_COUNT보다 적으면 나머지는
        /// 순서를 되풀이한다(1명뿐이면 3번 다 같은 사람, 의도된 fallback)
        /// </summary>
        public Transform ResolveAttackTarget(Transform defaultTarget, BossController controller)
        {
            int collectedCount = controller.ServerCollectAliveTargets(_aliveTargetsBuffer);
            Transform first = null;

            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                Transform selected = (collectedCount > 0)
                    ? _aliveTargetsBuffer[i % collectedCount]
                    : defaultTarget;
                _repeatCenters[i] = selected.position;
                _repeatTargetNetIds[i] = selected.TryGetComponent(out NetworkIdentity identity)
                    ? identity.netId : 0u;
                if (i == 0)
                {
                    first = selected;
                }
            }

            return first;
        }

        /// <summary> 확정된 반복별 중심 3개에 예고 VFX 신호를 보낸다(러프, 이슈 검증용) </summary>
        public void OnBeginAttack(BossController controller)
        {
            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                controller.ServerPlayPatternTelegraphVfx(BossPatternSO.PatternTemplate.MarkedCircle,
                    _repeatCenters[i] + (Vector3.up * VFX_HEIGHT_OFFSET), Vector3.forward);
            }
        }

        /// <summary>
        /// repeatIndex번째 폭발. 위치는 예고 시작 시점에 고정한 중심(GetCenter)이고, 반경·피해는
        /// 반복마다 커진다(PatternEntry.RepeatDamageMultipliers/RepeatImpactRadiusMultipliers,
        /// GameDB 값 - 코드 상수 아님) - MarkedCircleExecution.ResolveImpact와 같은 판정
        /// (360도 ExecuteMeleeArc)에 반복별 스케일만 더했다
        /// </summary>
        public void ResolveImpact(in BossPatternSO.PatternEntry pattern, int repeatIndex,
            in BossPatternExecutionSpec spec, float damage, uint attackerNetId,
            int playerLayerMask, BossController controller, uint deflectedDefender)
        {
            int index = Mathf.Clamp(repeatIndex, 0, REPEAT_COUNT - 1);
            Vector3 center       = _repeatCenters[index];
            float   radius       = spec.ImpactRadius * pattern.GetRepeatImpactRadiusMultiplier(index);
            float   scaledDamage = damage * pattern.GetRepeatDamageMultiplier(index);

            controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MarkedCircle,
                center + (Vector3.up * VFX_HEIGHT_OFFSET), Vector3.forward);

            BossPatternRunner.ExecuteMeleeArc(
                center,
                Vector3.forward,
                radius,
                360f,
                scaledDamage,
                attackerNetId,
                playerLayerMask,
                controller,
                deflectedDefender: deflectedDefender);
        }
    }
}
