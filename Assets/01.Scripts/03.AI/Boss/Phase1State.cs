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
    /// 1페이즈, 가르치는 판. 네 템플릿이 전부 돈다 - 패턴 1(여우불 봉 3연격, MeleeArc, 단일 타격
    /// MVP), 패턴 3(순간 접근 3연계, TargetedDash, 단일 돌진 MVP), 패턴 4(여우불 낙인,
    /// MarkedCircle), 패턴 5(여우 구슬 충격파, ConeBurst). 거리 대역별로 근접/중거리/원거리
    /// 후보 하나씩과, 거리 무관인 MarkedCircle이 항상 낀다 - 후보 중 무작위로 하나를 고른다
    /// (가중치 추첨은 아직 없다, 균등 추첨이다).
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

        private const int MAX_CANDIDATE_COUNT = 4; // 지금 있는 템플릿 수만큼, 늘어나면 같이 늘린다

        // TODO: BossPatternSO.PatternEntry에 템플릿별 필드(Range, AngleDegrees, DashSpeed 등)가
        // 생기면 SO로 옮긴다. 지금은 스키마가 00.Common 공용 파일이라 팀장 확인 없이 필드를
        // 추가하지 않고, 우리 파트 안에 임시 상수로 둔다
        // 2026-09-07 - 실측 키(2.109m)가 설계 가정(1.85m) 대비 x1.14라 사거리·반경류만 그 비율로 스케일했다.
        // 속도·각도·Telegraph 시간은 BOSS_DESIGN.md 11장 원칙(Windup 고정, 사거리만 확대)대로 그대로 둔다
        private const float MELEE_RANGE                 = 3.19f; // m, 1페이즈 MeleeArc 사거리 (2.8 x1.14)
        private const float MELEE_ANGLE_DEGREES         = 100f;  // 도, 부채꼴 전체 각도
        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

        private const float DASH_SPEED      = 10f;   // m/s, 1페이즈 돌진 속도
        private const float DASH_DISTANCE   = 9.12f; // m, 1페이즈 돌진 거리 (8 x1.14)
        private const float DASH_HIT_RADIUS = 1.37f; // m, 돌진 도착 지점 명중 판정 반경 (1.2 x1.14)

        private const float MARKED_CIRCLE_RADIUS = 3.99f; // m, 1페이즈 낙인 반경 (3.5 x1.14)

        private const float CONE_RANGE         = 10.26f; // m, 충격파 사거리 (9 x1.14)
        private const float CONE_ANGLE_DEGREES = 60f;     // 도, 충격파 부채꼴 전체 각도

        /****************************************
        *                Fields
        ****************************************/

        // 오프닝 고정 시퀀스(9.3절): 추첨 없이 이 순서대로, 쿨다운 무시하고 발동한다
        private static readonly int[] OPENING_SEQUENCE = { MELEE_ARC_PATTERN_ID, DERIVED_MELEE_PATTERN_ID, MARKED_CIRCLE_PATTERN_ID };

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;
        private readonly int[]           _candidatePatternIds = new int[MAX_CANDIDATE_COUNT]; // 매 선택마다 재사용, 할당 없음

        private AttackState _state              = AttackState.Idle;
        private float       _stateTimer         = 0f;
        private float       _cooldownRemaining  = 0f;
        private int         _openingIndex       = 0; // OPENING_SEQUENCE.Length에 도달하면 오프닝이 끝난 것

        private Transform                  _activeTarget;
        private IDamageable                _activeTargetStatus;
        private BossPatternSO.PatternEntry _activePattern;
        private PatternKind                _activeKind;
        private Vector3                    _lockedForward;  // 예고 시작 시점에 고정한 방향(방향 완전 잠금)
        private Vector3                    _dashDestination;
        private Vector3                    _markedCenter; // MarkedCircle 전용, 예고 시작 시점에 고정
        private uint                       _activeAttackInstanceId;
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
            _activeTarget           = null;
            _activeTargetStatus     = null;
            _stateTimer             = 0f;
            _cooldownRemaining      = _activePattern.Cooldown;
            _state                  = AttackState.Recovery;

            CloseParryWindowIfOpen();
            _controller.ServerCancelTelegraph(attackInstanceId);
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
            int   candidateCount = 0;

            // 근접/중거리/원거리 거리 대역별로 후보 하나씩만 낀다
            if (distance <= MELEE_RANGE)
            {
                _candidatePatternIds[candidateCount] = MELEE_ARC_PATTERN_ID;
                ++candidateCount;
            }
            else if (distance <= CONE_RANGE)
            {
                _candidatePatternIds[candidateCount] = CONE_BURST_PATTERN_ID;
                ++candidateCount;
            }
            else
            {
                _candidatePatternIds[candidateCount] = TARGETED_DASH_PATTERN_ID;
                ++candidateCount;
            }

            // MarkedCircle은 거리 무관 패턴이라 항상 후보에 낀다
            _candidatePatternIds[candidateCount] = MARKED_CIRCLE_PATTERN_ID;
            ++candidateCount;

            int chosenPatternId = _candidatePatternIds[Random.Range(0, candidateCount)];

            if (!GameDB.Boss.TryGetPattern(chosenPatternId, out BossPatternSO.PatternEntry pattern))
            {
                return;
            }

            BeginAttack(pattern, KindOf(chosenPatternId));
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

        private void BeginAttack(BossPatternSO.PatternEntry pattern, PatternKind kind)
        {
            _activeTarget           = Target;
            _activeTarget.TryGetComponent(out _activeTargetStatus);
            _activePattern          = pattern;
            _activeKind             = kind;
            _activeParried          = false;
            _activeExecution        = null;
            _activeAttackInstanceId = _controller.ServerNextAttackInstanceId();
            _stateTimer             = 0f;
            _state                  = AttackState.Windup;

            Vector3 direction = _activeTarget.position - Self.position;
            direction.y = 0f;
            direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : Self.forward;

            _lockedForward = direction; // 방향 완전 잠금 - 예고 중 대상이 움직여도 안 따라간다

            if (kind == PatternKind.TargetedDash)
            {
                _dashDestination = Self.position + (direction * DASH_DISTANCE);
            }
            else if (kind == PatternKind.MarkedCircle)
            {
                // 위치 확정 후 잠금 - 예고 중 대상이 움직여도 낙인 중심은 안 따라간다
                _markedCenter = _activeTarget.position;
            }

            TryOpenParryWindow(pattern);

            uint targetNetId = _activeTarget.TryGetComponent(out NetworkIdentity targetIdentity) ? targetIdentity.netId : 0u;
            Vector3 cueOrigin = (kind == PatternKind.MarkedCircle) ? _markedCenter : Self.position;

            BossTelegraphCue cue = new BossTelegraphCue
            {
                AttackInstanceId = _activeAttackInstanceId,
                PatternId        = pattern.PatternId,
                TargetNetId      = targetNetId,
                Origin           = cueOrigin,
                Direction        = direction,
                ImpactServerTime = NetworkTime.time + pattern.TelegraphDuration,
            };

            _controller.ServerBeginTelegraph(cue);

            // BossTelegraphView가 아직 없어 예고가 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(pattern.PatternId)} 예고 시작");
        }

        private void TickWindup(float deltaTime)
        {
            if ((_activeTarget == null) || ((_activeTargetStatus != null) && (_activeTargetStatus.IsDead)))
            {
                CancelActiveAttack();
                return;
            }

            if (_activeKind == PatternKind.TargetedDash)
            {
                // 앞뒤 절반 추적 대신 시작 시점에 전부 확정하는 완전 잠금으로 단순화했다(MVP)
                Self.position = Vector3.MoveTowards(Self.position, _dashDestination, DASH_SPEED * deltaTime);
            }

            _stateTimer += deltaTime;
            if (_stateTimer < _activePattern.TelegraphDuration)
            {
                return;
            }

            CloseParryWindowIfOpen();

            if (!_activeParried)
            {
                ResolveImpact();
            }

            _controller.ServerCancelTelegraph(_activeAttackInstanceId);
            _activeExecution = null;

            // 회복으로 넘어가는 이 지점이 패턴 종료다. 반복 패턴은 마지막 판정에서 한 번만 온다
            _controller.ServerNotifyPatternEnded(_activePattern.PatternId, _activeAttackInstanceId);

            _stateTimer        = 0f;
            _cooldownRemaining = _activePattern.Cooldown;
            _state             = AttackState.Recovery;
        }

        private void ResolveImpact()
        {
            // BossTelegraphView가 아직 없어 타격이 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(_activePattern.PatternId)} 타격 판정");

            uint attackerNetId = (_selfIdentity != null) ? _selfIdentity.netId : 0u;

            if (_activeKind == PatternKind.TargetedDash)
            {
                // 도착 지점 기준 전방위 판정이라 각도 제한이 필요 없어 360도로 둔다
                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    Self.forward,
                    DASH_HIT_RADIUS,
                    360f,
                    _activePattern.Damage,
                    attackerNetId,
                    _playerLayerMask);
                return;
            }

            if (_activeKind == PatternKind.MarkedCircle)
            {
                // 원점이 보스가 아니라 예고 시작 시점에 고정한 낙인 중심이다, 각도 제한도 없다
                BossPatternRunner.ExecuteMeleeArc(
                    _markedCenter,
                    Vector3.forward,
                    MARKED_CIRCLE_RADIUS,
                    360f,
                    _activePattern.Damage,
                    attackerNetId,
                    _playerLayerMask);
                return;
            }

            if (_activeKind == PatternKind.ConeBurst)
            {
                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    _lockedForward,
                    CONE_RANGE,
                    CONE_ANGLE_DEGREES,
                    _activePattern.Damage,
                    attackerNetId,
                    _playerLayerMask);
                return;
            }

            // MeleeArc. 방향은 예고 시작 시점에 잠근 값을 쓴다(방향 완전 잠금)
            BossPatternRunner.ExecuteMeleeArc(
                Self.position,
                _lockedForward,
                MELEE_RANGE,
                MELEE_ANGLE_DEGREES,
                _activePattern.Damage,
                attackerNetId,
                _playerLayerMask);
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

            CancelActiveAttack();
            _controller.ServerReduceGroggyGauge(CharacterKind.Chasa); // 검 사용자만 패링에 성공할 수 있다
        }

        private void TryOpenParryWindow(BossPatternSO.PatternEntry pattern)
        {
            if ((!pattern.IsParryable) || (_activeTarget == null) || (_selfIdentity == null))
            {
                return;
            }

            if (!_activeTarget.TryGetComponent(out IParryWindowTarget parryTarget))
            {
                return;
            }

            ParryWindowKey key = new ParryWindowKey(_selfIdentity.netId, _activeAttackInstanceId);

            double now        = NetworkTime.time;
            double impactTime = now + pattern.TelegraphDuration;

            BossAttackExecution execution = new BossAttackExecution(MELEE_PARRY_WINDOW_DURATION, HandleAttackParried);

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
