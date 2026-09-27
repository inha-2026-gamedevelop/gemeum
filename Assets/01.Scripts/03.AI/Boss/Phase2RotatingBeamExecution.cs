// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈 여우불 광선(RotatingBeam, 패턴 13) 전용 실행 상태. Phase1의 동종 패턴과
    /// 회전 계산 자체는 같지만, 같은 축에서 등간격으로 ROTATING_BEAM_COUNT개를 동시에
    /// 돌린다(회전 속도,폭은 그대로, 회피 가능한 연속 구간만 줄어든다 - 1페이즈보다
    /// 빡빡해지는 지점). 자체 완료 시점을 스스로 판단하는 패턴이라 TickWindup이
    /// PatternTickResult를 반환한다 - FSM 상태(_state),쿨다운,종료 통지는 Phase2State가
    /// Complete를 받은 뒤에만 수행한다
    /// </summary>
    internal sealed class Phase2RotatingBeamExecution
    {
        private const float FULL_SWEEP_DEGREES = 360f;
        private const int   BEAM_COUNT         = 2;

        // GGizmo.Line은 항상 1px라 얇으면 잘 안 보인다 - 십자로 살짝 오프셋한 선을 겹쳐 굵어 보이게 한다
        private const float GIZMO_THICKNESS = 0.15f;

        public PatternTickResult TickWindup(float deltaTime, float stateTimer, float telegraphDuration,
            Transform self, Vector3 lockedForward, in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller)
        {
            float sweepElapsed = stateTimer - telegraphDuration;
            if (sweepElapsed < 0f)
            {
                return PatternTickResult.Continue; // 아직 예고 중, 회전 시작 전
            }

            float sweepDuration = spec.MoveSpeed;
            if (sweepElapsed >= sweepDuration)
            {
                return PatternTickResult.Complete;
            }

            float   sweepAngle     = (sweepElapsed / sweepDuration) * FULL_SWEEP_DEGREES;
            float   beamSpacing    = FULL_SWEEP_DEGREES / BEAM_COUNT;
            Vector3 beamOrigin     = self.position;
            float   damageThisTick = damage * deltaTime;

            for (int i = 0; i < BEAM_COUNT; ++i)
            {
                Vector3 beamDirection = Quaternion.AngleAxis(sweepAngle + (i * beamSpacing), Vector3.up)
                    * lockedForward;
                Vector3 beamEnd = beamOrigin + (beamDirection * spec.Range);

                // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려지고
                // 릴리스 빌드에서는 호출 자체가 통째로 사라진다(GGizmo.Line의 Conditional 속성)
                DrawBeamGizmo(beamOrigin, beamEnd, beamDirection);

                BossPatternRunner.ExecuteMeleeArc(
                    beamOrigin,
                    beamDirection,
                    spec.Range,
                    spec.AngleDegrees,
                    damageThisTick,
                    attackerNetId,
                    playerLayerMask,
                    controller,
                    hitStrength: HitStrength.Light);
            }

            return PatternTickResult.Continue;
        }

        // GGizmo.Line은 GL 즉시 그리기라 두께 지정이 없다 - 십자로 살짝 오프셋한 선 다섯 개를
        // 겹쳐 그려서 굵어 보이게 한다(카메라 각도에 상관없이 보이도록 좌우, 상하 둘 다 오프셋)
        private static void DrawBeamGizmo(Vector3 origin, Vector3 end, Vector3 direction)
        {
            GGizmo.Line(GGizmoChannel.Monster, origin, end, Color.magenta);

            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized * GIZMO_THICKNESS;
            Vector3 up    = Vector3.up * GIZMO_THICKNESS;
            GGizmo.Line(GGizmoChannel.Monster, origin + right, end + right, Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin - right, end - right, Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin + up,    end + up,    Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin - up,    end - up,    Color.magenta);
        }
    }
}
