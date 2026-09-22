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
    /// 2페이즈, 시험하는 판. Phase1State의 네 템플릿을 SO 형상으로 재사용한다.
    /// 패턴 6은 3연타 마지막 타 패링, 8은 살아 있는 플레이어 순차 처형, 9는 3단 펄스다.
    /// 꼬리 봉인 뒤에는 7에 차사 패링 창, 9 종료에 무녀 공명을 이관하고 9의 신궁 중단과 공존한다.
    /// 조건부 10은 남은 원혼 수를 반복 수로 snapshot해 이미 수락된 백귀야행을 끝까지 실행한다.
    /// 일반 선택은 거리, 쿨다운, 직전 패턴, 조건부, 꼬리 봉인을 필터링한다.
    /// </summary>
    public sealed class Phase2State : BossPhaseStateBase
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

        // 여우불 진(15) 슬롯 하나의 상태 - 일반 AttackState와 별개로 흐른다
        private struct GroundZoneInstance
        {
            public bool  IsActive;
            public int   LandmarkIndex;
            public float Timer;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int MELEE_ARC_PATTERN_ID     = 6;  // 거대 꼬리치기
        private const int TARGETED_DASH_PATTERN_ID = 7;  // 마수 돌진
        private const int MARKED_CIRCLE_PATTERN_ID = 8;  // 표식 순환 처형
        private const int CONE_BURST_PATTERN_ID    = 9;  // 연쇄 부채꼴 3단
        private const int ROTATING_BEAM_PATTERN_ID  = 13; // 여우불 광선 - 360도 회전 빔
        private const int RANDOM_BARRAGE_PATTERN_ID = 14; // 여우불 탄막 - 랜덤 방향 연속 난사
        // 위 여섯 다 GameDB.Boss._patterns의 PatternId와 맞춘다. 10번(백귀야행)은
        // 조건부 발동(꼬리 상태)이라 쿨다운 기반 선택기 대상이 아니다 - 이 슬라이스에서 다루지 않는다

        private const int MAX_CANDIDATE_COUNT      = 6; // 현재 독립 후보 수, 패턴 10은 조건부 발동
        private const int SELECTABLE_PATTERN_COUNT = 6;

        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

        // 여우불 광선(13) 전용 - 360도를 ActiveDuration에 걸쳐 회전한다. Damage 필드는
        // 다른 패턴과 달리 초당 피해량(DPS)으로 읽는다, 매 틱 deltaTime을 곱해 적용한다
        private const float ROTATING_BEAM_FULL_SWEEP_DEGREES = 360f;

        // 2페이즈는 같은 축에서 등간격으로 여러 개를 같이 돌린다 - 회전 속도, 폭은 1페이즈와 동일하게
        // 두고 회피 가능한 연속 구간만 줄인다(사용자 요청, 2026-09-21)
        private const int ROTATING_BEAM_COUNT = 2;

        // GGizmo.Line은 항상 1px라 얇으면 잘 안 보인다 - 십자로 살짝 오프셋한 선을 겹쳐 굵어 보이게 한다
        private const float ROTATING_BEAM_GIZMO_THICKNESS = 0.15f;

        // 여우불 탄막(14) 전용 - 원래 1페이즈(12)에서 테스트하던 걸 그대로 옮기면서 한 번에
        // 나가는 발 수만 3에서 6으로 늘려 2페이즈답게 더 빡빡하게 만들었다. MoveSpeed를
        // "발사 간격(초)"으로, MoveDistance를 "총 난사 시간(초)"으로 재해석한다. 생존
        // 플레이어 방향을 우선 겨냥한 뒤 모자란 만큼 무작위로 채운다. 넓은 부채꼴을 한 번에
        // 훑으면 "제자리 지정 공격"처럼 느껴져서, 대신 구체가 사거리 끝까지 날아가는 동안
        // 경로를 여러 지점으로 쪼개 순서대로 판정한다 - 실제로 지나가는 자리에 있어야 맞는다
        private const int   BARRAGE_SHOTS_PER_VOLLEY     = 6;
        private const float BARRAGE_BULLET_TRAVEL_TIME   = 1.2f; // 원점에서 사거리 끝까지 날아가는 시간(초) - 피할 반응 시간
        private const int   BARRAGE_TRAVEL_SAMPLE_COUNT  = 4;    // 비행 경로를 몇 지점으로 쪼개 판정할지
        private const float BARRAGE_AIM_JITTER_DEGREES   = 15f;  // 완전한 조준처럼 안 느껴지게 주는 무작위 흔들림
        private const float BARRAGE_BULLET_GIZMO_RADIUS  = 0.3f; // 날아가는 구체 기즈모 반지름(m)
        private const float BARRAGE_BULLET_HEIGHT        = 1f;   // Self.position이 발밑이라 그만큼 띄운다(m)

        // 여우불 진(15) 전용 - 일반 선택기를 안 거치는 독립 배경 시스템이다. 보스가 다른 패턴을
        // 진행하는 동안에도 계속 흐르며, 최대 GROUND_ZONE_MAX_CONCURRENT개까지 동시에 유지된다.
        // TelegraphDuration을 "생성 전 경고 시간(초)"으로, ActiveDuration을 "생성 후 유지 시간(초)"으로,
        // Cooldown을 "다음 장판 배치까지 대기 시간(초)"으로, Damage를 초당 피해량(DPS)으로,
        // Range를 반경(m)으로 재해석한다. Template은 검증 통과용으로 ConeBurst(4)를 그대로 쓴다.
        // Cooldown 수치는 아직 팀 확정치가 아니다 - SO 값만 바꾸면 되니 밸런싱 중 조정 예정
        private const int GROUND_ZONE_PATTERN_ID     = 15;
        private const int GROUND_ZONE_MAX_CONCURRENT = 2;

        // 마당 돌무더기 4기 좌표(BOSS_DESIGN.md 14장) - "어디 근처"를 말로 지칭할 수 있는
        // 비대칭 랜드마크라 이 지점들을 그대로 장판 후보 지점으로 재사용한다
        private static readonly Vector3[] GROUND_ZONE_LANDMARKS =
        {
            new Vector3(-7.5f,  0f, 5.0f),   // 가 - 북서
            new Vector3(10.7f,  0f, 2.5f),   // 나 - 동
            new Vector3(8.0f,   0f, -10.25f), // 다 - 남동
            new Vector3(-14.0f, 0f, -5.4f),  // 라 - 남서
        };

        // 최초 배치까지의 지연 - 2페이즈 진입 직후 바로 깔리면 다른 패턴과 몰려 보인다
        private const float GROUND_ZONE_INITIAL_SPAWN_DELAY = 8f;

        private const int GROUND_ZONE_GIZMO_SEGMENTS = 24; // 원 외곽선을 그릴 선분 수

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;
        private static readonly int[] G_SELECTABLE_PATTERN_IDS =
        {
            MELEE_ARC_PATTERN_ID,
            TARGETED_DASH_PATTERN_ID,
            MARKED_CIRCLE_PATTERN_ID,
            CONE_BURST_PATTERN_ID,
            ROTATING_BEAM_PATTERN_ID,
            RANDOM_BARRAGE_PATTERN_ID,
        };

        private readonly BossPatternSelector.Candidate[] _candidatePatterns =
            new BossPatternSelector.Candidate[MAX_CANDIDATE_COUNT];
        private readonly float[]      _patternCooldowns = new float[SELECTABLE_PATTERN_COUNT];
        private readonly System.Random _random = new System.Random();

        private AttackState _state              = AttackState.Idle;
        private float       _stateTimer         = 0f;
        private float       _cooldownRemaining  = 0f;
        private int         _previousPatternId = 0;

        private Transform                  _activeTarget;
        private IDamageable                _activeTargetStatus;
        private BossPatternSO.PatternEntry _activePattern;
        private PatternKind                _activeKind;
        private BossPatternExecutionSpec   _activeSpec;
        private float                       _activeDamageMultiplier = 1f;
        private Vector3                    _lockedForward;  // 예고 시작 시점에 고정한 방향(방향 완전 잠금)
        private Vector3                    _dashDestination;
        private Vector3                    _markedCenter; // MarkedCircle 전용, 예고 시작 시점에 고정
        private uint                       _activeAttackInstanceId;
        private byte                       _activeRepeatIndex;
        private int                        _activeRepeatCount;
        private double                     _activeAttackStartServerTime;
        private uint                       _activeTargetNetId;
        private bool                       _activeParried;
        private BossAttackExecution        _activeExecution; // 늦게 도착한 이전 공격의 OnParried를 걸러내는 참조
        private IParryWindowTarget         _openParryTarget;
        private ParryWindowKey             _openParryKey;

        private readonly Transform[] _repeatTargets =
            new Transform[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private readonly uint[] _repeatTargetNetIds =
            new uint[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private readonly Vector3[] _repeatCenters =
            new Vector3[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private readonly Vector3[] _repeatDirections =
            new Vector3[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private readonly float[] _activeImpactOffsets =
            new float[BossPatternExecutionSpec.MAX_REPEAT_COUNT];
        private int _repeatCountOverride;

        // 여우불 탄막(RandomBarrage) 전용
        private readonly Transform[] _barrageAliveTargets    = new Transform[Constants.Network.MAX_PLAYERS];
        private readonly Vector3[]   _barrageDirections      = new Vector3[BARRAGE_SHOTS_PER_VOLLEY];
        private readonly int[]       _barrageNextSampleIndex = new int[BARRAGE_SHOTS_PER_VOLLEY];
        private int                  _barrageCycleIndex      = -1;

        // 여우불 진(GroundZone) 전용 - 일반 공격 상태 순환과 독립으로 흐른다
        private readonly GroundZoneInstance[] _groundZones =
            new GroundZoneInstance[GROUND_ZONE_MAX_CONCURRENT];
        private readonly bool[] _groundZoneLandmarkOccupied = new bool[GROUND_ZONE_LANDMARKS.Length];
        private float           _groundZoneSpawnCooldown    = GROUND_ZONE_INITIAL_SPAWN_DELAY;

        /****************************************
        *              Constructor
        ****************************************/

        public Phase2State(Transform self, BossStatus status, BossController controller) : base(self, status)
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

            // 일반 공격 상태 순환과 독립 - 보스가 다른 패턴을 진행 중이어도 계속 흐른다
            TickGroundZones(deltaTime);

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
            _cooldownRemaining      = _controller.ServerGetGlobalCadence(2);
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
            _repeatCountOverride = repeatCountOverride;
            BeginAttack(pattern, KindOf(patternId));
            _repeatCountOverride = 0;
            return true;
        }

        private void TickIdle()
        {
            if (Target == null)
            {
                return;
            }

            // 마수 돌진(7) 직후엔 쿨다운, 거리 추첨을 전부 건너뛰고 거대 꼬리치기(6)를 강제로
            // 붙인다 - 1페이즈 순간 접근(3) -> 여우불 봉 3연격(1) 강제 연계와 동일하게 맞춘다.
            // 마수 돌진 자체엔 전용 2타, 3타 애니메이션이 없어 반복 판정을 못 주고, 대신
            // 반복 애니메이션이 갖춰진 패턴6으로 이어 붙인다
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
            _candidatePatterns[candidateCount] = new BossPatternSelector.Candidate(
                pattern,
                (patternSlot < 0) ? 0f : _patternCooldowns[patternSlot],
                IsDistanceEligible(spec, distance),
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

        // 화면 로그용 이름. 기획 문서(BOSS_PATTERN_CONTRACT) 표기를 그대로 쓴다
        private static string PatternName(int patternId)
        {
            switch (patternId)
            {
                case MELEE_ARC_PATTERN_ID:     return "거대 꼬리치기";
                case TARGETED_DASH_PATTERN_ID: return "마수 돌진";
                case MARKED_CIRCLE_PATTERN_ID: return "표식 순환 처형";
                case CONE_BURST_PATTERN_ID:    return "연쇄 부채꼴 3단";
                case ROTATING_BEAM_PATTERN_ID: return "여우불 광선";
                default:                       return $"패턴 {patternId}";
            }
        }

        private void BeginAttack(BossPatternSO.PatternEntry pattern, PatternKind kind)
        {
            _activeTarget           = Target;
            _activeTarget.TryGetComponent(out _activeTargetStatus);
            _activePattern          = pattern;
            _activeSpec             = new BossPatternExecutionSpec(pattern);
            _activeKind             = ToKind(_activeSpec.Template, kind);
            if (pattern.PatternId == ROTATING_BEAM_PATTERN_ID)
            {
                // SO Template은 검증 통과용 ConeBurst라 ToKind가 못 잡는다, ID로 덮어쓴다
                _activeKind = PatternKind.RotatingBeam;
            }
            else if (pattern.PatternId == RANDOM_BARRAGE_PATTERN_ID)
            {
                _activeKind = PatternKind.RandomBarrage;
            }

            _barrageCycleIndex      = -1;
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
            _activeRepeatCount      = (_repeatCountOverride > 0)
                ? Mathf.Clamp(_repeatCountOverride, 1, BossPatternExecutionSpec.MAX_REPEAT_COUNT)
                : _activeSpec.RepeatCount;

            Vector3 direction = _activeTarget.position - Self.position;
            direction.y = 0f;
            direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : Self.forward;

            _lockedForward = direction; // 방향 완전 잠금 - 예고 중 대상이 움직여도 안 따라간다
            Self.rotation  = Quaternion.LookRotation(_lockedForward); // 공격마다 대상 방향으로 즉시 회전

            if (_activeKind == PatternKind.TargetedDash)
            {
                _dashDestination = Self.position + (direction * _activeSpec.MoveDistance);
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                // 위치 확정 후 잠금 - 예고 중 대상이 움직여도 낙인 중심은 안 따라간다
                _markedCenter = _activeTarget.position;
            }

            SnapshotRepeatTargets();

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

        private void SnapshotRepeatTargets()
        {
            for (int i = 0; i < _repeatTargets.Length; ++i)
            {
                _repeatTargets[i] = null;
                _repeatTargetNetIds[i] = 0u;
                _repeatCenters[i] = Vector3.zero;
                _repeatDirections[i] = _lockedForward;
            }

            int collectedTargetCount = 0;
            if (_activeSpec.Targeting == BossPatternSO.PatternTargeting.AllPlayers)
            {
                collectedTargetCount = _controller.ServerCollectAliveTargets(_repeatTargets);
                if ((_activeKind == PatternKind.MarkedCircle) && (_repeatCountOverride <= 0))
                {
                    _activeRepeatCount = Mathf.Clamp(collectedTargetCount, 1,
                        _activeRepeatCount);
                }
            }

            for (int i = 0; i < _activeRepeatCount; ++i)
            {
                Transform target = (collectedTargetCount > 0)
                    ? _repeatTargets[i % collectedTargetCount]
                    : _repeatTargets[i];
                if (target == null)
                {
                    target = _activeTarget;
                }

                _repeatTargets[i] = target;
                _repeatCenters[i] = (_activeKind == PatternKind.MarkedCircle)
                    ? target.position
                    : Self.position;
                Vector3 direction = target.position - Self.position;
                direction.y = 0f;
                _repeatDirections[i] = (direction.sqrMagnitude > 0.0001f)
                    ? direction.normalized
                    : _lockedForward;
                _repeatTargetNetIds[i] = target.TryGetComponent(out NetworkIdentity identity)
                    ? identity.netId : 0u;
            }

            _activeTargetNetId = _repeatTargetNetIds[0];
        }

        private void PublishRepeatTelegraphs()
        {
            for (int i = 0; i < _activeRepeatCount; ++i)
            {
                float impactOffset = ResolveImpactOffset(i);
                float startOffset = (i == 0) ? 0f : ResolveImpactOffset(i - 1);
                _controller.ServerBeginTelegraph(new BossTelegraphCue
                {
                    AttackInstanceId = _activeAttackInstanceId,
                    RepeatIndex      = (byte) i,
                    PatternId        = _activePattern.PatternId,
                    TargetNetId      = _repeatTargetNetIds[i],
                    Origin           = _repeatCenters[i],
                    Direction        = _repeatDirections[i],
                    StartServerTime  = _activeAttackStartServerTime + startOffset,
                    ImpactServerTime = _activeAttackStartServerTime + impactOffset,
                });
            }
        }

        private float ResolveImpactOffset(int repeatIndex)
        {
            if (repeatIndex < _activeSpec.RepeatCount)
            {
                return _activeImpactOffsets[repeatIndex];
            }

            float previous = ResolveImpactOffset(repeatIndex - 1);
            return previous + Mathf.Max(0.25f, _activePattern.TelegraphDuration * 0.35f);
        }

        private void TickWindup(float deltaTime)
        {
            bool targetUnavailable = (_activeTarget == null) ||
                ((_activeTargetStatus != null) &&
                    (_activeTargetStatus.IsDead ||
                        ((_activeTargetStatus is IBossSwallowTarget swallowed) &&
                            swallowed.IsSwallowed)));
            if (targetUnavailable &&
                (!_controller.ServerMustCompleteAcceptedPattern(_activePattern.PatternId)))
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
                Self.position = Vector3.MoveTowards(Self.position, _dashDestination,
                    _activeSpec.MoveSpeed * deltaTime);
            }

            _stateTimer += deltaTime;
            if (_stateTimer < ResolveImpactOffset(_activeRepeatIndex))
            {
                return;
            }

            CloseParryWindowIfOpen();

            if (!_activeParried)
            {
                ResolveImpact();
            }

            _controller.ServerCancelTelegraph(_activeAttackInstanceId,
                _activeRepeatIndex, _repeatTargetNetIds[_activeRepeatIndex]);
            _activeExecution = null;

            ++_activeRepeatIndex;
            if (_activeRepeatIndex < _activeRepeatCount)
            {
                _activeParried = false;
                TryOpenParryWindow(_activePattern, _activeRepeatIndex);
                GDebug.Server($"[보스] {PatternName(_activePattern.PatternId)} 반복 {_activeRepeatIndex + 1}/{_activeRepeatCount}");
                return;
            }

            // 회복으로 넘어가는 이 지점이 패턴 종료다. 반복 패턴은 마지막 판정에서 한 번만 온다
            _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);

            _stateTimer        = 0f;
            _cooldownRemaining = _controller.ServerGetGlobalCadence(2);
            _state             = AttackState.Recovery;
        }

        // 여우불 광선 전용 - ImpactOffsets 기반 반복 판정을 안 쓰고, 예고가 끝나면
        // MoveSpeed를 "회전에 걸리는 시간(초)"으로 재해석해 그 시간 동안 360도를 돈다
        // (Template이 ConeBurst라 실제 이동 속도로는 안 쓰인다). ActiveDuration은 다른
        // 패턴과 똑같이 회전이 끝난 뒤 회복 상태로 보여주는 짧은 버퍼로 남겨둔다.
        // Damage 필드는 초당 피해량(DPS)으로 읽어 deltaTime을 곱한 만큼만 적용한다.
        // 2페이즈는 ROTATING_BEAM_COUNT개를 같은 축에서 등간격으로 같이 돌린다(회전 속도, 폭은 그대로,
        // 회피 가능한 연속 구간만 줄인다) - 1페이즈(1개)보다 회피가 빡빡해지는 지점
        private void TickRotatingBeam(float deltaTime)
        {
            _stateTimer += deltaTime;

            float sweepElapsed  = _stateTimer - _activePattern.TelegraphDuration;
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
                _cooldownRemaining = _controller.ServerGetGlobalCadence(2);
                _state             = AttackState.Recovery;
                return;
            }

            float   sweepAngle     = (sweepElapsed / sweepDuration) * ROTATING_BEAM_FULL_SWEEP_DEGREES;
            float   beamSpacing    = ROTATING_BEAM_FULL_SWEEP_DEGREES / ROTATING_BEAM_COUNT;
            Vector3 beamOrigin     = Self.position;
            uint    attackerNetId  = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float   damageThisTick = _activePattern.Damage * _activeDamageMultiplier * deltaTime;

            for (int i = 0; i < ROTATING_BEAM_COUNT; ++i)
            {
                Vector3 beamDirection = Quaternion.AngleAxis(sweepAngle + (i * beamSpacing), Vector3.up)
                    * _lockedForward;
                Vector3 beamEnd = beamOrigin + (beamDirection * _activeSpec.Range);

                // 실제 VFX가 붙기 전까지만 쓰는 확인용 시각화 - 에디터/개발 빌드에서만 그려지고
                // 릴리스 빌드에서는 호출 자체가 통째로 사라진다(GGizmo.Line의 Conditional 속성)
                DrawBeamGizmo(beamOrigin, beamEnd, beamDirection);

                BossPatternRunner.ExecuteMeleeArc(
                    beamOrigin,
                    beamDirection,
                    _activeSpec.Range,
                    _activeSpec.AngleDegrees,
                    damageThisTick,
                    attackerNetId,
                    _playerLayerMask,
                    _controller,
                    HitStrength.Light);
            }
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
                _cooldownRemaining = _controller.ServerGetGlobalCadence(2);
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
                        HitStrength.Light);
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

        // 여우불 진 전용 - 일반 선택기, BeginAttack/TickWindup 순환을 안 거친다. 이 패턴의
        // PatternEntry는 스키마 계약(15개)을 채우기 위한 데이터 컨테이너로만 쓰이고, 필드는
        // 위 상수 주석대로 재해석한다. Target이 없으면(전투 시작 전 등) 아무것도 하지 않는다
        private void TickGroundZones(float deltaTime)
        {
            if (Target == null)
            {
                return;
            }

            if (!GameDB.Boss.TryGetPattern(GROUND_ZONE_PATTERN_ID, out BossPatternSO.PatternEntry zonePattern))
            {
                return;
            }

            float warningDuration = zonePattern.TelegraphDuration;
            float activeDuration  = zonePattern.ActiveDuration;
            float radius          = zonePattern.Range;
            float dps             = zonePattern.Damage;
            uint  attackerNetId   = (_selfIdentity != null) ? _selfIdentity.netId : 0u;

            for (int i = 0; i < _groundZones.Length; ++i)
            {
                if (!_groundZones[i].IsActive)
                {
                    continue;
                }

                _groundZones[i].Timer += deltaTime;
                float   timer    = _groundZones[i].Timer;
                Vector3 position = GROUND_ZONE_LANDMARKS[_groundZones[i].LandmarkIndex];

                if (timer < warningDuration)
                {
                    // 확인용 시각화 - 실제 VFX(SB_Energy_Release_Loop 등)가 붙기 전까지만 쓴다
                    DrawGroundZoneGizmo(position, radius, Color.yellow);
                    continue;
                }

                if (timer >= (warningDuration + activeDuration))
                {
                    _groundZones[i] = default;
                    continue;
                }

                DrawGroundZoneGizmo(position, radius, new Color(1f, 0.4f, 0f));

                // 틱마다 deltaTime만큼만 적용하는 지속 피해 - 주기적 타격이 아니라 서 있는 동안
                // 계속 깎이는 느낌을 준다. 카메라 흔들림 스팸을 피하려고 Light를 쓴다
                BossPatternRunner.ExecuteMeleeArc(
                    position,
                    Vector3.forward,
                    radius,
                    360f,
                    dps * deltaTime,
                    attackerNetId,
                    _playerLayerMask,
                    _controller,
                    HitStrength.Light);
            }

            _groundZoneSpawnCooldown -= deltaTime;
            if (_groundZoneSpawnCooldown > 0f)
            {
                return;
            }

            int freeSlot = FindFreeGroundZoneSlot();
            if ((freeSlot < 0) || (!TryPickFreeGroundZoneLandmark(out int landmarkIndex)))
            {
                return;
            }

            _groundZones[freeSlot] = new GroundZoneInstance
            {
                IsActive      = true,
                LandmarkIndex = landmarkIndex,
                Timer         = 0f,
            };
            _groundZoneSpawnCooldown = zonePattern.Cooldown;
        }

        private int FindFreeGroundZoneSlot()
        {
            for (int i = 0; i < _groundZones.Length; ++i)
            {
                if (!_groundZones[i].IsActive)
                {
                    return i;
                }
            }

            return -1;
        }

        // 이미 장판이 깔린 랜드마크는 제외하고 나머지 중 무작위로 고른다 - 4기 중 최대
        // GROUND_ZONE_MAX_CONCURRENT(2)기까지만 쓰이므로 항상 최소 2곳은 비어 있다
        private bool TryPickFreeGroundZoneLandmark(out int landmarkIndex)
        {
            for (int i = 0; i < _groundZoneLandmarkOccupied.Length; ++i)
            {
                _groundZoneLandmarkOccupied[i] = false;
            }

            for (int i = 0; i < _groundZones.Length; ++i)
            {
                if (_groundZones[i].IsActive)
                {
                    _groundZoneLandmarkOccupied[_groundZones[i].LandmarkIndex] = true;
                }
            }

            int freeCount = 0;
            for (int i = 0; i < _groundZoneLandmarkOccupied.Length; ++i)
            {
                if (!_groundZoneLandmarkOccupied[i])
                {
                    ++freeCount;
                }
            }

            if (freeCount <= 0)
            {
                landmarkIndex = -1;
                return false;
            }

            int pickedFreeIndex = _random.Next(freeCount);
            int seenFreeCount   = 0;
            for (int i = 0; i < _groundZoneLandmarkOccupied.Length; ++i)
            {
                if (_groundZoneLandmarkOccupied[i])
                {
                    continue;
                }

                if (seenFreeCount == pickedFreeIndex)
                {
                    landmarkIndex = i;
                    return true;
                }

                ++seenFreeCount;
            }

            landmarkIndex = -1;
            return false;
        }

        // GGizmo에는 원 전용 그리기가 없어 선분을 이어 외곽선으로 표현한다
        private static void DrawGroundZoneGizmo(Vector3 center, float radius, Color color)
        {
            Vector3 previous = center + (Vector3.forward * radius);
            for (int i = 1; i <= GROUND_ZONE_GIZMO_SEGMENTS; ++i)
            {
                float angle = (360f / GROUND_ZONE_GIZMO_SEGMENTS) * i;
                Vector3 next = center + (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius);
                GGizmo.Line(GGizmoChannel.Monster, previous, next, color);
                previous = next;
            }
        }

        private void ResolveImpact()
        {
            uint attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float damage       = _activePattern.Damage * _activeDamageMultiplier;

            if (_activeKind == PatternKind.TargetedDash)
            {
                // 도착 지점 기준 전방위 판정이라 각도 제한이 필요 없어 360도로 둔다
                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    Self.forward,
                    _activeSpec.ImpactRadius,
                    360f,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                // 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
                BossPatternRunner.ExecuteMeleeArc(
                    _repeatCenters[_activeRepeatIndex],
                    Vector3.forward,
                    _activeSpec.ImpactRadius,
                    360f,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller);
                return;
            }

            if (_activeKind == PatternKind.ConeBurst)
            {
                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    _repeatDirections[_activeRepeatIndex],
                    _activeSpec.Range,
                    _activeSpec.AngleDegrees,
                    damage,
                    attackerNetId,
                    _playerLayerMask,
                    _controller);
                return;
            }

            // MeleeArc. 방향은 예고 시작 시점에 잠근 값을 쓴다(방향 완전 잠금)
            BossPatternRunner.ExecuteMeleeArc(
                Self.position,
                _repeatDirections[_activeRepeatIndex],
                _activeSpec.Range,
                _activeSpec.AngleDegrees,
                damage,
                attackerNetId,
                _playerLayerMask,
                _controller);
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

        // 현재 실행만 패링 결과를 받는다. 이전 공격의 늦은 통지는 버린다.
        private void HandleAttackParried(BossAttackExecution execution, uint parriedByNetId)
        {
            if ((IsExited) || (execution == null) || (!ReferenceEquals(execution, _activeExecution))
                || (_state != AttackState.Windup))
            {
                return;
            }

            uint attackInstanceId = _activeAttackInstanceId;
            CancelActiveAttack();
            _controller.ServerRecordParryCounter(parriedByNetId, attackInstanceId);
        }

        private void TryOpenParryWindow(BossPatternSO.PatternEntry pattern, byte repeatIndex)
        {
            if ((!_activeSpec.IsRepeatParryable(pattern, repeatIndex)) &&
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
            double impactTime = _activeAttackStartServerTime + ResolveImpactOffset(repeatIndex);

            BossAttackExecution execution = new BossAttackExecution(
                MELEE_PARRY_WINDOW_DURATION, _activeAttackInstanceId, repeatIndex,
                HandleAttackParried);

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
