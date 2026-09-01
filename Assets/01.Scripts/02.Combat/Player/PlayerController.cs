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

        private Rigidbody                _rigidbody;
        private PlayerPrediction         _prediction;
        private PlayerPresentation       _presentation;
        private PlayerStatus             _status;
        private PlayerRollController     _roll;
        private NetworkTransformReliable _networkTransform;

        private double _lastInputSendTime = 0.0;
        private uint   _localSequence     = 0;

        // 서버 전용
        private readonly Queue<PlayerMoveInput> _serverInputBuffer = new Queue<PlayerMoveInput>();

        private uint    _lastAcceptedSequence        = 0;
        private uint    _lastProcessedSequence       = 0;
        private double  _lastInputReceivedServerTime = -1.0;
        private Vector2 _lastMoveDirection           = Vector2.zero;
        private Vector2 _lastFacingDirection         = Vector2.up; // 제자리 구르기가 쓰는 마지막 이동 방향
        private bool    _lastSprintHeld              = false;
        private bool    _wasMovingLastTick           = false;
        private float   _snapshotElapsed             = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 마지막으로 확인한 이동 방향, 제자리 구르기 방향의 원본 </summary>
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
            _networkTransform = GetComponent<NetworkTransformReliable>();
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            // 구르기는 서버가 방향·속도를 대신 정하고, 쓰러진 뒤에는 서버가 이동을 아예 거부한다.
            // 두 구간에서 로컬 예측을 계속하면 서버 위치와 벌어지기만 해 재조정이 고무줄이 된다.
            // 서버 phase는 소유 클라이언트가 알 수 없으므로 클라이언트가 아는 상태만 본다
            bool isServerDrivenMotion = (((_roll != null) && (_roll.IsRolling))
                || ((_status != null) && (_status.IsDead)));

            _prediction.SetSuspended(isServerDrivenMotion);

            // 구르기처럼 서버가 방향을 정하는 구간에는 로컬 표현도 서버 값을 따른다.
            // 안 그러면 구르는 방향이 관전자와 자기 화면에서 달라진다
            if ((_presentation != null) && (isServerDrivenMotion))
            {
                _presentation.ClientReleaseLocalState();
            }

            Vector2 moveAxis = new Vector2(
                Input.GetAxisRaw(Constants.Player.AXIS_HORIZONTAL),
                Input.GetAxisRaw(Constants.Player.AXIS_VERTICAL));

            Vector2 moveDirection = Vector2.ClampMagnitude(moveAxis, 1f);
            bool    sprintHeld    = Input.GetKey(Constants.Player.KEY_SPRINT);

            // 예측은 매 프레임, 발신은 30Hz다. 발신 주기에만 위치를 옮기면 프레임이 아무리 빨라도
            // 화면상 30Hz 계단으로 끊긴다. 호스트는 스스로가 서버라 지연이 없어 예측이 필요 없다
            if (!isServer)
            {
                _prediction.Advance(moveDirection, sprintHeld, Time.deltaTime, ResolveMoveSpeed);

                // 방향·속도 표현은 서버 왕복을 기다리지 않는다. 기다리면 방향 전환이 지연으로 느껴진다
                if ((_presentation != null) && (!isServerDrivenMotion))
                {
                    _presentation.ClientPredictMotion(moveDirection, ResolveMoveSpeed(sprintHeld));
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
                _prediction.RecordInput(input);
            }

            CmdSendMoveInput(input);
        }

        /****************************************
        *              Authority Callbacks
        ****************************************/

        public override void OnStartAuthority()
        {
            // NetworkTransformReliable은 ServerToClient여도 소유 클라이언트 transform을 덮어쓴다.
            // 로컬 예측과 같은 transform을 두고 싸우지 않도록 원격 소유자에서만 끈다
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
            // 원격 인스턴스의 물리가 서버 스냅샷·로컬 예측과 위치를 두고 싸우지 않게 한다.
            // 보간 책임도 하나만 남긴다 — 원격은 NetworkTransform, 자기 캐릭터는 PlayerPrediction이다.
            // Rigidbody 보간까지 남으면 같은 프레임에 두 계층이 Transform을 밀어 떨림이 된다
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
            double          now       = NetworkTime.time;

            if (_roll != null)
            {
                _roll.ServerUpdateState(now, canMove);
            }

            Vector2 direction = Vector2.zero;
            float   speed     = 0f;

            if ((canMove) && (_roll != null) && (_roll.TryGetMotion(out Vector2 rollDirection, out float rollSpeed)))
            {
                direction = rollDirection; // 구르기 중에는 일반 이동 입력을 무시한다
                speed     = rollSpeed;
            }
            else if (canMove)
            {
                direction = _lastMoveDirection;
                speed     = ResolveMoveSpeed(_lastSprintHeld);
            }

            ApplyPlanarVelocity(direction, speed);

            // 서버가 실제로 수락한 이동을 표현 상태로 확정한다. 관전자는 이 값으로 방향과 속도를 그린다
            if (_presentation != null)
            {
                _presentation.ServerSetMotion(direction, speed);
            }

            UpdateOwnerSnapshot(direction, speed, fixedDeltaTime);
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

                if (input.MoveDirection.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
                {
                    _lastFacingDirection = input.MoveDirection.normalized;
                }
            }
        }

        /// <summary>
        /// XZ만 속도로 지정하고 Y는 건드리지 않는다. 낙하·경사·충돌은 물리에 맡긴다.
        /// 속도 x 고정 스텝이 PlayerMotionModel의 변위와 같아 클라이언트 예측과 어긋나지 않는다
        /// </summary>
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
                TargetMoveSnapshot(_rigidbody.position, _lastProcessedSequence); // 이동·정지 전이를 즉시 확정한다
                _snapshotElapsed   = 0f;
                _wasMovingLastTick = isMoving;
                return;
            }

            // 정지 구간에도 성기게나마 계속 보낸다. 여기서 끊으면 소유 클라이언트가 마지막 처리 sequence를
            // 받지 못해 미확인 입력을 비우지도, 어긋난 예측 위치를 되돌리지도 못한다
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
