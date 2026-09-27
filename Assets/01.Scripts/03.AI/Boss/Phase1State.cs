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
    /// 패턴 16(여우불 낙인 전이, RelayBrand - MarkedCircle 판정을 재사용하되 3회 연속으로
    /// 대상을 옮겨가며 폭발시킨다. Pattern Execution 분리 구조가 신규 패턴 추가에 실제로
    /// 맞는지 검증하기 위한 파일럿, 2026-09-27).
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
            RelayBrand,    // ID로만 구분한다 - SO Template은 검증 통과용으로 MarkedCircle(3)을 그대로 쓴다
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
        private const int RELAY_BRAND_PATTERN_ID    = 16; // 여우불 낙인 전이(RelayBrand) - 구조 검증용 신규 패턴
        // 위 여덟 다 GameDB.Boss._patterns의 PatternId와 맞춘다

        private const int MAX_CANDIDATE_COUNT     = 8; // 현재 독립 후보 수
        private const int SELECTABLE_PATTERN_COUNT = 8;

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
            RELAY_BRAND_PATTERN_ID,
        };

        private readonly BossPatternSelector.Candidate[] _candidatePatterns =
            new BossPatternSelector.Candidate[MAX_CANDIDATE_COUNT];
        private readonly float[] _patternCooldowns = new float[SELECTABLE_PATTERN_COUNT];
        private readonly System.Random _random = new System.Random();

        // Stage 3: TickIdle의 실제 결정권자. Target/Opening/ForcedFollowUp/GeneralAttack
        // 우선순위를 그대로 옮긴 구조다(BOSS_COMBAT_BT_DESIGN 참고), 생성자에서 한 번만 조립한다
        private readonly ICombatBTNode _combatBT;

        // Stage 2 EvaluateShadowCategory의 결과를 리프 노드들이 비교 로그에 쓰기 위한 진단
        // 전용 필드 - 게임플레이 판단에는 전혀 쓰이지 않고, TickIdle이 매 틱 갱신만 한다
        private string _shadowCategoryForVerification;

        // 대상을 고르는 패턴(TargetedDash, MarkedCircle) 공용 - 직전 지목자를 뺀 후보를 다시
        // 뽑을 때 쓰는 재사용 버퍼, 할당 없음
        private readonly Collider[] _exclusionScanBuffer =
            new Collider[Constants.Network.MAX_PLAYERS + 1];

        // Stage: TargetedDash/MarkedCircle 실행 추출 - 패턴 전용 상태와 대상 재선정/
        // BeginAttack 초기화/Windup 추적/Impact 판정을 전부 이 실행 객체들이 소유한다.
        // Phase1State는 여전히 FSM 상태(_state), 타이머, attackInstanceId를 소유하고,
        // 이 객체들은 호출만 받는다(직접 _state를 바꾸지 않음)
        private readonly Phase1TargetedDashExecution   _targetedDash  = new Phase1TargetedDashExecution();
        private readonly Phase1MarkedCircleExecution   _markedCircle  = new Phase1MarkedCircleExecution();
        private readonly Phase1RotatingBeamExecution   _rotatingBeam  = new Phase1RotatingBeamExecution();
        private readonly Phase1RandomBarrageExecution  _randomBarrage = new Phase1RandomBarrageExecution();
        // RelayBrand(패턴 16) - Pattern Execution 분리 구조 검증용 신규 패턴(2026-09-27)
        private readonly Phase1RelayBrandExecution     _relayBrand    = new Phase1RelayBrandExecution();

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

        /****************************************
        *              Constructor
        ****************************************/

        public Phase1State(Transform self, BossStatus status, BossController controller) : base(self, status)
        {
            _controller      = controller;
            _selfIdentity    = self.GetComponent<NetworkIdentity>();
            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);

            // Stage 3: 1페이즈만 Opening 서브트리를 갖는다 - 확정 구조 그대로
            _combatBT = new SequenceNode(
                new DelegateNode(EvaluateHasTarget),
                new SelectorNode(
                    new DelegateNode(TryRunOpeningStep),
                    new DelegateNode(TryRunForcedFollowUp),
                    new DelegateNode(TryRunGeneralAttack)));
        }

        /****************************************
        *                Methods
        ****************************************/

        // Stage 2: Combat BT Shadow Evaluation - 읽기 전용 관찰만 한다, 아직 결정권이 없다
        // BuildCandidates/TrySelect/BeginAttack을 호출하지 않고, RNG를 쓰지 않고, 어떤
        // 필드도 쓰지 않는다. TickIdle의 우선순위 사슬을 그대로 흉내 내되 카테고리 5개까지만
        // 구분한다(구체적인 PatternId는 다루지 않는다)
        private string EvaluateShadowCategory()
        {
            if (Target == null)
            {
                return "Target 없음";
            }

            if (_openingIndex < OPENING_SEQUENCE.Length)
            {
                return "Opening";
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

        // Stage 3: TickIdle의 얇은 진입점. 실제 결정은 _combatBT가 한다 - Shadow는 이전과
        // 동일하게 결정권 없는 검증용 관찰이고, BT 호출과 같은 틱 안에서 먼저(아무 상태도
        // 바뀌기 전에) 평가해 기존 Stage 2와 같은 시점 보장을 유지한다
        private void TickIdle()
        {
            _shadowCategoryForVerification = EvaluateShadowCategory();
            _combatBT.Evaluate();
        }

        // ===== Combat BT 리프 (Stage 3) =====
        // 아래 네 메서드가 기존 TickIdle의 우선순위 사슬을 그대로 옮긴 것이다.
        // BuildCandidates/BossPatternSelector.TrySelect/BeginAttack 내부 로직은 손대지 않았고
        // 호출 순서·횟수도 그대로다

        private BTResult EvaluateHasTarget()
        {
            if (Target == null)
            {
                GDebug.Server("[Phase1] TickIdle 분류: Target 없음");
                GDebug.Server($"[Phase1][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=Target 없음 / " +
                    ((_shadowCategoryForVerification == "Target 없음") ? "일치" : "불일치"));
                return BTResult.Failure;
            }

            return BTResult.Success;
        }

        // 오프닝은 쿨다운을 안 보고, 추첨도 없이 정해진 순서 그대로 발동한다
        private BTResult TryRunOpeningStep()
        {
            if (_openingIndex >= OPENING_SEQUENCE.Length)
            {
                return BTResult.Failure;
            }

            GDebug.Server($"[Phase1] TickIdle 분류: Opening (index={_openingIndex})");
            GDebug.Server($"[Phase1][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=Opening / " +
                ((_shadowCategoryForVerification == "Opening") ? "일치" : "불일치"));

            int openingPatternId = OPENING_SEQUENCE[_openingIndex];
            if (!GameDB.Boss.TryGetPattern(openingPatternId, out BossPatternSO.PatternEntry openingPattern))
            {
                return BTResult.Blocked;
            }

            ++_openingIndex;
            BeginAttack(openingPattern, KindOf(openingPatternId));
            return BTResult.Success;
        }

        // 순간 접근(3) 직후엔 쿨다운, 거리 추첨을 전부 건너뛰고 여우불 봉 3연격(1)을 강제로
        // 붙인다 - 팀장 요청(2026-09-15). 순간접근 자체엔 전용 2타, 3타 애니메이션이 없어
        // 반복 판정을 못 주고, 대신 이미 반복 애니메이션이 갖춰진 패턴1로 이어 붙인다
        // 오프닝 이후에도 이 후속 공격만 고정 순서로 실행한다
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

            GDebug.Server("[Phase1] TickIdle 분류: ForcedFollowUp (TargetedDash 이후)");
            GDebug.Server($"[Phase1][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=ForcedFollowUp / " +
                ((_shadowCategoryForVerification == "ForcedFollowUp") ? "일치" : "불일치"));
            BeginAttack(followUpPattern, KindOf(MELEE_ARC_PATTERN_ID));
            return BTResult.Success;
        }

        private BTResult TryRunGeneralAttack()
        {
            if (_cooldownRemaining > 0f)
            {
                GDebug.Server($"[Phase1] TickIdle 분류: Cooldown/Wait (remaining={_cooldownRemaining:F2})");
                GDebug.Server($"[Phase1][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=Cooldown/Wait / " +
                    ((_shadowCategoryForVerification == "Cooldown/Wait") ? "일치" : "불일치"));
                return BTResult.Failure;
            }

            GDebug.Server("[Phase1] TickIdle 분류: GeneralAttack 진입");
            GDebug.Server($"[Phase1][ShadowBT] Shadow={_shadowCategoryForVerification} / Actual=GeneralAttack 가능 / " +
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
                GDebug.Server("[Phase1] TickIdle 분류: GeneralAttack TrySelect 실패");
                return BTResult.Blocked;
            }

            GDebug.Server($"[Phase1] TickIdle 분류: GeneralAttack 선택 성공 (patternId={selected.Pattern.PatternId})");
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
            candidateCount = AddCandidate(candidateCount, DERIVED_MELEE_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, TARGETED_DASH_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, MARKED_CIRCLE_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, CONE_BURST_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, ROTATING_BEAM_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, RANDOM_BARRAGE_PATTERN_ID, distance);
            candidateCount = AddCandidate(candidateCount, RELAY_BRAND_PATTERN_ID, distance);
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

            if (patternId == RELAY_BRAND_PATTERN_ID)
            {
                return PatternKind.RelayBrand;
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
            else if (pattern.PatternId == RELAY_BRAND_PATTERN_ID)
            {
                // SO Template은 검증 통과용 MarkedCircle이라 ToKind가 못 잡는다, ID로 덮어쓴다
                _activeKind = PatternKind.RelayBrand;
            }

            _randomBarrage.OnBeginAttack();

            // TargetedDash(패턴 3), MarkedCircle(패턴 4), RelayBrand(패턴 16)은 전역 어그로
            // 대상(Target)이 아니라 직전 지목자를 뺀 후보 중에서 다시 뽑는다 - 안 그러면
            // 같은 사람만 계속 지목당해 3인 역할 분리가 안 된다
            if (_activeKind == PatternKind.TargetedDash)
            {
                _activeTarget = _targetedDash.ResolveAttackTarget(Target, SelectTargetExcludingLast);
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                _activeTarget = _markedCircle.ResolveAttackTarget(Target, SelectTargetExcludingLast);
            }
            else if (_activeKind == PatternKind.RelayBrand)
            {
                _activeTarget = _relayBrand.ResolveAttackTarget(Target, _controller);
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
                _targetedDash.OnBeginAttack(Self.position, _lockedForward, _activeSpec);
            }
            else if (_activeKind == PatternKind.MarkedCircle)
            {
                _markedCircle.OnBeginAttack(_activeTarget, _controller);
            }
            else if (_activeKind == PatternKind.RelayBrand)
            {
                _relayBrand.OnBeginAttack(_controller);
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
            // RelayBrand는 반복마다 폭발 중심이 다르다(대상 전이) - 나머지 패턴은 전부
            // 패턴 하나에 원점 하나라 루프 밖에서 한 번만 계산해도 됐지만, RelayBrand
            // 때문에 반복 인덱스별로 다시 물어야 한다
            for (int i = 0; i < _activeSpec.RepeatCount; ++i)
            {
                Vector3 cueOrigin = (_activeKind == PatternKind.RelayBrand)
                    ? _relayBrand.GetCenter(i)
                    : (_activeKind == PatternKind.MarkedCircle)
                        ? _markedCircle.Center
                        : Self.position;
                // RelayBrand는 반복마다 지목 대상도 바뀐다 - "당신이 지목됨" 개인 UI가 매
                // 반복 실제 대상을 따라가야 한다(BossTelegraphView.cs 594줄)
                uint cueTargetNetId = (_activeKind == PatternKind.RelayBrand)
                    ? _relayBrand.GetTargetNetId(i)
                    : _activeTargetNetId;
                float impactOffset = _activeImpactOffsets[i];
                float startOffset = (i == 0)
                    ? 0f
                    : _activeImpactOffsets[i - 1];
                _controller.ServerBeginTelegraph(new BossTelegraphCue
                {
                    AttackInstanceId = _activeAttackInstanceId,
                    RepeatIndex      = (byte) i,
                    PatternId        = _activePattern.PatternId,
                    TargetNetId      = cueTargetNetId,
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

            if (_activeKind == PatternKind.TargetedDash)
            {
                _targetedDash.TickWindup(deltaTime, Self, _activeTarget, _activeSpec,
                    _activePattern.TelegraphDuration, _stateTimer);
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

            // RelayBrand는 반복마다 다른 대상 netId로 예고를 걸었다(PublishRepeatTelegraphs
            // 참고) - 취소도 같은 netId로 찾아야 한다, 안 그러면 이 예고가 안 지워지고 남는다
            uint impactedTargetNetId = (_activeKind == PatternKind.RelayBrand)
                ? _relayBrand.GetTargetNetId(_activeRepeatIndex)
                : _activeTargetNetId;
            _controller.ServerCancelTelegraph(_activeAttackInstanceId,
                _activeRepeatIndex, impactedTargetNetId);
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

        // RotatingBeam/RandomBarrage처럼 자체 완료 시점을 스스로 판단하는(PatternTickResult
        // 반환) 패턴 전용 FSM 전환. 위 TickWindup 하단의 표준 반복-오프셋 완료 경로와는
        // 별개다 - 그쪽은 ServerCancelTelegraph가 다른 오버로드(repeatIndex 포함)를 쓰고
        // 반복 인덱스 진행/재판정 분기가 얽혀 있어 여기로 합치지 않는다
        private void CompleteSelfManagedAttack()
        {
            _controller.ServerCancelTelegraph(_activeAttackInstanceId);
            _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);
            _stateTimer        = 0f;
            _cooldownRemaining = _controller.ServerGetGlobalCadence(1);
            _state             = AttackState.Recovery;
        }

        private void ResolveImpact()
        {
            uint attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;
            float damage       = _activePattern.Damage * _activeDamageMultiplier;

            if (_activeKind == PatternKind.TargetedDash)
            {
                uint deflectedDefender = (_deflectedAttack == _activeAttackInstanceId &&
                    _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u;
                _targetedDash.ResolveImpact(Self, _activeSpec, damage, attackerNetId,
                    _playerLayerMask, _controller, deflectedDefender);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                uint deflectedDefender = (_deflectedAttack == _activeAttackInstanceId &&
                    _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u;
                _markedCircle.ResolveImpact(_activeSpec, damage, attackerNetId,
                    _playerLayerMask, _controller, deflectedDefender);
                return;
            }

            if (_activeKind == PatternKind.RelayBrand)
            {
                uint deflectedDefender = (_deflectedAttack == _activeAttackInstanceId &&
                    _deflectedRepeat == _activeRepeatIndex) ? _deflectedDefender : 0u;
                _relayBrand.ResolveImpact(_activePattern, _activeRepeatIndex, _activeSpec, damage,
                    attackerNetId, _playerLayerMask, _controller, deflectedDefender);
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
