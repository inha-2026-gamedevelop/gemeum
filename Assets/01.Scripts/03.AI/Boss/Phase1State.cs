// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.AI
{
    /// <summary>
    /// 1페이즈, 가르치는 판. 네 템플릿이 전부 돈다 - 패턴 1(여우불 봉 3연격, MeleeArc, SO의
    /// ImpactOffsets 3개로 진짜 3연타 - 1·2타는 패링 불가, 마지막 3타만 패링 가능,
    /// BOSS_PATTERN_CONTRACT.md 3장), 패턴 3(순간 접근 3연계, TargetedDash, 예고 앞 절반
    /// 추적 DASH_TRACK_RATIO 적용됨, 11.2절), 패턴 4(여우불 낙인, MarkedCircle), 패턴 5(여우
    /// 구슬 충격파, ConeBurst), 패턴 11·12(여우불 광선·탄막, 2페이즈 13·14의 완화판).
    /// 거리 대역별로 근접/중거리/원거리 거리 대역에 맞는 후보와,
    /// 거리 무관인 MarkedCircle을 선택기에 넘긴다. 선택기는 개별 쿨다운과 직전 패턴을 걸러낸
    /// 뒤 남은 후보를 균등 추첨한다. 대상을 고르는 패턴(TargetedDash·MarkedCircle)은 직전
    /// 지목자를 제외한다(SelectTargetExcludingLast, BOSS_PATTERN_CONTRACT.md 5장 어그로 규칙).
    /// 전투 시작 직후엔 오프닝 고정 시퀀스(1 -> 2 -> 4)가 먼저 돈다, 쿨다운을 무시하고 순서대로
    /// 발동한다 - 패턴 2(반월 베기)는 패턴 1과 같은 MeleeArc 판정(각도 동일, 사거리는 5로 확장)이고
    /// 윈드업만 짧고 패링이 안 된다. 1번의 파생이지만 독립 쿨다운(SO 기준 15초)을 갖고
    /// 일반 후보 추첨에도 들어간다.
    /// 순간 접근(3) 직후에는 쿨다운, 거리 추첨을 건너뛰고 패턴 1을 강제로 이어 붙인다
    /// (순간 접근엔 전용 2·3타 애니메이션이 없어서다, TickIdle 참고)
    /// </summary>
    public sealed class Phase1State : BossPhaseStateBase
    {
        /****************************************
        *                Types
        ****************************************/

        private enum AttackState
        {
            Idle,     // 후보 탐색 중
            Windup,   // 예고 진행 중, 패링 창이 열려 있을 수 있다
            Recovery, // 판정 끝, 쿨다운 대기
        }

        // 실행 템플릿 구분. BossPatternSO에는 Kind 필드가 없어(공용 스키마라 손대지 않음)
        // PatternId를 여기서 직접 매핑한다
        private enum PatternKind
        {
            MeleeArc,
            TargetedDash,
            MarkedCircle,
            ConeBurst,
            RotatingBeam,  // ID로만 구분한다 - SO Template은 검증 통과용으로 ConeBurst(4)를 그대로 쓴다
            RandomBarrage, // ID로만 구분한다 - 위와 동일한 이유
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int MELEE_ARC_PATTERN_ID     = 1;  // 여우불 봉 3연격
        private const int DERIVED_MELEE_PATTERN_ID = 2;  // 반월 베기
        private const int TARGETED_DASH_PATTERN_ID = 3;  // 순간 접근 3연계
        private const int MARKED_CIRCLE_PATTERN_ID = 4;  // 여우불 낙인
        private const int CONE_BURST_PATTERN_ID    = 5;  // 여우 구슬 충격파
        private const int ROTATING_BEAM_PATTERN_ID  = 11; // 여우불 광선 - 360도 회전 빔, 2페이즈(13)보다 느리게 돈다
        private const int RANDOM_BARRAGE_PATTERN_ID = 12; // 여우불 탄막 - 랜덤 방향 연속 난사, 2페이즈(14)보다 발 수 적게
        // 위 일곱 다 GameDB.Boss._patterns의 PatternId와 맞춘다

        private const int MAX_CANDIDATE_COUNT     = 7; // 현재 독립 후보 수
        private const int SELECTABLE_PATTERN_COUNT = 7;

        // 여우불 광선(11) 전용 - 360도를 회전 시간(MoveSpeed 필드 재해석)에 걸쳐 돈다.
        // Damage 필드는 다른 패턴과 달리 초당 피해량(DPS)으로 읽어 매 틱 deltaTime을 곱해 적용한다
        private const float ROTATING_BEAM_FULL_SWEEP_DEGREES = 360f;

        // GGizmo.Line은 항상 1px라 얇으면 잘 안 보인다 - 십자로 살짝 오프셋한 선을 겹쳐 굵어 보이게 한다
        private const float ROTATING_BEAM_GIZMO_THICKNESS = 0.15f;

        // 여우불 탄막(12) 전용 - MoveSpeed를 "발사 간격(초)"으로, MoveDistance를 "총 난사
        // 시간(초)"으로 재해석한다. 한 번에 이 수만큼 동시에 쏘고, 생존 플레이어 방향을
        // 우선 겨냥한 뒤 모자란 만큼 무작위로 채운다. 넓은 부채꼴을 한 번에 훑으면 "제자리
        // 지정 공격"처럼 느껴져서, 대신 구체가 사거리 끝까지 날아가는 동안 경로를 여러 지점
        // 으로 쪼개 순서대로 판정한다 - 실제로 지나가는 자리에 있어야 맞는 궤적 판정이다.
        // 2페이즈(14)보다 한 번에 나가는 발 수를 적게 잡아 더 순하게 뒀다
        private const int   BARRAGE_SHOTS_PER_VOLLEY     = 3;
        private const float BARRAGE_BULLET_TRAVEL_TIME   = 1.2f; // 원점에서 사거리 끝까지 날아가는 시간(초) - 피할 반응 시간
        private const int   BARRAGE_TRAVEL_SAMPLE_COUNT  = 4;    // 비행 경로를 몇 지점으로 쪼개 판정할지
        private const float BARRAGE_AIM_JITTER_DEGREES   = 15f;  // 완전한 조준처럼 안 느껴지게 주는 무작위 흔들림
        private const float BARRAGE_BULLET_GIZMO_RADIUS  = 0.3f; // 날아가는 구체 기즈모 반지름(m)
        private const float BARRAGE_BULLET_HEIGHT        = 1f;   // Self.position이 발밑이라 그만큼 띄운다(m)

        // 여우불 낙인(4) 전용 - VFX 원점이 지면에 살짝 묻혀서 그만큼 띄운다. 판정 반경(_markedCenter)은
        // 그대로 두고 표현만 올린다
        private const float MARKED_CIRCLE_VFX_HEIGHT_OFFSET = 0.1f;

        // 예고(TelegraphDuration) 앞 절반은 대상을 따라가고 뒤 절반은 방향을 확정한다
        // 완전 잠금이면 정지 표적을 지나쳐버리고,
        // 완전 추적이면 회피 창이 없어진다 - 그 절충
        private const float DASH_TRACK_RATIO = 0.5f;

        // 대상을 고르는 패턴(TargetedDash, MarkedCircle) 공용 - 직전 지목자를 뺀 후보를 다시
        // 뽑을 때 쓰는 스캔 반경. BossController.RefreshTarget과 같은 반경이다
        private const float TARGET_EXCLUSION_SCAN_RADIUS = 30f;

        /****************************************
        *                Fields
        ****************************************/

        // 오프닝은 추첨 없이 이 순서대로, 쿨다운을 무시하고 발동한다
        private static readonly int[] OPENING_SEQUENCE = { MELEE_ARC_PATTERN_ID, DERIVED_MELEE_PATTERN_ID, MARKED_CIRCLE_PATTERN_ID };

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;
        private static readonly int[] G_SELECTABLE_PATTERN_IDS =
        {
            MELEE_ARC_PATTERN_ID,
            DERIVED_MELEE_PATTERN_ID,
            TARGETED_DASH_PATTERN_ID,
            MARKED_CIRCLE_PATTERN_ID,
            CONE_BURST_PATTERN_ID,
            ROTATING_BEAM_PATTERN_ID,
            RANDOM_BARRAGE_PATTERN_ID,
        };

        private readonly BossPatternSelector.Candidate[] _candidatePatterns =
            new BossPatternSelector.Candidate[MAX_CANDIDATE_COUNT];
        private readonly float[] _patternCooldowns = new float[SELECTABLE_PATTERN_COUNT];
        private readonly System.Random _random = new System.Random();

        // 대상을 고르는 패턴(TargetedDash, MarkedCircle) 공용 - 직전 지목자를 뺀 후보를 다시
        // 뽑을 때 쓰는 재사용 버퍼, 할당 없음
        private readonly Collider[] _exclusionScanBuffer =
            new Collider[Constants.Network.MAX_PLAYERS + 1];

        private Transform _lastDashTarget;   // 패턴 3 전용, 직전 지목자. 다음 지목에서 후보 제외 대상
        private Transform _lastMarkedTarget; // 패턴 4 전용, 직전 낙인 대상. 다음 지목에서 후보 제외 대상

        private AttackState _state              = AttackState.Idle;
        private float       _stateTimer         = 0f;
        private float       _cooldownRemaining  = 0f;
        private int         _openingIndex       = 0; // OPENING_SEQUENCE.Length에 도달하면 오프닝이 끝난 것
        private int         _previousPatternId = 0;

        private Transform                  _activeTarget;
        private IDamageable                _activeTargetStatus;
        private BossPatternSO.PatternEntry _activePattern;
        private PatternKind                _activeKind;
        private BossPatternExecutionSpec   _activeSpec;
        private float                       _activeDamageMultiplier = 1f;
        private Vector3                    _lockedForward;  // 예고 시작 시점에 고정한 방향(방향 완전 잠금)
        private Vector3                    _dashDestination;
        private Vector3                    _dashStartPosition; // TargetedDash 전용, 추적 중 잔여 거리 계산 기준
        private Vector3                    _markedCenter; // MarkedCircle 전용, 예고 시작 시점에 고정
        private uint                       _activeAttackInstanceId;
        private uint _deflectedAttack, _deflectedDefender;
        private byte _deflectedRepeat;
        private byte                       _activeRepeatIndex;
        private double                     _activeAttackStartServerTime;
        private uint                       _activeTargetNetId;
        private readonly float[]           _activeImpactOffsets =
            new float[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private bool                       _activeParried;
        private BossAttackExecution        _activeExecution; // 늦게 도착한 이전 공격의 OnParried를 걸러내는 참조
        private IParryWindowTarget         _openParryTarget;
        private ParryWindowKey             _openParryKey;

        // 여우불 광선(RotatingBeam) 전용
        private Vector3 _beamDirection;

        // 여우불 탄막(RandomBarrage) 전용
        private readonly Transform[] _barrageAliveTargets    = new Transform[Constants.Network.MAX_PLAYERS];
        private readonly Vector3[]   _barrageDirections      = new Vector3[BARRAGE_SHOTS_PER_VOLLEY];
        private readonly int[]       _barrageNextSampleIndex = new int[BARRAGE_SHOTS_PER_VOLLEY];
        private int                  _barrageCycleIndex      = -1;

        /****************************************
        *              Constructor
        ****************************************/

        public Phase1State(Transform self, BossStatus status, BossController controller) : base(self, status)
        {
            _controller      = controller;
            _selfIdentity    = self.GetComponent<NetworkIdentity>();
            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        /****************************************
        *                Methods
        ****************************************/

        public override void Tick(float deltaTime)
        {
            if (IsExited)
            {
                return;
            }

            TickPatternCooldowns(deltaTime);
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= deltaTime;
            }

            switch (_state)
            {
                case AttackState.Idle:
                    TickIdle();
                    break;
                case AttackState.Windup:
                    TickWindup(deltaTime);
                    break;
                case AttackState.Recovery:
                    TickRecovery(deltaTime);
                    break;
            }
        }

        public override void CancelActiveAttack()
        {
            if (_activeAttackInstanceId == 0u)
            {
                return;
            }

            uint attackInstanceId = _activeAttackInstanceId;
            _activeAttackInstanceId  = 0u;
            _activeExecution        = null;
            _activeParried          = true;
            _activeDamageMultiplier = 1f;
            _activeTarget           = null;
            _activeTargetStatus     = null;
            _stateTimer             = 0f;
            _cooldownRemaining      = _controller.ServerGetGlobalCadence(1);
            _state                  = AttackState.Recovery;

            CloseParryWindowIfOpen();
            _controller.ServerCancelTelegraph(attackInstanceId);
        }

        public override bool IsAttackWindup(uint attackInstanceId)
        {
            return (!IsExited) && (attackInstanceId != 0u)
                && (_state == AttackState.Windup)
                && (_activeAttackInstanceId == attackInstanceId);
        }

        public override bool TryBeginForcedAttack(int patternId, int repeatCountOverride)
        {
            if (IsExited || (Target == null) || _controller.ServerIsPatternSealed(patternId) ||
                (!GameDB.Boss.TryGetPattern(patternId, out BossPatternSO.PatternEntry pattern)))
            {
                return false;
            }

            CancelActiveAttack();
            BeginAttack(pattern, KindOf(patternId));
            return true;
        }

        private void TickIdle()
        {
            if (Target == null)
            {
                return;
            }

            if (_openingIndex < OPENING_SEQUENCE.Length)
            {
                // 오프닝은 쿨다운을 안 보고, 추첨도 없이 정해진 순서 그대로 발동한다
                int openingPatternId = OPENING_SEQUENCE[_openingIndex];
                if (!GameDB.Boss.TryGetPattern(openingPatternId, out BossPatternSO.PatternEntry openingPattern))
                {
                    return;
                }

                ++_openingIndex;
                BeginAttack(openingPattern, KindOf(openingPatternId));
                return;
            }

            // 순간 접근(3) 직후엔 쿨다운, 거리 추첨을 전부 건너뛰고 여우불 봉 3연격(1)을 강제로
            // 붙인다 - 팀장 요청(2026-09-15). 순간접근 자체엔 전용 2타, 3타 애니메이션이 없어
            // 반복 판정을 못 주고, 대신 이미 반복 애니메이션이 갖춰진 패턴1로 이어 붙인다
            // 오프닝 이후에도 이 후속 공격만 고정 순서로 실행한다
            if (_previousPatternId == TARGETED_DASH_PATTERN_ID)
            {
                if (GameDB.Boss.TryGetPattern(MELEE_ARC_PATTERN_ID, out BossPatternSO.PatternEntry followUpPattern))
                {
                    BeginAttack(followUpPattern, KindOf(MELEE_ARC_PATTERN_ID));
                    return;
                }
            }

            if (_cooldownRemaining > 0f)
            {
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            int candidateCount = BuildCandidates(distance);
            if (!BossPatternSelector.TrySelect(
                _candidatePatterns,
                candidateCount,
                _previousPatternId,
                _random,
                out BossPatternSelector.Candidate selected))
            {
                return;
            }

            BeginAttack(selected.Pattern, KindOf(selected.Pattern.PatternId));
        }

        private void TickPatternCooldowns(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < _patternCooldowns.Length; ++i)
            {
                _patternCooldowns[i] = Mathf.Max(0f, _patternCooldowns[i] - deltaTime);
            }
        }

        private int BuildCandidates(float distance)
        {
            int candidateCount = 0;
            candidateCount = AddCandidate(candidateCount, MELEE_ARC_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, DERIVED_MELEE_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, TARGETED_DASH_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, MARKED_CIRCLE_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, CONE_BURST_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, ROTATING_BEAM_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, RANDOM_BARRAGE_PATTERN_ID, distance);
            return candidateCount;
        }

        private int AddCandidate(int candidateCount, int patternId, float distance)
        {
            if (candidateCount >= _candidatePatterns.Length)
            {
                return candidateCount;
            }

            if (!GameDB.Boss.TryGetPattern(patternId, out BossPatternSO.PatternEntry pattern))
            {
                return candidateCount;
            }

            int patternSlot = GetPatternSlot(patternId);
            var spec = new BossPatternExecutionSpec(pattern);
            bool isDistanceEligible = IsDistanceEligible(spec, distance);
            _candidatePatterns[candidateCount] = new BossPatternSelector.Candidate(
                pattern,
                (patternSlot < 0) ? 0f : _patternCooldowns[patternSlot],
                isDistanceEligible,
                pattern.IsConditional,
                _controller.ServerIsPatternSealed(patternId));
            ++candidateCount;
            return candidateCount;
        }

        private static bool IsDistanceEligible(BossPatternExecutionSpec spec, float distance)
        {
            switch (spec.Template)
            {
                case BossPatternSO.PatternTemplate.MeleeArc:
                    return distance <= spec.Range;
                case BossPatternSO.PatternTemplate.TargetedDash:
                    return distance > spec.Range;
                case BossPatternSO.PatternTemplate.ConeBurst:
                    return (distance > 0f) && (distance <= spec.Range);
                default:
                    return true;
            }
        }

        private int GetPatternSlot(int patternId)
        {
            for (int i = 0; i < G_SELECTABLE_PATTERN_IDS.Length; ++i)
            {
                if (G_SELECTABLE_PATTERN_IDS[i] == patternId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SetPatternCooldown(int patternId, float cooldown)
        {
            int patternSlot = GetPatternSlot(patternId);
            if (patternSlot < 0)
            {
                return;
            }

            _patternCooldowns[patternSlot] = Mathf.Max(0f, cooldown);
        }

        private static PatternKind KindOf(int patternId)
        {
            if (patternId == TARGETED_DASH_PATTERN_ID)
            {
                return PatternKind.TargetedDash;
            }

            if (patternId == MARKED_CIRCLE_PATTERN_ID)
            {
                return PatternKind.MarkedCircle;
            }

            if (patternId == ROTATING_BEAM_PATTERN_ID)
            {
                return PatternKind.RotatingBeam;
            }

            if (patternId == RANDOM_BARRAGE_PATTERN_ID)
            {
                return PatternKind.RandomBarrage;
            }

            if (patternId == CONE_BURST_PATTERN_ID)
            {
                return PatternKind.ConeBurst;
            }

            return PatternKind.MeleeArc;
        }

        // 직전 지목자를 뺀 생존 플레이어 중 가장 가까운 대상을 고른다. 후보가 없으면(생존자가
        // 직전 지목자 하나뿐) null을 돌려주고 호출부가 Target(전역 어그로 대상)으로 대체한다 -
        // "후보가 0이 되면 직전 지목자 제외를 먼저 푼다"는 어그로 규칙의 필터 우선순위
        // TargetedDash(패턴 3), MarkedCircle(패턴 4) 둘 다 이 헬퍼를 쓴다
        private Transform SelectTargetExcludingLast(Transform lastTarget)
        {
            int count = Physics.OverlapSphereNonAlloc(Self.position, TARGET_EXCLUSION_SCAN_RADIUS,
                _exclusionScanBuffer, _playerLayerMask);

            Transform nearestExcludingLast = null;
            float     nearestDistance      = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                if ((!_exclusionScanBuffer[i].TryGetComponent(out IDamageable damageable)) || (damageable.IsDead))
                {
                    continue;
                }

                Transform candidate = _exclusionScanBuffer[i].transform;
                if (ReferenceEquals(candidate, lastTarget))
                {
                    continue;
                }

                float distance = Vector3.Distance(Self.position, candidate.position);
                if (distance < nearestDistance)
                {
                    nearestDistance      = distance;
                    nearestExcludingLast = candidate;
                }
            }

            return nearestExcludingLast;
        }

        private void BeginAttack(BossPatternSO.PatternEntry pattern, PatternKind kind)
        {
            _activeSpec = new BossPatternExecutionSpec(pattern);
            _activeKind = ToKind(_activeSpec.Template, kind);
            if (pattern.PatternId == ROTATING_BEAM_PATTERN_ID)
            {
                // SO Template은 검증 통과용 ConeBurst라 ToKind가 못 잡는다, ID로 덮어쓴다
                _activeKind = PatternKind.RotatingBeam;
            }
            else if (pattern.PatternId == RANDOM_BARRAGE_PATTERN_ID)
            {
                _activeKind = PatternKind.RandomBarrage;
            }

            _barrageCycleIndex = -1;

            // TargetedDash(패턴 3), MarkedCircle(패턴 4)은 전역 어그로 대상(Target)이 아니라
            // 직전 지목자를 뺀 후보 중에서 다시 뽑는다 - 안 그러면 같은 사람만 계속 지목당해
            // 3인 역할 분리가 안 된다
            if (_activeKind == PatternKind.TargetedDash)
            {
                _activeTarget   = SelectTargetExcludingLast(_lastDashTarget) ?? Target;
                _lastDashTarget = _activeTarget;
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                _activeTarget     = SelectTargetExcludingLast(_lastMarkedTarget) ?? Target;
                _lastMarkedTarget = _activeTarget;
            }
            else
            {
                _activeTarget = Target;
            }

            _activeTarget.TryGetComponent(out _activeTargetStatus);
            _activePattern          = pattern;
            _activeDamageMultiplier = _controller.ServerGetAttackDamageMultiplier();
            _previousPatternId      = pattern.PatternId;
            _activeParried          = false;
            _activeExecution        = null;
            _activeAttackInstanceId = _controller.ServerNextAttackInstanceId();
            _activeRepeatIndex      = 0;
            _activeAttackStartServerTime = NetworkTime.time;
            _activeSpec.BuildImpactSchedule(pattern, _random, _activeImpactOffsets);
            SetPatternCooldown(pattern.PatternId, pattern.Cooldown);
            _stateTimer             = 0f;
            _state                  = AttackState.Windup;

            Vector3 direction = _activeTarget.position - Self.position;
            direction.y = 0f;
            direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : Self.forward;

            _lockedForward = direction; // 방향 완전 잠금 - 예고 중 대상이 움직여도 안 따라간다
            Self.rotation  = Quaternion.LookRotation(_lockedForward); // 공격마다 대상 방향으로 즉시 회전

            if (_activeKind == PatternKind.TargetedDash)
            {
                _dashStartPosition = Self.position;
                _dashDestination   = Self.position + (direction * _activeSpec.MoveDistance);
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                // 위치 확정 후 잠금 - 예고 중 대상이 움직여도 낙인 중심은 안 따라간다
                _markedCenter = _activeTarget.position;

                // 러프 VFX 연결(이슈 검증용) - 낙인은 예고 시작 시점에도 신호를 쏜다.
                // 판정 지점(_markedCenter)은 그대로 두고 VFX만 지면에 안 묻히게 살짝 띄운다
                _controller.ServerPlayPatternTelegraphVfx(BossPatternSO.PatternTemplate.MarkedCircle,
                    _markedCenter + (Vector3.up * MARKED_CIRCLE_VFX_HEIGHT_OFFSET), Vector3.forward);
            }

            _activeTargetNetId = _activeTarget.TryGetComponent(out NetworkIdentity targetIdentity)
                ? targetIdentity.netId : 0u;

            // 여우불 광선은 360도로 돌고 여우불 탄막은 매 발 방향이 바뀌어 고정된 부채꼴
            // 장판을 못 그리지만, 신호 자체는 그대로 보내 모션 트리거(BossAnimationView)가
            // 돌게 한다 - 장판만 안 보이게 하는 건 BossTelegraphView 쪽 숨김 목록으로 처리한다
            PublishRepeatTelegraphs();

            TryOpenParryWindow(pattern, _activeRepeatIndex);
        }

        private static PatternKind ToKind(BossPatternSO.PatternTemplate template,
            PatternKind fallback)
        {
            switch (template)
            {
                case BossPatternSO.PatternTemplate.TargetedDash: return PatternKind.TargetedDash;
                case BossPatternSO.PatternTemplate.MarkedCircle: return PatternKind.MarkedCircle;
                case BossPatternSO.PatternTemplate.ConeBurst:    return PatternKind.ConeBurst;
                case BossPatternSO.PatternTemplate.MeleeArc:     return PatternKind.MeleeArc;
                default:                                         return fallback;
            }
        }

        private void PublishRepeatTelegraphs()
        {
            Vector3 cueOrigin = (_activeKind == PatternKind.MarkedCircle)
                ? _markedCenter : Self.position;
            for (int i = 0; i < _activeSpec.RepeatCount; ++i)
            {
                float impactOffset = _activeImpactOffsets[i];
                float startOffset = (i == 0)
                    ? 0f
                    : _activeImpactOffsets[i - 1];
                _controller.ServerBeginTelegraph(new BossTelegraphCue
                {
                    AttackInstanceId = _activeAttackInstanceId,
                    RepeatIndex      = (byte) i,
                    PatternId        = _activePattern.PatternId,
                    TargetNetId      = _activeTargetNetId,
                    Origin           = cueOrigin,
                    Direction        = _lockedForward,
                    StartServerTime  = _activeAttackStartServerTime + startOffset,
                    ImpactServerTime = _activeAttackStartServerTime + impactOffset,
                });
            }
        }

        private void TickWindup(float deltaTime)
        {
            if ((_activeTarget == null) || ((_activeTargetStatus != null) &&
                (_activeTargetStatus.IsDead ||
                    ((_activeTargetStatus is IBossSwallowTarget swallowed) && swallowed.IsSwallowed))))
            {
                CancelActiveAttack();
                return;
            }

            if (_activeKind == PatternKind.RotatingBeam)
            {
                TickRotatingBeam(deltaTime);
                return;
            }

            if (_activeKind == PatternKind.RandomBarrage)
            {
                TickRandomBarrage(deltaTime);
                return;
            }

            if (_activeKind == PatternKind.TargetedDash)
            {
                float trackDuration = _activePattern.TelegraphDuration * DASH_TRACK_RATIO;
                if (_stateTimer < trackDuration)
                {
                    // 앞 절반 - 매 틱 대상 방향으로 갱신하되, 남은 거리(총 MoveDistance 유지)만큼만 목적지를 다시 잡는다
                    Vector3 direction = _activeTarget.position - Self.position;
                    direction.y = 0f;
                    direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : _lockedForward;
                    _lockedForward = direction;

                    float traveled  = Vector3.Distance(_dashStartPosition, Self.position);
                    float remaining = Mathf.Max(0f, _activeSpec.MoveDistance - traveled);
                    _dashDestination = Self.position + (direction * remaining);
                }
                // 뒤 절반은 위에서 마지막으로 갱신된 _dashDestination을 그대로 따라간다(방향 확정)

                Self.position = Vector3.MoveTowards(Self.position, _dashDestination,
                    _activeSpec.MoveSpeed * deltaTime);
            }

            _stateTimer += deltaTime;
            float nextImpactOffset = _activeImpactOffsets[_activeRepeatIndex];
            if (_stateTimer < nextImpactOffset)
            {
                return;
            }

            CloseParryWindowIfOpen();

            if (!_activeParried)
            {
                ResolveImpact();
            }

            _controller.ServerCancelTelegraph(_activeAttackInstanceId,
                _activeRepeatIndex, _activeTargetNetId);
            _activeExecution = null;

            ++_activeRepeatIndex;
            if (_activeRepeatIndex < _activeSpec.RepeatCount)
            {
                _activeParried = false;
                TryOpenParryWindow(_activePattern, _activeRepeatIndex);
                return;
            }

            // 회복으로 넘어가는 이 지점이 패턴 종료다. 반복 패턴은 마지막 판정에서 한 번만 온다
            _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);

            _stateTimer        = 0f;
            _cooldownRemaining = _controller.ServerGetGlobalCadence(1);
            _state             = AttackState.Recovery;
        }

        // 여우불 광선 전용 - ImpactOffsets 기반 반복 판정을 안 쓰고, 예고가 끝나면
        // MoveSpeed를 "회전에 걸리는 시간(초)"으로 재해석해 그 시간 동안 360도를 돈다
        // (Template이 ConeBurst라 실제 이동 속도로는 안 쓰인다). ActiveDuration은 다른
        // 패턴과 똑같이 회전이 끝난 뒤 회복 상태로 보여주는 짧은 버퍼로 남겨둔다.
        // Damage 필드는 초당 피해량(DPS)으로 읽어 deltaTime을 곱한 만큼만 적용한다
        private void TickRotatingBeam(float deltaTime)
        {
            _stateTimer += deltaTime;

            float sweepElapsed = _stateTimer - _activePattern.TelegraphDuration;
            if (sweepElapsed < 0f)
            {
                return; // 아직 예고 중, 회전 시작 전
            }

            float sweepDuration = _activeSpec.MoveSpeed;
            if (sweepElapsed >= sweepDuration)
            {
                // 회전 시작 전 딱 한 번만 보낸 예고 신호를 여기서 닫는다 - 안 닫으면
                // 동시 예고 목록에 계속 남아 다음 발동들이 상한에 걸린다
                _controller.ServerCancelTelegraph(_activeAttackInstanceId);
                _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);
                _stateTimer        = 0f;
                _cooldownRemaining = _controller.ServerGetGlobalCadence(1);
                _state             = AttackState.Recovery;
                return;
            }

            float sweepAngle = (sweepElapsed / sweepDuration) * ROTATING_BEAM_FULL_SWEEP_DEGREES;
            _beamDirection = Quaternion.AngleAxis(sweepAngle, Vector3.up) * _lockedForward;

            Vector3 beamOrigin = Self.position;
            Vector3 beamEnd    = beamOrigin + (_beamDirection * _activeSpec.Range);
            // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려지고
            // 릴리스 빌드에서는 호출 자체가 통째로 사라진다(GGizmo.Line의 Conditional 속성)
            DrawBeamGizmo(beamOrigin, beamEnd, _beamDirection);

            uint  attackerNetId   = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float damagePerSecond = _activePattern.Damage * _activeDamageMultiplier;
            BossPatternRunner.ExecuteMeleeArc(
                beamOrigin,
                _beamDirection,
                _activeSpec.Range,
                _activeSpec.AngleDegrees,
                damagePerSecond * deltaTime,
                attackerNetId,
                _playerLayerMask,
                _controller,
                hitStrength: HitStrength.Light);
        }

        // GGizmo.Line은 GL 즉시 그리기라 두께 지정이 없다 - 십자로 살짝 오프셋한 선 다섯 개를
        // 겹쳐 그려서 굵어 보이게 한다(카메라 각도에 상관없이 보이도록 좌우, 상하 둘 다 오프셋)
        private static void DrawBeamGizmo(Vector3 origin, Vector3 end, Vector3 direction)
        {
            GGizmo.Line(GGizmoChannel.Monster, origin, end, Color.magenta);

            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized * ROTATING_BEAM_GIZMO_THICKNESS;
            Vector3 up    = Vector3.up * ROTATING_BEAM_GIZMO_THICKNESS;
            GGizmo.Line(GGizmoChannel.Monster, origin + right, end + right, Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin - right, end - right, Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin + up,    end + up,    Color.magenta);
            GGizmo.Line(GGizmoChannel.Monster, origin - up,    end - up,    Color.magenta);
        }

        // 여우불 탄막 전용 - ImpactOffsets 기반 반복 판정을 안 쓰고, MoveSpeed를 "발사
        // 간격(초)"으로, MoveDistance를 "총 난사 시간(초)"으로 재해석한다. 매 간격마다
        // BARRAGE_SHOTS_PER_VOLLEY발을 동시에 쏘는데, 각 발은 원점에서 사거리 끝까지
        // BARRAGE_BULLET_TRAVEL_TIME 동안 날아가는 구체로 보여주고, 그 경로를
        // BARRAGE_TRAVEL_SAMPLE_COUNT개 지점으로 쪼개 지나가는 순서대로 판정한다 -
        // 사거리 전체를 한 번에 훑지 않고 실제로 지나는 자리에 있어야 맞는다.
        // ImpactRadius는 탄환 하나의 판정 반경, Damage는 발당(정확히는 지점당) 피해량이다
        private void TickRandomBarrage(float deltaTime)
        {
            _stateTimer += deltaTime;

            float barrageElapsed = _stateTimer - _activePattern.TelegraphDuration;
            if (barrageElapsed < 0f)
            {
                return; // 아직 예고 중, 난사 시작 전
            }

            float totalDuration   = _activeSpec.MoveDistance;
            float volleyInterval  = _activeSpec.MoveSpeed;
            int   cycleIndex      = Mathf.FloorToInt(barrageElapsed / volleyInterval);
            float cycleStartTime  = cycleIndex * volleyInterval;

            // 이미 시작된 발은 끝까지 날아가게 두고, 다음 발을 새로 시작하려는 시점에만
            // 총 난사 시간을 넘었는지 확인한다 - 안 그러면 마지막 발이 도중에 끊긴다
            if ((cycleIndex != _barrageCycleIndex) && (cycleStartTime >= totalDuration))
            {
                // 난사 시작 전 딱 한 번만 보낸 예고 신호를 여기서 닫는다 - 안 닫으면
                // 동시 예고 목록에 계속 남아 다음 발동들이 상한에 걸린다
                _controller.ServerCancelTelegraph(_activeAttackInstanceId);
                _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);
                _stateTimer        = 0f;
                _cooldownRemaining = _controller.ServerGetGlobalCadence(1);
                _state             = AttackState.Recovery;
                return;
            }

            float cycleElapsed = barrageElapsed - cycleStartTime;

            if (cycleIndex != _barrageCycleIndex)
            {
                _barrageCycleIndex = cycleIndex;
                for (int i = 0; i < BARRAGE_SHOTS_PER_VOLLEY; ++i)
                {
                    _barrageNextSampleIndex[i] = 0;
                }

                PickBarrageDirections();
            }

            Vector3 origin        = Self.position + (Vector3.up * BARRAGE_BULLET_HEIGHT);
            float   travelRatio   = Mathf.Clamp01(cycleElapsed / BARRAGE_BULLET_TRAVEL_TIME);
            uint    attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float   damage        = _activePattern.Damage * _activeDamageMultiplier;

            for (int i = 0; i < BARRAGE_SHOTS_PER_VOLLEY; ++i)
            {
                Vector3 aimEnd = origin + (_barrageDirections[i] * _activeSpec.Range);

                if (cycleElapsed < BARRAGE_BULLET_TRAVEL_TIME)
                {
                    // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려진다
                    Vector3 bulletPos = Vector3.Lerp(origin, aimEnd, travelRatio);
                    GGizmo.Sphere(GGizmoChannel.Monster, bulletPos, BARRAGE_BULLET_GIZMO_RADIUS, Color.yellow);
                }

                // 이 발이 아직 안 지나간 다음 샘플 지점의 시각이 됐으면 그 자리를 판정한다
                while (_barrageNextSampleIndex[i] < BARRAGE_TRAVEL_SAMPLE_COUNT)
                {
                    float sampleRatio = (float) (_barrageNextSampleIndex[i] + 1) / BARRAGE_TRAVEL_SAMPLE_COUNT;
                    float sampleTime  = BARRAGE_BULLET_TRAVEL_TIME * sampleRatio;
                    if (cycleElapsed < sampleTime)
                    {
                        break;
                    }

                    Vector3 samplePos = Vector3.Lerp(origin, aimEnd, sampleRatio);
                    BossPatternRunner.ExecuteMeleeArc(
                        samplePos,
                        Vector3.forward,
                        _activeSpec.ImpactRadius,
                        360f,
                        damage,
                        attackerNetId,
                        _playerLayerMask,
                        _controller,
                        hitStrength: HitStrength.Light);
                    ++_barrageNextSampleIndex[i];
                }
            }
        }

        // 생존 플레이어 방향을 우선 겨냥해 허공에 낭비되는 발을 줄이고, 인원이 모자라면
        // 무작위 방향으로 채운다. 완전한 조준으로 안 느껴지도록 약간의 흔들림을 더한다
        private void PickBarrageDirections()
        {
            int aliveCount = _controller.ServerCollectAliveTargets(_barrageAliveTargets);
            Vector3 origin = Self.position;

            for (int i = 0; i < BARRAGE_SHOTS_PER_VOLLEY; ++i)
            {
                Vector3 baseDirection;
                if (i < aliveCount)
                {
                    Vector3 toTarget = _barrageAliveTargets[i].position - origin;
                    toTarget.y = 0f;
                    baseDirection = (toTarget.sqrMagnitude > 0.0001f)
                        ? toTarget.normalized
                        : _lockedForward;
                }
                else
                {
                    float randomAngle = (float) (_random.NextDouble() * 360.0);
                    baseDirection = Quaternion.AngleAxis(randomAngle, Vector3.up) * Vector3.forward;
                }

                float jitter = ((float) _random.NextDouble() - 0.5f) * 2f * BARRAGE_AIM_JITTER_DEGREES;
                _barrageDirections[i] = Quaternion.AngleAxis(jitter, Vector3.up) * baseDirection;
            }
        }

        private void ResolveImpact()
        {
            uint attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float damage       = _activePattern.Damage * _activeDamageMultiplier;

            if (_activeKind == PatternKind.TargetedDash)
            {
                // 러프 VFX 연결(이슈 검증용)
                _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.TargetedDash, Self.position, Self.forward);

                // 도착 지점 기준 전방위 판정이라 각도 제한이 필요 없어 360도로 둔다
                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    Self.forward,
                    _activeSpec.ImpactRadius,
                    360f,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller,
                    deflectedDefender: (_deflectedAttack == _activeAttackInstanceId &&
                        _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                // 러프 VFX 연결(이슈 검증용) - 판정 원점(_markedCenter)은 그대로 두고 VFX만 띄운다
                _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MarkedCircle,
                    _markedCenter + (Vector3.up * MARKED_CIRCLE_VFX_HEIGHT_OFFSET), Vector3.forward);

                // 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
                BossPatternRunner.ExecuteMeleeArc(
                    _markedCenter,
                    Vector3.forward,
                    _activeSpec.ImpactRadius,
                    360f,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller,
                    deflectedDefender: (_deflectedAttack == _activeAttackInstanceId &&
                        _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u);
                return;
            }

            if (_activeKind == PatternKind.ConeBurst)
            {
                // 러프 VFX 연결(이슈 검증용)
                _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.ConeBurst, Self.position, _lockedForward);

                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    _lockedForward,
                    _activeSpec.Range,
                    _activeSpec.AngleDegrees,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller,
                    deflectedDefender: (_deflectedAttack == _activeAttackInstanceId &&
                        _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u);
                return;
            }

            // MeleeArc. 방향은 예고 시작 시점에 잠근 값을 쓴다(방향 완전 잠금)
            // 러프 VFX 연결(이슈 검증용) - 반월 베기(파생 패턴)도 여기로 들어와 같은 VFX를 쓴다
            _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MeleeArc, Self.position, _lockedForward);

            BossPatternRunner.ExecuteMeleeArc(
                Self.position,
                _lockedForward,
                _activeSpec.Range,
                _activeSpec.AngleDegrees,
                damage,
                attackerNetId,
                _playerLayerMask,
                _controller,
                deflectedDefender: (_deflectedAttack == _activeAttackInstanceId &&
                    _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u);
        }

        private void TickRecovery(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer >= _activePattern.ActiveDuration)
            {
                _activeAttackInstanceId = 0u;
                _activeTarget          = null;
                _activeTargetStatus    = null;
                _state = AttackState.Idle;
            }
        }

        // 현재 실행만 패링 결과를 받는다. 이전 공격의 늦은 통지는 버린다
        private void HandleAttackParried(BossAttackExecution execution, uint parriedByNetId)
        {
            if ((IsExited) || (execution == null) || (!ReferenceEquals(execution, _activeExecution))
                || (_state != AttackState.Windup))
            {
                return;
            }

            if (execution.IsDeflect)
            {
                _deflectedAttack = execution.AttackInstanceId;
                _deflectedRepeat = execution.RepeatIndex;
                _deflectedDefender = parriedByNetId;
                return;
            }
            uint attackInstanceId = _activeAttackInstanceId;
            CancelActiveAttack();
            _controller.ServerRecordParryCounter(parriedByNetId, attackInstanceId);
        }

        private void TryOpenParryWindow(BossPatternSO.PatternEntry pattern, byte repeatIndex)
        {
            if ((!_activeSpec.IsRepeatParryable(pattern, repeatIndex)) &&
                (!_activeSpec.IsRepeatDeflectable(pattern, repeatIndex)) &&
                (!_controller.ServerIsTransferredParryRepeat(pattern.PatternId, repeatIndex)))
            {
                return;
            }

            if ((_activeTarget == null) || (_selfIdentity == null))
            {
                return;
            }

            if (!_activeTarget.TryGetComponent(out IParryWindowTarget parryTarget))
            {
                return;
            }

            ParryWindowKey key = new ParryWindowKey(_selfIdentity.netId,
                _controller.ServerNextParryWindowId());

            double now        = NetworkTime.time;
            double impactTime = _activeAttackStartServerTime +
                _activeImpactOffsets[repeatIndex];

            // 기본 창 길이는 비대상 섬광 근사와 같은 설정값을 쓴다. 로드아웃 보너스는 ParryController가 더한다
            BossAttackExecution execution = new BossAttackExecution(
                GameDB.Combat.ParryWindowDuration, _activeAttackInstanceId, repeatIndex,
                HandleAttackParried);
            uint sequenceId = _activeAttackInstanceId;
            execution.SequenceActive = () => IsAttackWindup(sequenceId);
            execution.IsDeflect = _activeSpec.IsRepeatDeflectable(pattern, repeatIndex) &&
                !_controller.ServerIsTransferredParryRepeat(pattern.PatternId, repeatIndex);

            if (parryTarget.OpenParryWindow(execution, key, now, impactTime))
            {
                _activeExecution = execution;
                _openParryTarget = parryTarget;
                _openParryKey    = key;
            }
        }

        private void CloseParryWindowIfOpen()
        {
            if (_openParryTarget == null)
            {
                return;
            }

            IParryWindowTarget target = _openParryTarget;
            ParryWindowKey key = _openParryKey;
            _openParryTarget = null;
            _openParryKey    = default;

            if ((target is UnityEngine.Object unityTarget) && (unityTarget == null))
            {
                return;
            }

            target.CloseParryWindow(key);
        }
    }
}
