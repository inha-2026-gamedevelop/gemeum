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
    /// 2페이즈, 시험하는 판. Phase1State의 네 템플릿을 SO 형상으로 재사용한다
    /// 패턴 6은 3연타 마지막 타 패링, 8은 살아 있는 플레이어 순차 처형, 9는 3단 펄스다
    /// 꼬리 봉인 뒤에는 7에 차사 패링 창, 9 종료에 무녀 공명을 이관하고 9의 신궁 중단과 공존한다
    /// 조건부 10은 남은 원혼 수를 반복 수로 snapshot해 이미 수락된 백귀야행을 끝까지 실행한다
    /// 일반 선택은 거리, 쿨다운, 직전 패턴, 조건부, 꼬리 봉인을 필터링한다
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
            FoxfireTrail,  // ID로만 구분한다 - SO Template은 검증 통과용으로 TargetedDash(2)를 그대로 쓴다
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
        private const int FOXFIRE_TRAIL_PATTERN_ID  = 17; // 여우불 잔로 - 구조 검증용 신규 패턴
        // 위 일곱 다 GameDB.Boss._patterns의 PatternId와 맞춘다. 10번(백귀야행)은
        // 조건부 발동(꼬리 상태)이라 쿨다운 기반 선택기 대상이 아니다 - 이 슬라이스에서 다루지 않는다

        private const int MAX_CANDIDATE_COUNT      = 7; // 현재 독립 후보 수, 패턴 10은 조건부 발동
        private const int SELECTABLE_PATTERN_COUNT = 7;

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
            FOXFIRE_TRAIL_PATTERN_ID,
        };

        private readonly BossPatternSelector.Candidate[] _candidatePatterns =
            new BossPatternSelector.Candidate[MAX_CANDIDATE_COUNT];
        private readonly float[]      _patternCooldowns = new float[SELECTABLE_PATTERN_COUNT];
        private readonly System.Random _random = new System.Random();

        // Stage 3: TickIdle의 실제 결정권자. Phase1과 달리 Opening 서브트리가 없다
        private readonly ICombatBTNode _combatBT;

        // Stage 2 EvaluateShadowCategory의 결과를 리프 노드들이 비교 로그에 쓰기 위한 진단
        // 전용 필드 - 게임플레이 판단에는 전혀 쓰이지 않고, TickIdle이 매 틱 갱신만 한다
        private string _shadowCategoryForVerification;

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

        // Stage: TargetedDash/MarkedCircle 실행 추출 - 패턴 전용 상태와 BeginAttack 초기화/
        // Impact 판정을 이 실행 객체들이 소유한다. Phase2State는 여전히 FSM 상태(_state),
        // 타이머, attackInstanceId를 소유하고, 이 객체들은 호출만 받는다(직접 _state를
        // 바꾸지 않음). Phase1의 동종 패턴들과 동작이 달라(추적 구간, 직전 지목자 제외 없음)
        // 별도 클래스로 둔다 - 공통화는 아직 안 함.
        // MarkedCircle의 실제 판정 원점은 ConeBurst/MeleeArc와 공유하는 반복-대상 스냅샷
        // (_repeatCenters, SnapshotRepeatTargets)에서 나온다 - 그 공용 인프라는 이번
        // 파일럿에서 옮기지 않고, Phase2MarkedCircleExecution에는 원본에서도 죽은 값이었던
        // Center 계산만 옮겼다(자세한 근거는 그 클래스의 주석 참고)
        private readonly Phase2TargetedDashExecution   _targetedDash  = new Phase2TargetedDashExecution();
        private readonly Phase2MarkedCircleExecution   _markedCircle  = new Phase2MarkedCircleExecution();

        // RotatingBeam은 자체 완료 시점을 스스로 판단하는 패턴이다 - TickWindup이
        // PatternTickResult를 반환하고, FSM 전환(_state/쿨다운/종료 통지)은 Complete를
        // 받은 Phase2State만 수행한다(실행 객체가 직접 하지 않음)
        private readonly Phase2RotatingBeamExecution   _rotatingBeam  = new Phase2RotatingBeamExecution();
        private readonly Phase2RandomBarrageExecution  _randomBarrage = new Phase2RandomBarrageExecution();

        // 여우불 잔로(패턴 17) - Pattern Execution 분리 구조 검증용 신규 패턴(2026-09-27)
        private readonly Phase2FoxfireTrailExecution   _foxfireTrail  = new Phase2FoxfireTrailExecution();

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

            // Stage 3: 2페이즈는 Opening 서브트리가 없다 - 확정 구조 그대로
            _combatBT = new SequenceNode(
                new DelegateNode(EvaluateHasTarget),
                new SelectorNode(
                    new DelegateNode(TryRunForcedFollowUp),
                    new DelegateNode(TryRunGeneralAttack)));
        }

        /****************************************
        *                Methods
        ****************************************/

        // Stage 2: Combat BT Shadow Evaluation - 읽기 전용 관찰만 한다, 아직 결정권이 없다
        // BuildCandidates/TrySelect/BeginAttack을 호출하지 않고, RNG를 쓰지 않고, 어떤
        // 필드도 쓰지 않는다. Phase2에는 Opening Sequence가 없어 그 카테고리는 나오지 않는다
        private string EvaluateShadowCategory()
        {
            if (Target == null)
            {
                return "Target 없음";
            }

            // ForcedFollowUp은 Actual도 TryGetPattern이 실패하면 이 분기를 포기하고
            // 아래로 흘러가므로(코드 그대로), Shadow도 같은 조건을 봐야 거짓 불일치가 안 생긴다
            // TryGetPattern은 데이터 조회일 뿐 상태를 바꾸지 않는다
            if ((_previousPatternId == TARGETED_DASH_PATTERN_ID) &&
                GameDB.Boss.TryGetPattern(MELEE_ARC_PATTERN_ID, out _))
            {
                return "ForcedFollowUp";
            }

            if (_cooldownRemaining > 0f)
            {
                return "Cooldown/Wait";
            }

            return "GeneralAttack 가능";
        }

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

        // Stage 3: TickIdle의 얇은 진입점. 실제 결정은 _combatBT가 한다 - Shadow는 이전과
        // 동일하게 결정권 없는 검증용 관찰이고, BT 호출과 같은 틱 안에서 먼저(아무 상태도
        // 바뀌기 전에) 평가해 기존 Stage 2와 같은 시점 보장을 유지한다
        private void TickIdle()
        {
            _shadowCategoryForVerification = EvaluateShadowCategory();
            _combatBT.Evaluate();
        }

        // ===== Combat BT 리프 (Stage 3) =====
        // 아래 메서드들이 기존 TickIdle의 우선순위 사슬을 그대로 옮긴 것이다.
        // BuildCandidates/BossPatternSelector.TrySelect/BeginAttack 내부 로직은 손대지 않았고
        // 호출 순서·횟수도 그대로다. Phase2에는 Opening 리프가 없다

        private BTResult EvaluateHasTarget()
        {
            if (Target == null)
            {
                GDebug.Server("[Phase2] TickIdle 분류: Target 없음");
                GDebug.Server($"[Phase2][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=Target 없음 / " +
                    ((_shadowCategoryForVerification == "Target 없음") ? "일치" : "불일치"));
                return BTResult.Failure;
            }

            return BTResult.Success;
        }

        // 마수 돌진(7) 직후엔 쿨다운, 거리 추첨을 전부 건너뛰고 거대 꼬리치기(6)를 강제로
        // 붙인다 - 1페이즈 순간 접근(3) -> 여우불 봉 3연격(1) 강제 연계와 동일하게 맞춘다.
        // 마수 돌진 자체엔 전용 2타, 3타 애니메이션이 없어 반복 판정을 못 주고, 대신
        // 반복 애니메이션이 갖춰진 패턴6으로 이어 붙인다
        private BTResult TryRunForcedFollowUp()
        {
            if (_previousPatternId != TARGETED_DASH_PATTERN_ID)
            {
                return BTResult.Failure;
            }

            if (!GameDB.Boss.TryGetPattern(MELEE_ARC_PATTERN_ID, out BossPatternSO.PatternEntry followUpPattern))
            {
                return BTResult.Failure;
            }

            GDebug.Server("[Phase2] TickIdle 분류: ForcedFollowUp (TargetedDash 이후)");
            GDebug.Server($"[Phase2][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=ForcedFollowUp / " +
                ((_shadowCategoryForVerification == "ForcedFollowUp") ? "일치" : "불일치"));
            BeginAttack(followUpPattern, KindOf(MELEE_ARC_PATTERN_ID));
            return BTResult.Success;
        }

        private BTResult TryRunGeneralAttack()
        {
            if (_cooldownRemaining > 0f)
            {
                GDebug.Server($"[Phase2] TickIdle 분류: Cooldown/Wait (remaining={_cooldownRemaining:F2})");
                GDebug.Server($"[Phase2][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=Cooldown/Wait / " +
                    ((_shadowCategoryForVerification == "Cooldown/Wait") ? "일치" : "불일치"));
                return BTResult.Failure;
            }

            GDebug.Server("[Phase2] TickIdle 분류: GeneralAttack 진입");
            GDebug.Server($"[Phase2][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=GeneralAttack 가능 / " +
                ((_shadowCategoryForVerification == "GeneralAttack 가능") ? "일치" : "불일치"));
            float distance = Vector3.Distance(Self.position, Target.position);
            int candidateCount = BuildCandidates(distance);
            if (!BossPatternSelector.TrySelect(
                _candidatePatterns,
                candidateCount,
                _previousPatternId,
                _random,
                out BossPatternSelector.Candidate selected))
            {
                GDebug.Server("[Phase2] TickIdle 분류: GeneralAttack TrySelect 실패");
                return BTResult.Blocked;
            }

            GDebug.Server($"[Phase2] TickIdle 분류: GeneralAttack 선택 성공 (patternId={selected.Pattern.PatternId})");
            BeginAttack(selected.Pattern, KindOf(selected.Pattern.PatternId));
            return BTResult.Success;
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
            candidateCount = AddCandidate(candidateCount, FOXFIRE_TRAIL_PATTERN_ID, distance);
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

        private bool IsDistanceEligible(BossPatternExecutionSpec spec, float distance)
        {
            switch (spec.Template)
            {
                case BossPatternSO.PatternTemplate.MeleeArc:
                    return distance <= spec.Range;
                case BossPatternSO.PatternTemplate.TargetedDash:
                    return _targetedDash.IsDistanceEligible(spec, distance);
                case BossPatternSO.PatternTemplate.MarkedCircle:
                    return _markedCircle.IsDistanceEligible(spec, distance);
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

            if (patternId == FOXFIRE_TRAIL_PATTERN_ID)
            {
                return PatternKind.FoxfireTrail;
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
                case FOXFIRE_TRAIL_PATTERN_ID: return "여우불 잔로";
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
            else if (pattern.PatternId == FOXFIRE_TRAIL_PATTERN_ID)
            {
                // SO Template은 검증 통과용 TargetedDash라 ToKind가 못 잡는다, ID로 덮어쓴다
                _activeKind = PatternKind.FoxfireTrail;
            }

            _randomBarrage.OnBeginAttack();
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
                _targetedDash.OnBeginAttack(Self.position, _lockedForward, _activeSpec);
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                _markedCircle.OnBeginAttack(_activeTarget);
            }
            else if (_activeKind == PatternKind.FoxfireTrail)
            {
                _foxfireTrail.OnBeginAttack(Self.position, _lockedForward, _activeSpec);
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
                _stateTimer += deltaTime;
                uint  attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
                float damage        = _activePattern.Damage * _activeDamageMultiplier;
                PatternTickResult result = _rotatingBeam.TickWindup(deltaTime, _stateTimer,
                    _activePattern.TelegraphDuration, Self, _lockedForward, _activeSpec, damage,
                    attackerNetId, _playerLayerMask, _controller);
                if (result == PatternTickResult.Complete)
                {
                    // 회전 시작 전 딱 한 번만 보낸 예고 신호를 여기서 닫는다 - 안 닫으면
                    // 동시 예고 목록에 계속 남아 다음 발동들이 상한에 걸린다
                    CompleteSelfManagedAttack();
                }
                return;
            }

            if (_activeKind == PatternKind.RandomBarrage)
            {
                _stateTimer += deltaTime;
                uint  attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
                float damage        = _activePattern.Damage * _activeDamageMultiplier;
                PatternTickResult result = _randomBarrage.TickWindup(_stateTimer,
                    _activePattern.TelegraphDuration, Self, _lockedForward, _activeSpec, damage,
                    attackerNetId, _playerLayerMask, _controller, _random);
                if (result == PatternTickResult.Complete)
                {
                    // 난사 시작 전 딱 한 번만 보낸 예고 신호를 여기서 닫는다 - 안 닫으면
                    // 동시 예고 목록에 계속 남아 다음 발동들이 상한에 걸린다
                    CompleteSelfManagedAttack();
                }
                return;
            }

            if (_activeKind == PatternKind.FoxfireTrail)
            {
                _stateTimer += deltaTime;
                uint  attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
                float damage        = _activePattern.Damage * _activeDamageMultiplier;
                PatternTickResult result = _foxfireTrail.TickWindup(deltaTime, _stateTimer,
                    _activePattern.TelegraphDuration, Self, _activeSpec, damage, attackerNetId,
                    _playerLayerMask, _controller, _activeAttackInstanceId, _activePattern.PatternId);
                if (result == PatternTickResult.Complete)
                {
                    // 공용 예고(진입 신호) 하나만 여기서 닫는다 - 잔상별 Telegraph는 폭발 시점마다
                    // ExplodeTrailPoint가 이미 개별적으로 닫아서 이 시점엔 하나도 안 남아 있다
                    CompleteSelfManagedAttack();
                }
                return;
            }

            if (_activeKind == PatternKind.TargetedDash)
            {
                _targetedDash.TickWindup(deltaTime, Self, _activeSpec);
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

        // RotatingBeam/RandomBarrage처럼 자체 완료 시점을 스스로 판단하는(PatternTickResult
        // 반환) 패턴 전용 FSM 전환. 위 TickWindup 하단의 표준 반복-오프셋 완료 경로와는
        // 별개다 - 그쪽은 ServerCancelTelegraph가 다른 오버로드(repeatIndex 포함)를 쓰고
        // 반복 인덱스 진행/재판정 분기가 얽혀 있어 여기로 합치지 않는다
        private void CompleteSelfManagedAttack()
        {
            _controller.ServerCancelTelegraph(_activeAttackInstanceId);
            _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);
            _stateTimer        = 0f;
            _cooldownRemaining = _controller.ServerGetGlobalCadence(2);
            _state             = AttackState.Recovery;
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
                    hitStrength: HitStrength.Light);
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
                _targetedDash.ResolveImpact(Self, _activeSpec, damage, attackerNetId,
                    _playerLayerMask, _controller);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                // 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
                // 실제 원점은 반복-대상 스냅샷(_repeatCenters)에서 나온다 - ConeBurst/MeleeArc와
                // 공유하는 인프라라 실행 객체에 넘기지 않고 여기서 읽어 값으로 전달한다
                _markedCircle.ResolveImpact(_repeatCenters[_activeRepeatIndex], _activeSpec,
                    damage, attackerNetId, _playerLayerMask, _controller);
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

        // 현재 실행만 패링 결과를 받는다. 이전 공격의 늦은 통지는 버린다
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

            // 기본 창 길이는 비대상 섬광 근사와 같은 설정값을 쓴다. 로드아웃 보너스는 ParryController가 더한다
            BossAttackExecution execution = new BossAttackExecution(
                GameDB.Combat.ParryWindowDuration, _activeAttackInstanceId, repeatIndex,
                HandleAttackParried);
            uint sequenceId = _activeAttackInstanceId;
            execution.SequenceActive = () => IsAttackWindup(sequenceId);

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
