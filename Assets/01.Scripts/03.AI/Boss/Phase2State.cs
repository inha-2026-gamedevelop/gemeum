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
    /// 2페이즈, 시험하는 판. Phase1State와 같은 네 템플릿을 재사용하되 사거리, 속도가 커지고
    /// 패턴 6(MeleeArc)만 패링 가능하다. 패턴 9(ConeBurst)는 "3단 반복 펄스"까지 구현했다(2026-09-07)
    /// - Windup을 세 번 반복해 매번 새 텔레그래프 큐를 보낸다. 다만 신궁 시전 중단(원거리 캔슬)
    /// 이벤트는 아직 없어 펄스를 끊는 훅은 비어 있다. 패턴 8(MarkedCircle)의 "순차 처형"(3인 전원
    /// 표식 후 순서대로 폭발), 패턴 10(백귀야행, 조건부 발동)은 여전히 단일 판정 MVP/미구현이다 -
    /// 다중 대상, 꼬리 상태 조건은 이 슬라이스 범위 밖이다. Phase1State와 구조가 거의 같지만
    /// 값이 전부 달라 별도 클래스로 뒀다(공유 리팩터링은 사용자 판단으로 미룸, 2026-09-06)
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

        private const int MAX_CANDIDATE_COUNT = 4; // 지금 있는 템플릿 수만큼, 늘어나면 같이 늘린다

        // TODO: BossPatternSO.PatternEntry에 템플릿별 필드가 생기면 SO로 옮긴다. 지금은 스키마가
        // 00.Common 공용 파일이라 팀장 확인 없이 필드를 추가하지 않고, 우리 파트 안에 임시 상수로 둔다
        // 2026-09-07 - 실측 키(2.109m)가 설계 가정(1.85m) 대비 x1.14라 사거리·반경류만 그 비율로 스케일했다.
        // 속도·각도·Telegraph 시간은 BOSS_DESIGN.md 11장 원칙(Windup 고정, 사거리만 확대)대로 그대로 둔다
        private const float MELEE_RANGE                 = 5.13f; // m, 2페이즈 MeleeArc 사거리 (4.5 x1.14)
        private const float MELEE_ANGLE_DEGREES         = 180f;  // 도, 부채꼴 전체 각도
        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

        private const float DASH_SPEED      = 12f;    // m/s, 2페이즈 돌진 속도
        private const float DASH_DISTANCE   = 15.96f; // m, 2페이즈 돌진 거리 (14 x1.14)
        private const float DASH_HIT_RADIUS = 1.37f;  // m, 돌진 도착 지점 명중 판정 반경 (1.2 x1.14)

        private const float MARKED_CIRCLE_RADIUS = 6.27f; // m, 2페이즈 낙인 반경 (5.5 x1.14)

        private const float CONE_RANGE         = 10.26f; // m, 충격파 사거리 (9 x1.14)
        private const float CONE_ANGLE_DEGREES = 60f;     // 도, 충격파 부채꼴 전체 각도

        // 9번(연쇄 부채꼴 3단) 전용 - 계약 3장 "ConeBurst 반복 펄스, 시전 중단 창이 세 번".
        // 신궁의 시전 중단(원거리 캔슬)은 아직 별도 이벤트가 없어(BossController 상단 TODO 참고)
        // 이 슬라이스에서는 펄스 반복 구조만 만들고 캔슬 훅은 비워 둔다
        private const int CONE_BURST_PULSE_COUNT = 3; // 연속 판정 횟수

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;
        private readonly int[]           _candidatePatternIds = new int[MAX_CANDIDATE_COUNT]; // 매 선택마다 재사용, 할당 없음

        private AttackState _state              = AttackState.Idle;
        private float       _stateTimer         = 0f;
        private float       _cooldownRemaining  = 0f;
        private uint        _nextParryWindowId  = 0;

        private Transform                  _activeTarget;
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

        private int _conePulseIndex = 0; // ConeBurst 전용, 0-based. 다른 패턴에서는 안 쓴다

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

        private void TickIdle()
        {
            if ((_cooldownRemaining > 0f) || (Target == null))
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
            _activePattern          = pattern;
            _activeKind             = kind;
            _activeParried          = false;
            _activeExecution        = null;
            _activeAttackInstanceId = _controller.ServerNextAttackInstanceId();
            _stateTimer             = 0f;
            _state                  = AttackState.Windup;
            _conePulseIndex         = 0;

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
            SendTelegraphCue();

            // BossTelegraphView가 아직 없어 예고가 화면에 안 보인다. 확인용 임시 로그 - 뷰 붙으면 지운다
            GDebug.Server($"[보스] {PatternName(pattern.PatternId)} 예고 시작");
        }

        /// <summary> 현재 상태(_lockedForward, _markedCenter 등) 기준으로 예고 큐를 전파한다. 9번 펄스마다 다시 부른다 </summary>
        private void SendTelegraphCue()
        {
            uint targetNetId = _activeTarget.TryGetComponent(out NetworkIdentity targetIdentity) ? targetIdentity.netId : 0u;
            Vector3 cueOrigin = (_activeKind == PatternKind.MarkedCircle) ? _markedCenter : Self.position;

            BossTelegraphCue cue = new BossTelegraphCue
            {
                AttackInstanceId = _activeAttackInstanceId,
                PatternId        = _activePattern.PatternId,
                TargetNetId      = targetNetId,
                Origin           = cueOrigin,
                Direction        = _lockedForward,
                ImpactServerTime = NetworkTime.time + _activePattern.TelegraphDuration,
            };

            _controller.ServerBeginTelegraph(cue);
        }

        private void TickWindup(float deltaTime)
        {
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

            // 9번(연쇄 부채꼴 3단) - 판정 하나가 끝나도 회복으로 넘어가지 않고, 정해진 횟수만큼
            // Windup을 다시 돈다. ConeBurst는 패링 불가라 _activeParried가 true일 일이 없지만,
            // 혹시 몰라 취소된 펄스는 카운트만 올리고 다음 펄스로 넘긴다(무한 반복 방지)
            if (_activeKind == PatternKind.ConeBurst)
            {
                ++_conePulseIndex;
                if (_conePulseIndex < CONE_BURST_PULSE_COUNT)
                {
                    _activeAttackInstanceId = _controller.ServerNextAttackInstanceId();
                    _activeParried          = false;
                    _stateTimer             = 0f;
                    SendTelegraphCue();
                    GDebug.Server($"[보스] {PatternName(_activePattern.PatternId)} 펄스 {_conePulseIndex + 1}/{CONE_BURST_PULSE_COUNT}");
                    return; // Windup 유지, Recovery로 넘어가지 않는다
                }
            }

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
                _state = AttackState.Idle;
            }
        }

        /// <summary> 패링 성공 시 BossAttackExecution이 호출한다. 참조가 다르면 이미 지난 공격의 늦은 통지다 </summary>
        private void HandleAttackParried(BossAttackExecution execution, uint parriedByNetId)
        {
            if ((!ReferenceEquals(execution, _activeExecution)) || (_state != AttackState.Windup))
            {
                return;
            }

            _activeParried = true;
            _controller.RpcCancelTelegraph(_activeAttackInstanceId);
            _controller.ServerReduceGroggyGauge(CharacterKind.Chasa); // 검 사용자만 패링에 성공할 수 있다
        }

        private void TryOpenParryWindow(BossPatternSO.PatternEntry pattern)
        {
            if ((!pattern.IsParryable) || (_activeTarget == null) || (_selfIdentity == null))
            {
                return;
            }

            IParryWindowTarget parryTarget = _activeTarget.GetComponent<IParryWindowTarget>();
            if (parryTarget == null)
            {
                return;
            }

            ++_nextParryWindowId;
            ParryWindowKey key = new ParryWindowKey(_selfIdentity.netId, _nextParryWindowId);

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

            _openParryTarget.CloseParryWindow(_openParryKey);
            _openParryTarget = null;
            _openParryKey    = default;
        }
    }
}
