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
    /// 구슬 충격파, ConeBurst). 거리 대역별로 근접/중거리/원거리 거리 대역에 맞는 후보와,
    /// 거리 무관인 MarkedCircle을 선택기에 넘긴다. 선택기는 개별 쿨다운과 직전 패턴을 걸러낸
    /// 뒤 남은 후보를 균등 추첨한다. 대상을 고르는 패턴(TargetedDash·MarkedCircle)은 직전
    /// 지목자를 제외한다(SelectTargetExcludingLast, BOSS_PATTERN_CONTRACT.md 5장 어그로 규칙).
    /// 전투 시작 직후엔 오프닝 고정 시퀀스(1 -> 2 -> 4)가 먼저 돈다, 쿨다운을 무시하고 순서대로
    /// 발동한다 - 패턴 2(반월 베기)는 패턴 1과 같은 MeleeArc 판정(사거리, 각도 동일)이고
    /// 윈드업만 짧고 패링이 안 된다, 독립 쿨다운이 없어 후보 추첨 대상이 아니다
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
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int MELEE_ARC_PATTERN_ID     = 1; // 여우불 봉 3연격
        private const int DERIVED_MELEE_PATTERN_ID = 2; // 반월 베기, 패턴 1의 파생. 오프닝 전용, 후보 추첨 대상 아님
        private const int TARGETED_DASH_PATTERN_ID = 3; // 순간 접근 3연계
        private const int MARKED_CIRCLE_PATTERN_ID = 4; // 여우불 낙인
        private const int CONE_BURST_PATTERN_ID    = 5; // 여우 구슬 충격파
        // 위 다섯 다 GameDB.Boss._patterns의 PatternId와 맞춘다

        private const int MAX_CANDIDATE_COUNT     = 4; // 현재 독립 후보 수, 패턴 2는 오프닝 전용
        private const int SELECTABLE_PATTERN_COUNT = 4;

        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

        // 예고(TelegraphDuration) 앞 절반은 대상을 따라가고 뒤 절반은 방향을 확정한다
        // (BOSS_PATTERN_CONTRACT.md 11.2절 확정값). 완전 잠금이면 정지 표적을 지나쳐버리고,
        // 완전 추적이면 회피 창이 없어진다 - 그 절충
        private const float DASH_TRACK_RATIO = 0.5f;

        // 대상을 고르는 패턴(TargetedDash·MarkedCircle) 공용 - 직전 지목자를 뺀 후보를 다시
        // 뽑을 때 쓰는 스캔 반경. BossController.RefreshTarget과 같은 반경이다
        // (BOSS_PATTERN_CONTRACT.md 5장 어그로 규칙)
        private const float TARGET_EXCLUSION_SCAN_RADIUS = 30f;

        /****************************************
        *                Fields
        ****************************************/

        // 오프닝 고정 시퀀스(9.3절): 추첨 없이 이 순서대로, 쿨다운 무시하고 발동한다
        private static readonly int[] OPENING_SEQUENCE = { MELEE_ARC_PATTERN_ID, DERIVED_MELEE_PATTERN_ID, MARKED_CIRCLE_PATTERN_ID };

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;
        private static readonly int[] G_SELECTABLE_PATTERN_IDS =
        {
            MELEE_ARC_PATTERN_ID,
            TARGETED_DASH_PATTERN_ID,
            MARKED_CIRCLE_PATTERN_ID,
            CONE_BURST_PATTERN_ID,
        };

        private readonly BossPatternSelector.Candidate[] _candidatePatterns =
            new BossPatternSelector.Candidate[MAX_CANDIDATE_COUNT];
        private readonly float[] _patternCooldowns = new float[SELECTABLE_PATTERN_COUNT];
        private readonly System.Random _random = new System.Random();

        // 대상을 고르는 패턴(TargetedDash·MarkedCircle) 공용 - 직전 지목자를 뺀 후보를 다시
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

            if (patternId == CONE_BURST_PATTERN_ID)
            {
                return PatternKind.ConeBurst;
            }

            return PatternKind.MeleeArc;
        }

        // 화면 로그용 이름. 기획 문서(boss-pattern-design) 표기를 그대로 쓴다
        private static string PatternName(int patternId)
        {
            switch (patternId)
            {
                case MELEE_ARC_PATTERN_ID:     return "여우불 봉 3연격";
                case DERIVED_MELEE_PATTERN_ID: return "반월 베기";
                case TARGETED_DASH_PATTERN_ID: return "순간 접근 3연계";
                case MARKED_CIRCLE_PATTERN_ID: return "여우불 낙인";
                case CONE_BURST_PATTERN_ID:    return "여우 구슬 충격파";
                default:                       return $"패턴 {patternId}";
            }
        }

        // 직전 지목자를 뺀 생존 플레이어 중 가장 가까운 대상을 고른다. 후보가 없으면(생존자가
        // 직전 지목자 하나뿐) null을 돌려주고 호출부가 Target(전역 어그로 대상)으로 대체한다 -
        // "후보가 0이 되면 직전 지목자 제외를 먼저 푼다"는 어그로 규칙의 필터 우선순위.
        // TargetedDash(패턴 3)·MarkedCircle(패턴 4) 둘 다 이 헬퍼를 쓴다(BOSS_PATTERN_CONTRACT.md 5장)
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

            // TargetedDash(패턴 3)·MarkedCircle(패턴 4)은 전역 어그로 대상(Target)이 아니라
            // 직전 지목자를 뺀 후보 중에서 다시 뽑는다 - 안 그러면 같은 사람만 계속 지목당해
            // 3인 역할 분리가 안 된다(BOSS_PATTERN_CONTRACT.md 5장)
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

                // 러프 VFX 연결(이슈 검증용) — 낙인은 예고 시작 시점에도 신호를 쏜다
                _controller.ServerPlayPatternTelegraphVfx(BossPatternSO.PatternTemplate.MarkedCircle, _markedCenter, Vector3.forward);
            }

            _activeTargetNetId = _activeTarget.TryGetComponent(out NetworkIdentity targetIdentity)
                ? targetIdentity.netId : 0u;
            PublishRepeatTelegraphs();
            TryOpenParryWindow(pattern, _activeRepeatIndex);

            // BossTelegraphView가 아직 없어 예고가 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(pattern.PatternId)} 예고 시작");
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

        private void ResolveImpact()
        {
            // BossTelegraphView가 아직 없어 타격이 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(_activePattern.PatternId)} 타격 판정");

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
                    _controller);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                // 러프 VFX 연결(이슈 검증용)
                _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MarkedCircle, _markedCenter, Vector3.forward);

                // 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
                BossPatternRunner.ExecuteMeleeArc(
                    _markedCenter,
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
                    _controller);
                return;
            }

            // MeleeArc. 방향은 예고 시작 시점에 잠근 값을 쓴다(방향 완전 잠금)
            // 러프 VFX 연결(이슈 검증용) — 반월 베기(파생 패턴)도 여기로 들어와 같은 VFX를 쓴다
            _controller.ServerPlayPatternImpact(BossPatternSO.PatternTemplate.MeleeArc, Self.position, _lockedForward);

            BossPatternRunner.ExecuteMeleeArc(
                Self.position,
                _lockedForward,
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
            double impactTime = _activeAttackStartServerTime +
                _activeImpactOffsets[repeatIndex];

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
