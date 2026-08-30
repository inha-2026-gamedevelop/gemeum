// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.InputSystem;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// 이동 입력을 일정 주기로 서버에 보내고, 서버가 순서·충돌을 검증한 뒤 위치를 확정한다.
    /// 소유 클라이언트는 로컬 예측 후 서버 스냅샷으로 재조정하고, 원격 클라이언트는 NetworkTransform 보간만 쓴다
    /// </summary>
    public class PlayerController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private InputActionAsset _inputActions;

        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _rollAction;

        private CharacterController       _characterController;
        private PlayerPrediction          _prediction;
        private PlayerStatus              _status;
        private PlayerAnimationController _animation;
        private NetworkTransformReliable  _networkTransform;

        private double _lastInputSendTime = 0.0;
        private uint   _localSequence     = 0;

        // 서버 전용
        private readonly Queue<PlayerMoveInput> _serverInputBuffer = new Queue<PlayerMoveInput>();

        private uint   _lastAcceptedSequence        = 0;
        private uint   _lastProcessedSequence       = 0;
        private double _lastInputReceivedServerTime = -1.0;
        private double _lastMotionServerTime        = -1.0; // 틱 호출 사이에도 이어져야 한다, 지역 변수면 매 틱 delta가 0이 된다

        // 구르기. 이동 입력과 달리 서버가 방향·속도를 고정해 두고 재생하는 단발 상태다.
        // 판정은 전부 서버가 하지만 모든 관찰자가 구르기 모션을 봐야 하므로 진행 여부만 동기화한다
        [SyncVar(hook = nameof(OnRollingChanged))] private bool _isRolling = false;

        // 서버 전용 — 아래 값은 판정에만 쓰이고 클라이언트가 알 필요가 없다
        private Vector2 _rollDirection               = Vector2.zero;
        private double  _rollEndServerTime           = 0.0;
        private double  _rollInvincibleEndServerTime = 0.0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _prediction           = GetComponent<PlayerPrediction>();
            _status               = GetComponent<PlayerStatus>();
            _animation            = GetComponent<PlayerAnimationController>();
            _networkTransform     = GetComponent<NetworkTransformReliable>();
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            if (_moveAction == null)
            {
                return;
            }

            double now = NetworkTime.time;
            if ((now - _lastInputSendTime) < Constants.Network.PLAYER_INPUT_SEND_INTERVAL)
            {
                return;
            }

            _lastInputSendTime = now;

            PlayerMoveInput input = new PlayerMoveInput
            {
                MoveDirection             = Vector2.ClampMagnitude(_moveAction.ReadValue<Vector2>(), 1f),
                SprintHeld                = ((_sprintAction != null) && (_sprintAction.ReadValue<float>() > 0f)),
                ClientEstimatedServerTime = now,
                Sequence                  = ++_localSequence,
            };

            // 호스트는 스스로가 서버라 지연이 없다, 별도 예측이 필요 없다
            if (!isServer)
            {
                _prediction.Predict(input, ResolveMoveSpeed);
            }

            CmdSendMoveInput(input);
        }

        /****************************************
        *              Input
        ****************************************/

        private void OnRollPerformed(InputAction.CallbackContext context)
        {
            Vector2 direction = _moveAction.ReadValue<Vector2>();
            if (direction.sqrMagnitude < (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                direction = Vector2.up; // 입력이 없으면 정면(월드 +Z)으로 구른다
            }

            CmdRequestRoll(direction.normalized);
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        // 서버·클라이언트 모두에서 값이 바뀔 때 호출된다, 판정에는 관여하지 않는다
        private void OnRollingChanged(bool oldRolling, bool newRolling)
        {
            if (!newRolling || (_animation == null))
            {
                return; // 종료는 애니메이터가 클립 길이로 알아서 빠져나온다
            }

            _animation.PlayRoll();
        }

        /****************************************
        *              Authority Callbacks
        ****************************************/

        public override void OnStartAuthority()
        {
            // NetworkTransformReliable은 syncDirection이 ServerToClient라 소유 여부와 무관하게
            // 서버 스냅샷을 클라이언트 transform에 계속 덮어쓴다(Mirror의 IsClientWithAuthority가
            // syncDirection 기준이라 소유해도 false). PlayerPrediction과 같은 transform을 두고
            // 싸워 떨림·정지 현상이 생기므로, 원격 소유 클라이언트에서는 로컬에서만 꺼 둔다.
            // 호스트는 애초에 NetworkTransformReliable.UpdateClient가 돌지 않아 대상이 아니다
            if ((_networkTransform != null) && !isServer)
            {
                _networkTransform.enabled = false;
            }

            if (_inputActions == null)
            {
                Debug.LogError("[PlayerController] InputActionAsset이 비어 있어 이동 입력을 받을 수 없다", this);
                return;
            }

            InputActionMap map = _inputActions.FindActionMap(Constants.Player.ACTION_MAP);
            _moveAction   = map.FindAction(Constants.Player.ACTION_MOVE);
            _sprintAction = map.FindAction(Constants.Player.ACTION_SPRINT);
            _rollAction   = map.FindAction(Constants.Player.ACTION_ROLL);

            _moveAction.Enable();
            _sprintAction.Enable();

            _rollAction.performed += OnRollPerformed;
            _rollAction.Enable();
        }

        public override void OnStopAuthority()
        {
            if (_moveAction != null)
            {
                _moveAction.Disable();
            }

            if (_sprintAction != null)
            {
                _sprintAction.Disable();
            }

            if (_rollAction != null)
            {
                _rollAction.performed -= OnRollPerformed;
                _rollAction.Disable();
            }
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[PlayerController] AuthorityTickScheduler가 없어 이동을 처리할 수 없다", this);
                return;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_MOVEMENT, ServerProcessMovementTick);
        }

        public override void OnStopServer()
        {
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerProcessMovementTick);
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command(channel = Channels.Unreliable)]
        private void CmdSendMoveInput(PlayerMoveInput input)
        {
            if (input.Sequence <= _lastAcceptedSequence)
            {
                return; // 과거·중복 입력은 버린다, 위치를 보정하지 않는다
            }

            input.MoveDirection = Vector2.ClampMagnitude(input.MoveDirection, 1f); // 클라 입력 크기를 신뢰하지 않는다

            _lastAcceptedSequence        = input.Sequence;
            _lastInputReceivedServerTime = NetworkTime.time;

            _serverInputBuffer.Enqueue(input);
        }

        [Command]
        private void CmdRequestRoll(Vector2 direction)
        {
            if (_isRolling)
            {
                return; // 이미 구르는 중이면 재요청을 그냥 버린다, 재전송도 정상 트래픽이다
            }

            RunPhase        phase     = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            PlayerLifeState lifeState = (_status != null) ? _status.LifeState : PlayerLifeState.Alive;

            if (!PlayerActionGate.CanAct(phase, lifeState))
            {
                return;
            }

            if (!GameDB.IsInitialized)
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            if ((_status == null) || !_status.TrySpendStamina(combat.RollStaminaCost))
            {
                return;
            }

            double now = NetworkTime.time;

            _isRolling                   = true;
            _rollDirection               = Vector2.ClampMagnitude(direction, 1f); // 클라 방향값을 신뢰하지 않는다
            _rollEndServerTime           = now + combat.RollTotalDuration;
            _rollInvincibleEndServerTime = now + combat.RollInvincibleDuration;

            _status.SetInvincible(true);
        }

        /****************************************
        *              Server Tick
        ****************************************/

        // AuthorityTickScheduler가 TICK_PRIORITY_MOVEMENT 순서로 매 서버 틱 호출한다
        private void ServerProcessMovementTick(float fixedDeltaTime)
        {
            bool hasBufferedInput = (_serverInputBuffer.Count > 0);
            bool isInputExpired = ((_lastInputReceivedServerTime >= 0.0)
                && ((NetworkTime.time - _lastInputReceivedServerTime) > Constants.Network.PLAYER_INPUT_EXPIRY));

            if (!hasBufferedInput && !isInputExpired && !_isRolling)
            {
                return; // 새 입력도 없고 만료도 안 됐고 구르는 중도 아니면 이번 틱은 건너뛴다
            }

            RunPhase        phase     = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            PlayerLifeState lifeState = (_status != null) ? _status.LifeState : PlayerLifeState.Alive;
            bool            canMove   = PlayerActionGate.CanAct(phase, lifeState);

            if (_isRolling)
            {
                UpdateRollState(NetworkTime.time, canMove);
            }

            while (_serverInputBuffer.Count > 0)
            {
                PlayerMoveInput input = _serverInputBuffer.Dequeue();
                _lastProcessedSequence = input.Sequence;

                if (!canMove || _isRolling)
                {
                    continue; // sequence는 갱신하되 위치는 바꾸지 않는다, 구르기 중에는 일반 이동 입력을 무시한다
                }

                // _lastMotionServerTime은 필드다 — 틱 하나에 입력이 1개만 들어오는 게 보통이라
                // 지역 변수로 두면 매번 "이전 시각 없음"이 되어 delta가 항상 0이 된다
                double effectiveTime = ClampInputTime(input.ClientEstimatedServerTime, NetworkTime.time);
                float  deltaTime = (_lastMotionServerTime < 0.0)
                    ? 0f
                    : Mathf.Clamp((float) (effectiveTime - _lastMotionServerTime), 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
                _lastMotionServerTime = effectiveTime;

                ApplyServerMotion(input.MoveDirection, ResolveMoveSpeed(input.SprintHeld), deltaTime);
            }

            if (_isRolling)
            {
                ApplyRollMotion(fixedDeltaTime);
            }

            TargetMoveSnapshot(transform.position, _lastProcessedSequence);
        }

        private void ApplyServerMotion(Vector2 moveDirection, float moveSpeed, float deltaTime)
        {
            Vector3 nextPosition = PlayerMotionModel.ComputeNextPosition(transform.position, moveDirection, moveSpeed, deltaTime);
            Vector3 delta        = nextPosition - transform.position;

            _characterController.Move(delta);
        }

        /// <summary> 무적 만료·구르기 종료·생명 상태 이탈(사망 등)을 확인한다. 위치는 바꾸지 않는다 </summary>
        private void UpdateRollState(double now, bool canMove)
        {
            if (_status.IsInvincible && (now >= _rollInvincibleEndServerTime))
            {
                _status.SetInvincible(false);
            }

            if (!canMove || (now >= _rollEndServerTime))
            {
                _isRolling = false;

                if (_status.IsInvincible)
                {
                    _status.SetInvincible(false); // 구르는 도중 사망 등으로 강제 종료되면 무적도 같이 끈다
                }
            }
        }

        private void ApplyRollMotion(float deltaTime)
        {
            CombatConfigSO combat    = GameDB.Combat;
            float          rollSpeed = combat.RollDistance / combat.RollTotalDuration;

            Vector3 nextPosition = PlayerMotionModel.ComputeNextPosition(transform.position, _rollDirection, rollSpeed, deltaTime);
            Vector3 delta        = nextPosition - transform.position;

            _characterController.Move(delta);
        }

        private static double ClampInputTime(double clientEstimatedServerTime, double serverNow)
        {
            double earliest = serverNow - Constants.Network.MAX_INPUT_TIME_BEHIND;
            double latest   = serverNow + Constants.Network.MAX_INPUT_TIME_AHEAD;

            return Math.Clamp(clientEstimatedServerTime, earliest, latest);
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetMoveSnapshot(Vector3 serverPosition, uint lastProcessedSequence)
        {
            if (isServer)
            {
                return; // 호스트는 스스로가 서버라 재조정할 예측이 없다
            }

            _prediction.Reconcile(serverPosition, lastProcessedSequence, ResolveMoveSpeed);
        }

        /****************************************
        *                Methods
        ****************************************/

        private float ResolveMoveSpeed(bool sprintHeld)
        {
            if (!GameDB.IsInitialized)
            {
                return 0f;
            }

            return sprintHeld ? GameDB.Combat.RunSpeed : GameDB.Combat.WalkSpeed;
        }
    }
}
