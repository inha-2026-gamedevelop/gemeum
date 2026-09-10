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
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int MELEE_ARC_PATTERN_ID     = 6; // 거대 꼬리치기
        private const int TARGETED_DASH_PATTERN_ID = 7; // 마수 돌진
        private const int MARKED_CIRCLE_PATTERN_ID = 8; // 표식 순환 처형
        private const int CONE_BURST_PATTERN_ID    = 9; // 연쇄 부채꼴 3단
        // 위 넷 다 GameDB.Boss._patterns의 PatternId와 맞춘다. 10번(백귀야행)은
        // 조건부 발동(꼬리 상태)이라 쿨다운 기반 선택기 대상이 아니다 - 이 슬라이스에서 다루지 않는다

        private const int MAX_CANDIDATE_COUNT      = 4; // 현재 독립 후보 수, 패턴 10은 조건부 발동
        private const int SELECTABLE_PATTERN_COUNT = 4;

        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

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
            if ((_cooldownRemaining > 0f) || (Target == null))
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

        private void ResolveImpact()
        {
            // BossTelegraphView가 아직 없어 타격이 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(_activePattern.PatternId)} 타격 판정");

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
