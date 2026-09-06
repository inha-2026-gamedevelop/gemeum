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
    /// 1페이즈, 가르치는 판. 지금은 패턴 1(여우불 봉 3연격)을 단일 타격 MVP로 축소해 하나만 돈다.
    /// 연격, 엇박, 나머지 세 템플릿은 이 슬라이스가 L2로 검증된 뒤 채운다. 거리, 쿨다운 필터만 있고
    /// 선택기의 가중치 추첨은 아직 없다(후보가 하나뿐이라 필요 없다)
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

        /****************************************
        *              Constants
        ****************************************/

        private const int MELEE_ARC_PATTERN_ID = 1; // 여우불 봉 3연격, GameDB.Boss._patterns의 PatternId와 맞춘다

        // TODO: BossPatternSO.PatternEntry에 Range/AngleDegrees/ParryWindowDuration 필드가
        // 생기면 SO로 옮긴다. 지금은 스키마가 00.Common 공용 파일이라 팀장 확인 없이 필드를
        // 추가하지 않고, 우리 파트 안에 임시 상수로 둔다
        private const float MELEE_RANGE                 = 2.8f;  // m, 1페이즈 MeleeArc 사거리
        private const float MELEE_ANGLE_DEGREES         = 100f;  // 도, 부채꼴 전체 각도
        private const float MELEE_PARRY_WINDOW_DURATION = 0.32f; // 초, 패링 판정 창 길이

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossController  _controller;
        private readonly NetworkIdentity _selfIdentity;
        private readonly int             _playerLayerMask;

        private AttackState _state              = AttackState.Idle;
        private float       _stateTimer         = 0f;
        private float       _cooldownRemaining  = 0f;
        private uint        _nextParryWindowId  = 0;

        private Transform                  _activeTarget;
        private BossPatternSO.PatternEntry _activePattern;
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

            if (!GameDB.Boss.TryGetPattern(MELEE_ARC_PATTERN_ID, out BossPatternSO.PatternEntry pattern))
            {
                return;
            }

            float distance = Vector3.Distance(Self.position, Target.position);
            if (distance > MELEE_RANGE)
            {
                return; // 사거리 밖이면 허공을 친다
            }

            BeginAttack(pattern);
        }

        private void BeginAttack(BossPatternSO.PatternEntry pattern)
        {
            _activeTarget           = Target;
            _activePattern          = pattern;
            _activeParried          = false;
            _activeExecution        = null;
            _activeAttackInstanceId = _controller.ServerNextAttackInstanceId();
            _stateTimer             = 0f;
            _state                  = AttackState.Windup;

            TryOpenParryWindow(pattern);

            Vector3 direction = _activeTarget.position - Self.position;
            direction.y = 0f;
            direction   = (direction.sqrMagnitude > 0.0001f) ? direction.normalized : Self.forward;

            uint targetNetId = _activeTarget.TryGetComponent(out NetworkIdentity targetIdentity) ? targetIdentity.netId : 0u;

            BossTelegraphCue cue = new BossTelegraphCue
            {
                AttackInstanceId = _activeAttackInstanceId,
                PatternId        = pattern.PatternId,
                TargetNetId      = targetNetId,
                Origin           = Self.position,
                Direction        = direction,
                ImpactServerTime = NetworkTime.time + pattern.TelegraphDuration,
            };

            _controller.ServerBeginTelegraph(cue);
        }

        private void TickWindup(float deltaTime)
        {
            _stateTimer += deltaTime;
            if (_stateTimer < _activePattern.TelegraphDuration)
            {
                return;
            }

            CloseParryWindowIfOpen();

            if (!_activeParried)
            {
                Vector3 forward = _activeTarget.position - Self.position;
                forward.y = 0f;
                forward   = (forward.sqrMagnitude > 0.0001f) ? forward.normalized : Self.forward;

                BossPatternRunner.ExecuteMeleeArc(
                    Self.position,
                    forward,
                    MELEE_RANGE,
                    MELEE_ANGLE_DEGREES,
                    _activePattern.Damage,
                    (_selfIdentity != null) ? _selfIdentity.netId : 0u,
                    _playerLayerMask);
            }

            _stateTimer        = 0f;
            _cooldownRemaining = _activePattern.Cooldown;
            _state             = AttackState.Recovery;
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
