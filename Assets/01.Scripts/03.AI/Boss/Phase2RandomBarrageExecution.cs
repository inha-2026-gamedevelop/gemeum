// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.AI
{
    /// <summary>
    /// 2페이즈 여우불 탄막(RandomBarrage, 패턴 14) 전용 실행 상태. Phase1과 회전/판정
    /// 계산 자체는 같지만, 한 번에 나가는 발 수만 3에서 6으로 늘려 2페이즈답게 더
    /// 빡빡하게 뒀다(원래 1페이즈에서 테스트하던 걸 그대로 옮긴 것).
    ///
    /// 자체 완료 시점을 스스로 판단하는 패턴이라 TickWindup이 PatternTickResult를
    /// 반환한다 - 그러나 FSM 상태(_state),쿨다운,종료 통지(ServerNotifyPatternEnded)는
    /// 이 클래스가 직접 하지 않는다. Phase2State가 Complete를 받은 뒤에만 수행한다
    /// </summary>
    internal sealed class Phase2RandomBarrageExecution
    {
        private const int   SHOTS_PER_VOLLEY    = 6;   // 한 번에 동시에 나가는 발 수
        private const float BULLET_TRAVEL_TIME  = 1.2f; // 원점에서 사거리 끝까지 날아가는 시간(초) - 피할 반응 시간
        private const int   TRAVEL_SAMPLE_COUNT = 4;    // 비행 경로를 몇 지점으로 쪼개 판정할지
        private const float AIM_JITTER_DEGREES  = 15f;  // 완전한 조준처럼 안 느껴지게 주는 무작위 흔들림
        private const float BULLET_GIZMO_RADIUS = 0.3f; // 날아가는 구체 기즈모 반지름(m)
        private const float BULLET_HEIGHT       = 1f;   // Self.position이 발밑이라 그만큼 띄운다(m)

        private readonly Transform[] _aliveTargets   = new Transform[Constants.Network.MAX_PLAYERS];
        private readonly Vector3[]   _directions      = new Vector3[SHOTS_PER_VOLLEY];
        private readonly int[]       _nextSampleIndex = new int[SHOTS_PER_VOLLEY];

        // 공격 1회성 - OnBeginAttack에서 매번 -1로 되돌린다(다음 볼리 판단 기준)
        private int _cycleIndex = -1;

        public void OnBeginAttack()
        {
            _cycleIndex = -1;
        }

        /// <summary>
        /// 이미 시작된 발은 끝까지 날아가게 두고, 다음 발을 새로 시작하려는 시점에만
        /// 총 난사 시간을 넘었는지 확인한다 - 안 그러면 마지막 발이 도중에 끊긴다
        /// </summary>
        public PatternTickResult TickWindup(float stateTimer, float telegraphDuration,
            Transform self, Vector3 lockedForward, in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller, System.Random random)
        {
            float barrageElapsed = stateTimer - telegraphDuration;
            if (barrageElapsed < 0f)
            {
                return PatternTickResult.Continue; // 아직 예고 중, 난사 시작 전
            }

            float totalDuration  = spec.MoveDistance;
            float volleyInterval = spec.MoveSpeed;
            int   cycleIndex     = Mathf.FloorToInt(barrageElapsed / volleyInterval);
            float cycleStartTime = cycleIndex * volleyInterval;

            if ((cycleIndex != _cycleIndex) && (cycleStartTime >= totalDuration))
            {
                return PatternTickResult.Complete;
            }

            float cycleElapsed = barrageElapsed - cycleStartTime;

            if (cycleIndex != _cycleIndex)
            {
                _cycleIndex = cycleIndex;
                for (int i = 0; i < SHOTS_PER_VOLLEY; ++i)
                {
                    _nextSampleIndex[i] = 0;
                }

                PickDirections(self, lockedForward, controller, random);
            }

            Vector3 origin      = self.position + (Vector3.up * BULLET_HEIGHT);
            float   travelRatio = Mathf.Clamp01(cycleElapsed / BULLET_TRAVEL_TIME);

            for (int i = 0; i < SHOTS_PER_VOLLEY; ++i)
            {
                Vector3 aimEnd = origin + (_directions[i] * spec.Range);

                if (cycleElapsed < BULLET_TRAVEL_TIME)
                {
                    // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려진다
                    Vector3 bulletPos = Vector3.Lerp(origin, aimEnd, travelRatio);
                    GGizmo.Sphere(GGizmoChannel.Monster, bulletPos, BULLET_GIZMO_RADIUS, Color.yellow);
                }

                // 이 발이 아직 안 지나간 다음 샘플 지점의 시각이 됐으면 그 자리를 판정한다
                while (_nextSampleIndex[i] < TRAVEL_SAMPLE_COUNT)
                {
                    float sampleRatio = (float) (_nextSampleIndex[i] + 1) / TRAVEL_SAMPLE_COUNT;
                    float sampleTime  = BULLET_TRAVEL_TIME * sampleRatio;
                    if (cycleElapsed < sampleTime)
                    {
                        break;
                    }

                    Vector3 samplePos = Vector3.Lerp(origin, aimEnd, sampleRatio);
                    BossPatternRunner.ExecuteMeleeArc(
                        samplePos,
                        Vector3.forward,
                        spec.ImpactRadius,
                        360f,
                        damage,
                        attackerNetId,
                        playerLayerMask,
                        controller,
                        hitStrength: HitStrength.Light);
                    ++_nextSampleIndex[i];
                }
            }

            return PatternTickResult.Continue;
        }

        // 생존 플레이어 방향을 우선 겨냥해 허공에 낭비되는 발을 줄이고, 인원이 모자라면
        // 무작위 방향으로 채운다. 완전한 조준으로 안 느껴지도록 약간의 흔들림을 더한다
        private void PickDirections(Transform self, Vector3 lockedForward, BossController controller,
            System.Random random)
        {
            int aliveCount = controller.ServerCollectAliveTargets(_aliveTargets);
            Vector3 origin = self.position;

            for (int i = 0; i < SHOTS_PER_VOLLEY; ++i)
            {
                Vector3 baseDirection;
                if (i < aliveCount)
                {
                    Vector3 toTarget = _aliveTargets[i].position - origin;
                    toTarget.y = 0f;
                    baseDirection = (toTarget.sqrMagnitude > 0.0001f)
                        ? toTarget.normalized
                        : lockedForward;
                }
                else
                {
                    float randomAngle = (float) (random.NextDouble() * 360.0);
                    baseDirection = Quaternion.AngleAxis(randomAngle, Vector3.up) * Vector3.forward;
                }

                float jitter = ((float) random.NextDouble() - 0.5f) * 2f * AIM_JITTER_DEGREES;
                _directions[i] = Quaternion.AngleAxis(jitter, Vector3.up) * baseDirection;
            }
        }
    }
}
