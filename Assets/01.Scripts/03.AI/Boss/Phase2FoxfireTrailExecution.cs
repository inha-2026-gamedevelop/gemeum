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
    /// 2페이즈 여우불 잔로(FoxfireTrail, 패턴 17) 전용 실행 상태. 대상 방향으로 직선 이동하며
    /// 일정 간격마다 잔상 위치를 기록하고, 이동이 끝나면 기록된 위치 전부에 Telegraph를 걸어
    /// 순서대로 폭발시킨다. 꼬리 본·IK에 의존하지 않고 여우불 판정만으로 "이동 경로가 후속
    /// 공격 데이터가 된다"는 것을 구현하는 파일럿이다(2026-09-27, Pattern Execution 구조 검증)
    ///
    /// self-managed(PatternTickResult)로 구현했다 - standard repeat-offset flow는 BeginAttack
    /// 시점에 모든 반복의 Origin을 미리 알아야 하는데, 여기서는 "이동이 끝나야" 잔상 위치가
    /// 정해진다. 이동 구간과 폭발 구간이 서로 다른 시간 축(이동은 위치 기반 종료, 폭발은
    /// 고정 간격 종료)을 갖는 것도 표준 흐름 하나로 억지로 합치기 어려운 이유다. FSM 상태
    /// (_state), 쿨다운, 종료 통지는 여전히 Phase2State가 Complete를 받은 뒤에만 수행한다
    /// </summary>
    internal sealed class Phase2FoxfireTrailExecution
    {
        // TEMP: balance value, team confirmation required - 목표 4~6개 중 중간값
        private const int TRAIL_POINT_COUNT = 5;

        // TEMP: balance value, team confirmation required - 이동 중 잔상 채집 간격(초)
        private const float TRAIL_SAMPLE_INTERVAL = 0.35f;

        // TEMP: balance value, team confirmation required - 이동 종료 후 첫 폭발까지 지연(초)
        private const float EXPLOSION_INITIAL_DELAY = 0.6f;

        // TEMP: balance value, team confirmation required - 폭발 간 간격(초)
        private const float EXPLOSION_INTERVAL = 0.35f;

        // 목적지 도달 판정 허용 오차(m) - 밸런스 수치가 아니라 부동소수 이동 오차 보정값
        private const float ARRIVAL_EPSILON = 0.15f;

        private enum Stage
        {
            Moving,    // 직선 이동 중, 잔상 위치를 기록한다
            Exploding, // 이동 종료, 기록된 잔상을 순서대로 터뜨린다
        }

        private readonly Vector3[] _trailPositions = new Vector3[TRAIL_POINT_COUNT];

        private Vector3 _dashDestination; // 공격 1회성 - OnBeginAttack에서 매번 다시 계산한다
        private Stage   _stage;
        private float   _sinceLastSample;
        private float   _explosionTimer;
        private int     _trailCount;
        private int     _explodedCount;

        public void OnBeginAttack(Vector3 selfPosition, Vector3 lockedForward,
            in BossPatternExecutionSpec spec)
        {
            _dashDestination = selfPosition + (lockedForward * spec.MoveDistance);
            _stage           = Stage.Moving;
            _sinceLastSample = 0f;
            _explosionTimer  = 0f;
            _trailCount      = 0;
            _explodedCount   = 0;
        }

        /// <summary>
        /// stateTimer가 telegraphDuration을 넘기 전까지는 RotatingBeam/RandomBarrage와 같은
        /// 공통 관례(공용 Telegraph 하나가 이미 떠 있는 예고 대기 구간)를 그대로 따른다.
        /// 그 뒤 이동 -> 잔상 확정 -> 순차 폭발 순서로 넘어간다
        /// </summary>
        public PatternTickResult TickWindup(float deltaTime, float stateTimer, float telegraphDuration,
            Transform self, in BossPatternExecutionSpec spec, float damage, uint attackerNetId,
            int playerLayerMask, BossController controller, uint attackInstanceId, int patternId)
        {
            float elapsed = stateTimer - telegraphDuration;
            if (elapsed < 0f)
            {
                return PatternTickResult.Continue;
            }

            if (_stage == Stage.Moving)
            {
                TickMoving(deltaTime, self, spec, controller, attackInstanceId, patternId);
                return PatternTickResult.Continue;
            }

            return TickExploding(deltaTime, spec, damage, attackerNetId, playerLayerMask,
                controller, attackInstanceId);
        }

        private void TickMoving(float deltaTime, Transform self, in BossPatternExecutionSpec spec,
            BossController controller, uint attackInstanceId, int patternId)
        {
            self.position = Vector3.MoveTowards(self.position, _dashDestination,
                spec.MoveSpeed * deltaTime);
            bool reachedDestination = Vector3.Distance(self.position, _dashDestination)
                <= ARRIVAL_EPSILON;

            // 마지막 한 자리는 항상 도착 지점용으로 남겨둔다 - 중간 샘플로 다 채워버리면
            // 실제 멈춘 위치가 잔상에 안 남는다
            if ((!reachedDestination) && (_trailCount < (TRAIL_POINT_COUNT - 1)))
            {
                _sinceLastSample += deltaTime;
                if (_sinceLastSample >= TRAIL_SAMPLE_INTERVAL)
                {
                    _trailPositions[_trailCount] = self.position;
                    ++_trailCount;
                    _sinceLastSample = 0f;
                }
            }

            if (!reachedDestination)
            {
                return;
            }

            if (_trailCount < TRAIL_POINT_COUNT)
            {
                _trailPositions[_trailCount] = _dashDestination;
                ++_trailCount;
            }

            BeginTrailTelegraphs(controller, attackInstanceId, patternId);
            _stage = Stage.Exploding;
        }

        // 이동이 끝나야 잔상 위치가 확정되므로, 예고는 여기서 한꺼번에(BeginAttack 시점이 아니라)
        // 띄운다 - "이동 종료 후 경로 전체가 위험지대로 드러난다"는 기획 의도와도 맞다
        private void BeginTrailTelegraphs(BossController controller, uint attackInstanceId,
            int patternId)
        {
            double now = NetworkTime.time;
            for (int i = 0; i < _trailCount; ++i)
            {
                float impactOffset = EXPLOSION_INITIAL_DELAY + (i * EXPLOSION_INTERVAL);
                controller.ServerBeginTelegraph(new BossTelegraphCue
                {
                    AttackInstanceId = attackInstanceId,
                    RepeatIndex      = (byte) i,
                    PatternId        = patternId,
                    TargetNetId      = 0u, // 지면 cue - 특정 플레이어를 지목하지 않는다
                    Origin           = _trailPositions[i],
                    Direction        = Vector3.forward,
                    StartServerTime  = now,
                    ImpactServerTime = now + impactOffset,
                });
            }
        }

        private PatternTickResult TickExploding(float deltaTime, in BossPatternExecutionSpec spec,
            float damage, uint attackerNetId, int playerLayerMask, BossController controller,
            uint attackInstanceId)
        {
            _explosionTimer += deltaTime;
            if (_explodedCount >= _trailCount)
            {
                return PatternTickResult.Complete;
            }

            float dueTime = EXPLOSION_INITIAL_DELAY + (_explodedCount * EXPLOSION_INTERVAL);
            if (_explosionTimer < dueTime)
            {
                return PatternTickResult.Continue;
            }

            ExplodeTrailPoint(_explodedCount, spec, damage, attackerNetId, playerLayerMask,
                controller, attackInstanceId);
            ++_explodedCount;
            return PatternTickResult.Continue;
        }

        private void ExplodeTrailPoint(int index, in BossPatternExecutionSpec spec, float damage,
            uint attackerNetId, int playerLayerMask, BossController controller,
            uint attackInstanceId)
        {
            Vector3 position = _trailPositions[index];

            controller.ServerCancelTelegraph(attackInstanceId, (byte) index, 0u);
            controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.TargetedDash,
                position, Vector3.forward);

            BossPatternRunner.ExecuteMeleeArc(
                position,
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
