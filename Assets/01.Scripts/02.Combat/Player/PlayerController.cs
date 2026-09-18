// System
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
    /// <summary> 이동 입력을 일정 주기로 서버에 보내고, 서버가 순서, 충돌을 검증한 뒤 위치를 확정한다 </summary>
    public class PlayerController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Rigidbody                _rigidbody;
        private PlayerPrediction         _prediction;
        private PlayerPresentation       _presentation;
        private PlayerStatus             _status;
        private PlayerRollController     _roll;
        private PlayerAttack             _attack;
        private PlayerAnimationController _animation;
        private PrecisionShotController  _precisionShot;
        private LockOnController         _lockOn;
        private ReviveController         _revive;
        private HealController           _heal;
        private NetworkTransformReliable _networkTransform;

        private double _lastInputSendTime = 0.0;
        private uint   _localSequence     = 0;

        // 서버 전용
        private readonly Queue<PlayerMoveInput> _serverInputBuffer = new Queue<PlayerMoveInput>();

        private uint    _lastAcceptedSequence        = 0;
        private uint    _lastProcessedSequence       = 0;
        private double  _lastInputReceivedServerTime = -1.0;
        private Vector2 _lastMoveDirection           = Vector2.zero;
        private Vector2 _lastFacingDirection         = Vector2.up; // 마지막 이동 방향
        private bool    _lastSprintHeld              = false;
        private bool    _wasMovingLastTick           = false;
        private float   _snapshotElapsed             = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 마지막으로 확인한 이동 방향 </summary>
        public Vector2 LastFacingDirection => _lastFacingDirection;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rigidbody        = GetComponent<Rigidbody>();
            _prediction       = GetComponent<PlayerPrediction>();
            _presentation     = GetComponent<PlayerPresentation>();
            _status           = GetComponent<PlayerStatus>();
            _roll             = GetComponent<PlayerRollController>();
            _attack           = GetComponent<PlayerAttack>();
            _animation        = GetComponent<PlayerAnimationController>();
            _precisionShot    = GetComponent<PrecisionShotController>();
            _lockOn           = GetComponent<LockOnController>();
            _revive           = GetComponent<ReviveController>();
            _heal             = GetComponent<HealController>();
            _networkTransform = GetComponent<NetworkTransformReliable>();
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            bool isPaused = ((RunManager.LocalInstance != null) && (RunManager.LocalInstance.IsPaused));
            bool inputBlocked = !LocalGameplayInputGate.AllowsGameplayInput;

            bool isAttackLocked = (((_animation != null) && (_animation.IsAttackCueActive))
                || ((_attack != null) && (_attack.IsClientMoveLockPending)));

            bool isServerDrivenMotion = (((_roll != null) && (_roll.IsRolling))
                || ((_status != null) && (_status.IsDead || _status.IsSwallowed))
                || (isPaused) || (inputBlocked) || (isAttackLocked));

            _prediction.SetSuspended(isServerDrivenMotion);

            if ((_presentation != null) && (isServerDrivenMotion))
            {
                _presentation.ClientReleaseLocalState();
            }

            Vector2 moveAxis = inputBlocked
                ? Vector2.zero
                : new Vector2(
                    Input.GetAxisRaw(Constants.Player.AXIS_HORIZONTAL),
                    Input.GetAxisRaw(Constants.Player.AXIS_VERTICAL));

            Vector2 moveDirection = CameraFrame.ToWorld(Vector2.ClampMagnitude(moveAxis, 1f));
            bool    sprintHeld    = (!inputBlocked) && Input.GetKey(Constants.Player.KEY_SPRINT);
            float   predictedSpeed = ResolveMoveSpeed(sprintHeld);

            if (!isServer)
            {
                _prediction.Advance(moveDirection, sprintHeld, Time.deltaTime, predictedSpeed);

                if ((_presentation != null) && (!isServerDrivenMotion))
                {
                    _presentation.ClientPredictMotion(moveDirection, predictedSpeed);
                }
            }

            double now = NetworkTime.time;
            if ((now - _lastInputSendTime) < Constants.Network.PLAYER_INPUT_SEND_INTERVAL)
            {
                return;
            }

            _lastInputSendTime = now;

            PlayerMoveInput input = new PlayerMoveInput
            {
                MoveDirection             = moveDirection,
                SprintHeld                = sprintHeld,
                ClientEstimatedServerTime = now,
                Sequence                  = ++_localSequence,
            };

            if (!isServer)
            {
                _prediction.RecordInput(input, predictedSpeed);
            }

            CmdSendMoveInput(input);
        }

        /****************************************
        *              Authority Callbacks
        ****************************************/

        public override void OnStartAuthority()
        {
            if ((_networkTransform != null) && (!isServer))
            {
                _networkTransform.enabled = false;
            }
        }

        /****************************************
        *              Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            if ((!isServer) && (_rigidbody != null))
            {
                _rigidbody.isKinematic   = true;
                _rigidbody.interpolation = RigidbodyInterpolation.None;
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

            if (_roll == null)
            {
                Debug.LogError("[PlayerController] PlayerRollController가 없어 구르기를 처리할 수 없다", this);
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
        *              Server API
        ****************************************/

        /// <summary> 서버 위치를 즉시 옮기고 소유 클라이언트에 스냅샷을 보낸다 </summary>
        [Server]
        public void ServerTeleport(Vector3 position)
        {
            if (_rigidbody == null)
            {
                transform.position = position;
                return;
            }

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.position       = position;
            transform.position        = position;

            TargetMoveSnapshot(position, _lastProcessedSequence);
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command(channel = Channels.Unreliable)]
        private void CmdSendMoveInput(PlayerMoveInput input)
        {
            if (input.Sequence <= _lastAcceptedSequence)
            {
                return;
            }

            if ((!IsFinite(input.MoveDirection.x)) || (!IsFinite(input.MoveDirection.y)))
            {
                return;
            }

            input.MoveDirection = Vector2.ClampMagnitude(input.MoveDirection, 1f);

            while (_serverInputBuffer.Count >= Constants.Network.PLAYER_SERVER_INPUT_BUFFER_MAX)
            {
                _serverInputBuffer.Dequeue();
            }

            _lastAcceptedSequence        = input.Sequence;
            _lastInputReceivedServerTime = NetworkTime.time;

            _serverInputBuffer.Enqueue(input);
        }

        /****************************************
        *              Server Tick
        ****************************************/

        private void ServerProcessMovementTick(float fixedDeltaTime)
        {
            DrainInputBuffer();

            RunPhase        phase     = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool            isPaused  = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));
            PlayerLifeState lifeState = (_status != null) ? _status.LifeState : PlayerLifeState.Alive;
            bool            canAct    = PlayerActionGate.CanAct(phase, lifeState, isPaused);
            double          now       = NetworkTime.time;
            bool            canMove   = canAct;

            if (_roll != null)
            {
                _roll.ServerUpdateState(now, canMove);
            }

            if (_attack != null)
            {
                _attack.ServerUpdateState(now);
            }

            if (_precisionShot != null)
            {
                _precisionShot.ServerUpdateState();
            }

            if (_lockOn != null)
            {
                _lockOn.ServerUpdateState();
            }

            if (_revive != null)
            {
                _revive.ServerUpdateState(now);
            }

            if (_heal != null)
            {
                _heal.ServerUpdateState(now);
            }

            // 행동 갱신이 끝난 뒤에 판단한다. 이번 틱에 후딜이 풀렸으면 곧바로 다시 움직일 수 있다
            bool isAttackInProgress = ((_attack != null) && (_attack.IsBaseActionInProgress(now)));
            canMove = PlayerActionGate.CanMove(canAct, isAttackInProgress);

            Vector2 direction = Vector2.zero;
            float   speed     = 0f;

            if ((canMove) && (_roll != null) && (_roll.TryGetMotion(out Vector2 rollDirection, out float rollSpeed)))
            {
                direction = rollDirection;
                speed     = rollSpeed;
            }
            else if (canMove)
            {
                direction = _lastMoveDirection;
                speed     = ResolveMoveSpeed(_lastSprintHeld);
            }

            ApplyPlanarVelocity(direction, speed);

            if (_presentation != null)
            {
                _presentation.ServerSetMotion(direction, speed);
            }

            UpdateOwnerSnapshot(direction, speed, fixedDeltaTime);
        }

        private void DrainInputBuffer()
        {
            bool isInputExpired = ((_lastInputReceivedServerTime >= 0.0)
                && ((NetworkTime.time - _lastInputReceivedServerTime) > Constants.Network.PLAYER_INPUT_EXPIRY));

            if (isInputExpired)
            {
                _lastMoveDirection = Vector2.zero;
                _lastSprintHeld    = false;
            }

            while (_serverInputBuffer.Count > 0)
            {
                PlayerMoveInput input = _serverInputBuffer.Dequeue();

                _lastProcessedSequence = input.Sequence;
                _lastMoveDirection     = input.MoveDirection;
                _lastSprintHeld        = input.SprintHeld;

                if (input.MoveDirection.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
                {
                    _lastFacingDirection = input.MoveDirection.normalized;
                }
            }
        }

        /// <summary> XZ만 속도로 지정하고 Y는 건드리지 않는다 </summary>
        private void ApplyPlanarVelocity(Vector2 moveDirection, float moveSpeed)
        {
            Vector3 velocity = _rigidbody.linearVelocity;

            velocity.x = moveDirection.x * moveSpeed;
            velocity.z = moveDirection.y * moveSpeed;

            _rigidbody.linearVelocity = velocity;
        }

        private void UpdateOwnerSnapshot(Vector2 direction, float speed, float fixedDeltaTime)
        {
            bool isMoving = ((direction.sqrMagnitude > 0f) && (speed > 0f));

            if (isMoving != _wasMovingLastTick)
            {
                TargetMoveSnapshot(_rigidbody.position, _lastProcessedSequence);
                _snapshotElapsed   = 0f;
                _wasMovingLastTick = isMoving;
                return;
            }

            float sendInterval = isMoving
                ? Constants.Network.PLAYER_SYNC_INTERVAL
                : Constants.Network.PLAYER_SNAPSHOT_IDLE_INTERVAL;

            _snapshotElapsed += fixedDeltaTime;

            if (_snapshotElapsed >= sendInterval)
            {
                TargetMoveSnapshot(_rigidbody.position, _lastProcessedSequence);
                _snapshotElapsed -= sendInterval;
            }

            _wasMovingLastTick = isMoving;
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetMoveSnapshot(Vector3 serverPosition, uint lastProcessedSequence)
        {
            if (isServer)
            {
                return;
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

            float baseSpeed = (sprintHeld) ? GameDB.Combat.RunSpeed : GameDB.Combat.WalkSpeed;
            float multiplier = (_precisionShot == null)
                ? 1f
                : _precisionShot.ResolveMovementMultiplier(isServer);
            return baseSpeed * multiplier;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
