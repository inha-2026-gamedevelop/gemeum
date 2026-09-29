// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈 여우불 광선(RotatingBeam, 패턴 11) 전용 실행 상태. ImpactOffsets 기반
    /// 반복 판정을 안 쓰고, 예고가 끝나면 MoveSpeed를 "회전에 걸리는 시간(초)"으로
    /// 재해석해 그 시간 동안 360도를 돈다. Damage는 초당 피해량(DPS)으로 읽어
    /// deltaTime을 곱한 만큼만 적용한다.
    ///
    /// 자체 완료 시점을 스스로 판단하는 패턴이라 TickWindup이 PatternTickResult를
    /// 반환한다 - 그러나 FSM 상태(_state),쿨다운,종료 통지(ServerNotifyPatternEnded)는
    /// 이 클래스가 직접 하지 않는다. Phase1State가 Complete를 받은 뒤에만 수행한다
    /// </summary>
    internal sealed class Phase1RotatingBeamExecution
    {
        private const float FULL_SWEEP_DEGREES = 360f;

        // GGizmo.Line은 항상 1px라 얇으면 잘 안 보인다 - 십자로 살짝 오프셋한 선을 겹쳐 굵어 보이게 한다
        private const float GIZMO_THICKNESS = 0.15f;

        /// <summary>
        /// 예고 구간은 아무 것도 안 하고 Continue만 돌려준다. 회전 시간이 끝나면 Complete를
        /// 돌려주고(FSM 전환은 호출자 몫), 회전 중에는 매 틱 빔 방향을 다시 계산해 판정하고
        /// Continue를 돌려준다. beamDirection은 이 틱의 판정에만 쓰이고 다음 틱까지
        /// 유지할 필요가 없어 지역 변수로 둔다(원본도 필드였지만 외부에서 읽는 곳이 없었다)
        /// </summary>
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

            float sweepAngle = (sweepElapsed / sweepDuration) * FULL_SWEEP_DEGREES;
            Vector3 beamDirection = Quaternion.AngleAxis(sweepAngle, Vector3.up) * lockedForward;

            Vector3 beamOrigin = self.position;
            Vector3 beamEnd    = beamOrigin + (beamDirection * spec.Range);
            // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려지고
            // 릴리스 빌드에서는 호출 자체가 통째로 사라진다(GGizmo.Line의 Conditional 속성)
            DrawBeamGizmo(beamOrigin, beamEnd, beamDirection);

            BossPatternRunner.ExecuteMeleeArc(
                beamOrigin,
                beamDirection,
                spec.Range,
                spec.AngleDegrees,
                damage * deltaTime,
                attackerNetId,
                playerLayerMask,
                controller,
                hitStrength: HitStrength.Light);

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
