// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Dev;

namespace Geumeum.Combat
{
    /// <summary> 신궁 정밀 사격의 입력, 서버 홀드, 해제 발사를 소유한다 </summary>
    public class PrecisionShotController : NetworkBehaviour, IPlayerAction, IRunResettable
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("정밀 사격 홀드 입력 키. 활을 든 동안만 받는다")]
        [SerializeField] private KeyCode _precisionShotKey = KeyCode.Mouse1;

        [SyncVar] private PrecisionShotState _state                    = PrecisionShotState.Idle;
        [SyncVar] private uint               _activeRunIdValue         = 0u;
        [SyncVar] private uint   _activeSceneRevisionValue = 0u;
        [SyncVar] private uint               _activeHoldGeneration     = 0u;
        [SyncVar] private double             _holdStartGameTime         = 0.0;

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryController      _parry;
        private HealController       _heal;
        private PlayerAttack         _attack;

        private RequestIdTracker _requestIds;
        private uint             _nextLocalRequestId   = 1u;
        private uint             _nextLocalGeneration  = 1u;
        private uint             _lastServerGeneration = 0u;
        private uint             _nextServerActionId   = 1u;

        private bool _localHoldPredicted  = false;
        private uint _localHoldGeneration = 0u;

        private PrecisionAimRay   _pendingAim;
        private ArrowFireSnapshot _pendingShot;
        private uint               _pendingActionId     = 0u;
        private double             _recoveryEndGameTime = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public PrecisionShotState State => _state;

        public uint HoldGeneration => _activeHoldGeneration;

        public double HoldStartGameTime => _holdStartGameTime;

        public bool IsActionInProgress => (_state != PrecisionShotState.Idle);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _parry  = GetComponent<ParryController>();
            _heal   = GetComponent<HealController>();
            _attack = GetComponent<PlayerAttack>();

            if (_weapon != null)
            {
                _weapon.WeaponChanged += HandleWeaponChanged;
            }
        }

        private void OnDestroy()
        {
            if (_weapon != null)
            {
                _weapon.WeaponChanged -= HandleWeaponChanged;
            }
        }

        private void OnDisable()
        {
            if ((isServer) && (_state != PrecisionShotState.Idle))
            {
                ServerCancelHold();
            }

            _localHoldPredicted = false;
            _localHoldGeneration = 0u;
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            if (_localHoldPredicted)
            {
                HandleHeldInput();
                return;
            }

            if ((!CanStartLocalHold()) || (!Input.GetKeyDown(_precisionShotKey)))
            {
                return;
            }

            if ((!TryTakeNextId(ref _nextLocalRequestId, out uint requestId))
                || (!TryTakeNextId(ref _nextLocalGeneration, out uint generation)))
            {
                return;
            }

            _localHoldPredicted = true;
            _localHoldGeneration = generation;
            if (!TryGetLocalRunContext(out uint runIdValue, out uint sceneRevisionValue))
            {
                _localHoldPredicted = false;
                _localHoldGeneration = 0u;
                return;
            }

            GDebug.Log("[우클릭] 활: 정밀 사격 홀드 요청");
            CmdBeginPrecisionShot(runIdValue, sceneRevisionValue, requestId, generation);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if ((_status == null) || (_weapon == null) || (_attack == null))
            {
                Debug.LogError("[PrecisionShotController] 정밀 사격에 필요한 컴포넌트가 없다", this);
            }
        }

        public override void OnStopServer()
        {
            ServerCancelHold();
            _requestIds             = default;
            _lastServerGeneration   = 0u;
            _nextServerActionId     = 1u;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdBeginPrecisionShot(
            uint runIdValue,
            uint sceneRevisionValue,
            uint requestId,
            uint holdGeneration)
        {
            ActionRejection rejection = ServerBeginPrecisionShot(
                runIdValue, sceneRevisionValue, requestId, holdGeneration);
            if (connectionToClient == null)
            {
                return;
            }

            if (rejection == ActionRejection.None)
            {
                TargetPrecisionBeginAccepted(holdGeneration);
            }
            else
            {
                TargetPrecisionRejected(holdGeneration, rejection);
            }
        }

        /// <summary> 런, 씬, 홀드 세대를 검증하여 정밀 사격을 준비한다. 여기서는 발사하지 않는다 </summary>
        [Server]
        public ActionRejection ServerBeginPrecisionShot(
            uint runIdValue, uint sceneRevisionValue, uint requestId, uint holdGeneration)
        {
            if (!IsCurrentServerContext(runIdValue, sceneRevisionValue))
            {
                return ActionRejection.State;
            }

            if ((!_requestIds.TryAccept(requestId)) || (holdGeneration == 0u)
                || (holdGeneration <= _lastServerGeneration))
            {
                return ActionRejection.Duplicate;
            }

            _lastServerGeneration = holdGeneration;

            double gameTime = RunTimeLocator.Current.GameplayElapsedSeconds;
            if (!TryBuildBeginContext(gameTime, out PlayerActionContext context))
            {
                return ActionRejection.NotReady;
            }

            ActionRejection rejection = PlayerActionPipeline.Evaluate(context);
            if (rejection != ActionRejection.None)
            {
                return rejection;
            }

            _activeRunIdValue       = runIdValue;
            _activeSceneRevisionValue = sceneRevisionValue;
            _activeHoldGeneration   = holdGeneration;
            _holdStartGameTime      = gameTime;
            _state                  = PrecisionShotState.Holding;
            return ActionRejection.None;
        }

        [Command]
        private void CmdReleasePrecisionShot(
            uint runIdValue,
            uint sceneRevisionValue,
            uint requestId,
            uint holdGeneration,
            PrecisionAimRay aim)
        {
            PrecisionReleaseDecision decision = ServerReleasePrecisionShot(
                runIdValue,
                sceneRevisionValue,
                requestId,
                holdGeneration,
                aim,
                out ActionRejection rejection);

            if (connectionToClient == null)
            {
                return;
            }

            if (decision == PrecisionReleaseDecision.Fire)
            {
                TargetPrecisionReleased(holdGeneration);
                return;
            }

            if (decision == PrecisionReleaseDecision.Cancel)
            {
                TargetPrecisionCancelled(holdGeneration);
                return;
            }

            TargetPrecisionRejected(holdGeneration, rejection);
        }

        /// <summary> 홀드 경계 이전에는 취소, 이후에는 기존 발사 게이트로 처리한다 </summary>
        [Server]
        public PrecisionReleaseDecision ServerReleasePrecisionShot(
            uint runIdValue, uint sceneRevisionValue, uint requestId,
            uint holdGeneration, PrecisionAimRay aim, out ActionRejection rejection)
        {
            return ServerResolveRelease(runIdValue, sceneRevisionValue,
                requestId, holdGeneration, aim, out rejection);
        }

        [Server]
        private PrecisionReleaseDecision ServerResolveRelease(
            uint runIdValue,
            uint sceneRevisionValue,
            uint requestId,
            uint holdGeneration,
            PrecisionAimRay aim,
            out ActionRejection rejection)
        {
            rejection = ActionRejection.None;
            if ((!IsCurrentServerContext(runIdValue, sceneRevisionValue))
                || (_activeRunIdValue != runIdValue)
                || (_activeSceneRevisionValue != sceneRevisionValue))
            {
                rejection = ActionRejection.State;
                return PrecisionReleaseDecision.Reject;
            }

            double gameTime = RunTimeLocator.Current.GameplayElapsedSeconds;
            PrecisionReleaseDecision decision = PrecisionShotRules.ResolveRelease(
                _state,
                _activeHoldGeneration,
                holdGeneration,
                _holdStartGameTime,
                gameTime,
                GameDB.IsInitialized ? GameDB.Combat.PrecisionShotHoldDuration : -1f);

            if (decision == PrecisionReleaseDecision.Reject)
            {
                rejection = ActionRejection.State;
                return PrecisionReleaseDecision.Reject;
            }

            if (!_requestIds.TryAccept(requestId))
            {
                rejection = ActionRejection.Duplicate;
                return PrecisionReleaseDecision.Reject;
            }

            if (decision == PrecisionReleaseDecision.Cancel)
            {
                ServerCancelHold();
                return PrecisionReleaseDecision.Cancel;
            }

            // 홀드 경계와 서버 틱이 같은 프레임에 겹쳐도 해제 시각 자체로 Ready를 확정한다
            _state = PrecisionShotState.Ready;

            _pendingAim      = aim;
            _pendingActionId = _nextServerActionId;

            rejection = PlayerActionPipeline.TryExecuteAccepted(
                this, gameTime);
            if (rejection != ActionRejection.None)
            {
                ServerCancelHold();
                return PrecisionReleaseDecision.Reject;
            }

            return PrecisionReleaseDecision.Fire;
        }

        [Command]
        private void CmdCancelPrecisionShot(
            uint runIdValue,
            uint sceneRevisionValue,
            uint requestId,
            uint holdGeneration)
        {
            if ((ServerCancelPrecisionShot(runIdValue, sceneRevisionValue, requestId, holdGeneration))
                && (connectionToClient != null))
            {
                TargetPrecisionCancelled(holdGeneration);
            }
        }

        /// <summary> Ready 이후라도 발사 없이 홀드만 취소한다. 평타 전환도 이 경로를 쓴다 </summary>
        [Server]
        public bool ServerCancelPrecisionShot(
            uint runIdValue, uint sceneRevisionValue, uint requestId, uint holdGeneration)
        {
            if ((!IsCurrentServerContext(runIdValue, sceneRevisionValue))
                || (_activeRunIdValue != runIdValue)
                || (_activeSceneRevisionValue != sceneRevisionValue)
                || (!_requestIds.TryAccept(requestId)))
            {
                return false;
            }

            if ((_activeHoldGeneration != holdGeneration)
                || ((_state != PrecisionShotState.Holding) && (_state != PrecisionShotState.Ready)))
            {
                return false;
            }

            ServerCancelHold();
            return true;
        }

        /****************************************
        *              Player Action
        ****************************************/

        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;
            _pendingShot = default;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null)
                || (_attack == null) || (_pendingActionId == 0u)
                || (_state != PrecisionShotState.Ready))
            {
                return false;
            }

            if (!_attack.ServerTryBuildPrecisionShot(_pendingAim, _pendingActionId, out _pendingShot))
            {
                return false;
            }

            bool isOtherActionInProgress = _status.IsHitReacting || ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(NetworkTime.time)))
                || ((_heal != null) && (_heal.IsActionInProgress(NetworkTime.time)))
                || (_attack.IsBaseActionInProgress(NetworkTime.time));

            RunManager run = RunManager.ServerInstance;
            RunPhase phase = (run == null) ? RunPhase.Lobby : run.Phase;
            bool isPaused  = (run != null) && (run.IsPaused);

            context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                ((_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Bow)),
                isOtherActionInProgress,
                serverTime,
                _recoveryEndGameTime,
                _status.Stamina,
                _pendingShot.ResourceCost);
            return true;
        }

        public bool TrySpendResource(in PlayerActionContext context)
        {
            return _status.TrySpendStamina(context.ResourceCost);
        }

        public void CommitAction(in PlayerActionContext context)
        {
            _state                    = PrecisionShotState.Released;
            _activeHoldGeneration     = 0u;
            _holdStartGameTime        = 0.0;
            _recoveryEndGameTime      = context.ServerTime + _pendingShot.RecoveryDuration;

            _attack.ServerFirePrecisionShot(_pendingShot);
            AdvanceServerActionId();

            _pendingActionId = 0u;
            _pendingAim      = default;
            _pendingShot     = default;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 서버 게임 시각으로 준비와 후딜 상태를 진행한다 </summary>
        [Server]
        public void ServerUpdateState()
        {
            double gameTime = RunTimeLocator.Current.GameplayElapsedSeconds;

            if (_state == PrecisionShotState.Idle)
            {
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((!GameDB.IsInitialized) || (run == null) || (run.Phase == RunPhase.Lobby)
                || (run.Phase == RunPhase.Result) || (_status == null) || (_status.IsDead)
                || (_status.IsSwallowed)
                || (_weapon == null) || (!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Bow))
            {
                ServerCancelHold();
                return;
            }

            if (((_state == PrecisionShotState.Holding) || (_state == PrecisionShotState.Ready))
                && (!IsCurrentServerContext(_activeRunIdValue, _activeSceneRevisionValue)))
            {
                ServerCancelHold();
                return;
            }

            if (_state == PrecisionShotState.Released)
            {
                _state = PrecisionShotState.Recovery;
            }

            if (_state == PrecisionShotState.Recovery)
            {
                if (gameTime >= _recoveryEndGameTime)
                {
                    ServerCancelHold();
                }

                return;
            }

            if ((_state != PrecisionShotState.Holding) && (_state != PrecisionShotState.Ready))
            {
                return;
            }

            if (run.IsPaused)
            {
                return;
            }

            if ((_state == PrecisionShotState.Holding)
                && ((gameTime - _holdStartGameTime) >= GameDB.Combat.PrecisionShotHoldDuration))
            {
                _state = PrecisionShotState.Ready;
            }
        }

        /// <summary> 서버와 소유자 예측이 같은 SO 배율을 읽는다 </summary>
        public float ResolveMovementMultiplier(bool serverAuthority)
        {
            if ((!GameDB.IsInitialized) || (GameDB.Combat == null))
            {
                return 1f;
            }

            bool isHolding = serverAuthority
                ? ((_state == PrecisionShotState.Holding) || (_state == PrecisionShotState.Ready))
                : _localHoldPredicted;

            return (isHolding) ? GameDB.Combat.PrecisionShotMoveSpeedMultiplier : 1f;
        }

        [Server]
        public void ResetForNewRun()
        {
            ServerCancelHold();
        }

        /****************************************
        *              Target Rpc
        ****************************************/

        [TargetRpc]
        private void TargetPrecisionBeginAccepted(uint holdGeneration)
        {
            if (_localHoldGeneration != holdGeneration)
            {
                return;
            }

            _localHoldPredicted = true;
        }

        [TargetRpc]
        private void TargetPrecisionCancelled(uint holdGeneration)
        {
            ClearLocalHold(holdGeneration);
        }

        [TargetRpc]
        private void TargetPrecisionReleased(uint holdGeneration)
        {
            ClearLocalHold(holdGeneration);
        }

        [TargetRpc]
        private void TargetPrecisionRejected(uint holdGeneration, ActionRejection rejection)
        {
            ClearLocalHold(holdGeneration);
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleHeldInput()
        {
            if ((!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!CanContinueLocalHold()) || (!CameraFrame.InputEnabled))
            {
                SendLocalCancel();
                return;
            }

            if (!Input.GetKeyUp(_precisionShotKey))
            {
                return;
            }

            uint generation = _localHoldGeneration;
            _localHoldPredicted = false;
            _localHoldGeneration = 0u;

            if (!TryTakeNextId(ref _nextLocalRequestId, out uint requestId))
            {
                return;
            }

            PrecisionAimRay aim = BuildLocalAimRay();
            if (TryGetLocalRunContext(out uint runIdValue, out uint sceneRevisionValue))
            {
                CmdReleasePrecisionShot(
                    runIdValue, sceneRevisionValue, requestId, generation, aim);
            }
        }

        private void SendLocalCancel()
        {
            uint generation = _localHoldGeneration;
            _localHoldPredicted = false;
            _localHoldGeneration = 0u;

            if (!TryTakeNextId(ref _nextLocalRequestId, out uint requestId))
            {
                return;
            }

            if (TryGetLocalRunContext(out uint runIdValue, out uint sceneRevisionValue))
            {
                CmdCancelPrecisionShot(
                    runIdValue, sceneRevisionValue, requestId, generation);
            }
        }

        private bool CanStartLocalHold()
        {
            return LocalGameplayInputGate.AllowsGameplayInput &&
                (CameraFrame.InputEnabled) && CanContinueLocalHold();
        }

        private bool CanContinueLocalHold()
        {
            return (_status != null) && (!_status.IsDead) && (!_status.IsSwallowed) && (_weapon != null)
                && (_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Bow);
        }

        private static PrecisionAimRay BuildLocalAimRay()
        {
            return ArrowAimGeometry.BuildCameraRay(Camera.main);
        }

        private bool TryBuildBeginContext(double gameTime, out PlayerActionContext context)
        {
            context = default;
            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null)
                || (_attack == null))
            {
                return false;
            }

            bool isOtherActionInProgress = _status.IsHitReacting || ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(NetworkTime.time)))
                || ((_heal != null) && (_heal.IsActionInProgress(NetworkTime.time)))
                || (_attack.IsBaseActionInProgress(NetworkTime.time))
                || IsActionInProgress;

            RunManager run = RunManager.ServerInstance;
            RunPhase phase = (run == null) ? RunPhase.Lobby : run.Phase;
            bool isPaused  = (run != null) && (run.IsPaused);

            context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                ((_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Bow)),
                isOtherActionInProgress,
                gameTime,
                _recoveryEndGameTime,
                _status.Stamina,
                0f);
            return true;
        }

        [Server]
        public void ServerInterruptForHit()
        {
            uint generation = _activeHoldGeneration;
            ServerCancelHold();
            if (generation != 0u && connectionToClient != null)
            {
                TargetPrecisionCancelled(generation);
            }
        }

        [Server]
        private void ServerCancelHold()
        {
            _state                    = PrecisionShotState.Idle;
            _activeRunIdValue         = 0u;
            _activeSceneRevisionValue = 0u;
            _activeHoldGeneration     = 0u;
            _holdStartGameTime        = 0.0;
            _recoveryEndGameTime      = 0.0;
            _pendingActionId          = 0u;
            _pendingAim               = default;
            _pendingShot              = default;
        }

        private void HandleWeaponChanged(WeaponKind weapon)
        {
            if ((isServer) && (_state != PrecisionShotState.Idle))
            {
                ServerCancelHold();
            }
        }

        private void ClearLocalHold(uint holdGeneration)
        {
            if ((_localHoldGeneration != 0u) && (_localHoldGeneration != holdGeneration))
            {
                return;
            }

            _localHoldPredicted = false;
            _localHoldGeneration = 0u;
        }

        private void AdvanceServerActionId()
        {
            _nextServerActionId = (_nextServerActionId == uint.MaxValue)
                ? 0u
                : _nextServerActionId + 1u;
        }

        private static bool TryTakeNextId(ref uint nextId, out uint value)
        {
            value = nextId;
            if (value == 0u)
            {
                return false;
            }

            nextId = (nextId == uint.MaxValue) ? 0u : nextId + 1u;
            return true;
        }

        private static bool TryGetLocalRunContext(
            out uint runIdValue,
            out uint sceneRevisionValue)
        {
            RunManager run = RunManager.LocalInstance;
            RunSceneDirector director = RunSceneDirector.LocalInstance;
            runIdValue = (run == null) ? 0u : run.RunId.Value;
            sceneRevisionValue = (director == null) ? 0u : director.Revision.Value;
            return (runIdValue != 0u) && (director != null);
        }

        private static bool IsCurrentServerContext(uint runIdValue, uint sceneRevisionValue)
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return (run != null) && (director != null)
                && PrecisionShotRules.IsCurrentRequestContext(
                    runIdValue,
                    sceneRevisionValue,
                    run.RunId.Value,
                    director.Revision.Value);
        }
    }
}
