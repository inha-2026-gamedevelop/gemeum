// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

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
    /// 서버는 Rigidbody 속도로 이동시키고 Y는 중력에 맡기며, 물리 스텝에서만 값을 만진다.
    /// 소유 클라이언트는 로컬 예측 후 서버 스냅샷으로 재조정하고, 원격 클라이언트는 NetworkTransform 보간만 쓴다
    /// </summary>
    public class PlayerController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Rigidbody                 _rigidbody;
        private PlayerPrediction          _prediction;
        private PlayerStatus              _status;
        private PlayerAnimationController _animation;
        private NetworkTransformReliable  _networkTransform;

        private double _lastInputSendTime = 0.0;
        private uint   _localSequence     = 0;
        private bool   _rollRequested     = false; // Update에서 눌린 프레임을 잡아 두고 전송 주기에 실어 보낸다

        // 서버 전용
        private readonly Queue<PlayerMoveInput> _serverInputBuffer = new Queue<PlayerMoveInput>();

        private uint    _lastAcceptedSequence        = 0;
        private uint    _lastProcessedSequence       = 0;
        private double  _lastInputReceivedServerTime = -1.0;
        private Vector2 _lastMoveDirection           = Vector2.zero;
        private Vector2 _lastFacingDirection         = Vector2.up; // 입력이 멈춰도 유지되는 마지막 이동 방향, 제자리 구르기가 이 방향을 쓴다
        private bool    _lastSprintHeld              = false;
        private bool    _wasMovingLastTick           = false;

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
            _rigidbody        = GetComponent<Rigidbody>();
            _prediction       = GetComponent<PlayerPrediction>();
            _status           = GetComponent<PlayerStatus>();
            _animation        = GetComponent<PlayerAnimationController>();
            _networkTransform = GetComponent<NetworkTransformReliable>();
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            // 전송 주기와 무관하게 매 프레임 본다, 30Hz로 걸러내면 누른 프레임을 놓친다
            if (Input.GetKeyDown(Constants.Player.KEY_ROLL))
            {
                _rollRequested = true;
            }

            double now = NetworkTime.time;
            if ((now - _lastInputSendTime) < Constants.Network.PLAYER_INPUT_SEND_INTERVAL)
            {
                return;
            }

            _lastInputSendTime = now;

            if (_rollRequested)
            {
                _rollRequested = false;
                CmdRequestRoll(ResolveRollDirection());
            }

            Vector2 moveAxis = new Vector2(
                Input.GetAxisRaw(Constants.Player.AXIS_HORIZONTAL),
                Input.GetAxisRaw(Constants.Player.AXIS_VERTICAL));

            PlayerMoveInput input = new PlayerMoveInput
            {
                MoveDirection             = Vector2.ClampMagnitude(moveAxis, 1f),
                SprintHeld                = Input.GetKey(Constants.Player.KEY_SPRINT),
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
        }

        /****************************************
        *              Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            // 원격 인스턴스의 물리가 스스로 돌면 서버 스냅샷·로컬 예측과 위치를 두고 싸운다.
            // 호스트는 자기 자신이 서버라 물리를 그대로 써야 하므로 제외한다
            if (!isServer && (_rigidbody != null))
            {
                _rigidbody.isKinematic = true;
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
            _rollDirection               = ResolveServerRollDirection(direction);
            _rollEndServerTime           = now + combat.RollTotalDuration;
            _rollInvincibleEndServerTime = now + combat.RollInvincibleDuration;

            _status.SetInvincible(true);
        }

        /****************************************
        *              Server Tick
        ****************************************/

        // AuthorityTickScheduler가 물리 스텝마다 TICK_PRIORITY_MOVEMENT 순서로 호출한다
        private void ServerProcessMovementTick(float fixedDeltaTime)
        {
            DrainInputBuffer();

            RunPhase        phase     = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            PlayerLifeState lifeState = (_status != null) ? _status.LifeState : PlayerLifeState.Alive;
            bool            canMove   = PlayerActionGate.CanAct(phase, lifeState);

            if (_isRolling)
            {
                UpdateRollState(NetworkTime.time, canMove);
            }

            Vector2 direction = Vector2.zero;
            float   speed     = 0f;

            if (canMove && _isRolling)
            {
                CombatConfigSO combat = GameDB.Combat;

                direction = _rollDirection; // 구르기 중에는 일반 이동 입력을 무시한다
                speed     = combat.RollDistance / combat.RollTotalDuration;
            }
            else if (canMove)
            {
                direction = _lastMoveDirection;
                speed     = ResolveMoveSpeed(_lastSprintHeld);
            }

            ApplyPlanarVelocity(direction, speed);

            // 멈춰 있는 동안까지 매 물리 스텝 스냅샷을 보내면 3연결에 그대로 낭비가 된다
            bool isMoving = ((direction.sqrMagnitude > 0f) && (speed > 0f));
            if (isMoving || _wasMovingLastTick)
            {
                TargetMoveSnapshot(_rigidbody.position, _lastProcessedSequence);
            }

            _wasMovingLastTick = isMoving;
        }

        // 버퍼에 쌓인 입력을 순서대로 비우고 마지막 값만 남긴다.
        // 속도로 이동시키므로 물리 스텝 하나에 적용할 방향은 하나뿐이다
        private void DrainInputBuffer()
        {
            bool isInputExpired = ((_lastInputReceivedServerTime >= 0.0)
                && ((NetworkTime.time - _lastInputReceivedServerTime) > Constants.Network.PLAYER_INPUT_EXPIRY));

            if (isInputExpired)
            {
                _lastMoveDirection = Vector2.zero; // 통신이 끊기면 마지막 입력을 무한히 유지하지 않는다
                _lastSprintHeld    = false;
            }

            while (_serverInputBuffer.Count > 0)
            {
                PlayerMoveInput input = _serverInputBuffer.Dequeue();

                _lastProcessedSequence = input.Sequence;
                _lastMoveDirection     = input.MoveDirection;
                _lastSprintHeld        = input.SprintHeld;

                // 입력을 놓아도 마지막으로 향하던 방향은 남겨 둔다, 제자리 구르기가 이 값을 쓴다
                if (input.MoveDirection.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
                {
                    _lastFacingDirection = input.MoveDirection.normalized;
                }
            }
        }

        /// <summary>
        /// XZ만 속도로 지정하고 Y는 건드리지 않는다. 낙하 · 경사 · 충돌은 물리에 맡긴다.
        /// 속도 x 고정 스텝이 PlayerMotionModel의 변위와 같아 클라이언트 예측과 어긋나지 않는다
        /// </summary>
        private void ApplyPlanarVelocity(Vector2 moveDirection, float moveSpeed)
        {
            Vector3 velocity = _rigidbody.linearVelocity;

            velocity.x = moveDirection.x * moveSpeed;
            velocity.z = moveDirection.y * moveSpeed;

            _rigidbody.linearVelocity = velocity;
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

        // 누르고 있는 방향이 있으면 그 방향, 없으면 0을 보낸다. 실제 방향 결정은 서버가 한다
        private static Vector2 ResolveRollDirection()
        {
            Vector2 direction = new Vector2(
                Input.GetAxisRaw(Constants.Player.AXIS_HORIZONTAL),
                Input.GetAxisRaw(Constants.Player.AXIS_VERTICAL));

            if (direction.sqrMagnitude < (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return Vector2.zero;
            }

            return direction.normalized;
        }

        /// <summary>
        /// 방향키를 누른 채면 그 방향, 제자리면 마지막으로 향하던 방향으로 구른다.
        /// 클라이언트가 보낸 값은 크기만 신뢰하지 않고 잘라 쓴다
        /// </summary>
        [Server]
        private Vector2 ResolveServerRollDirection(Vector2 requested)
        {
            if (requested.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return Vector2.ClampMagnitude(requested, 1f);
            }

            return _lastFacingDirection;
        }

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
