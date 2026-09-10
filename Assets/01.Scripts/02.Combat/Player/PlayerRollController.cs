// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 구르기 요청 검증, 방향, 지속 시간, 무적 전이를 소유한다 </summary>
    public class PlayerRollController : NetworkBehaviour, IPlayerAction
    {
        /****************************************
        *                Fields
        ****************************************/

        private PlayerController          _movement;
        private PlayerStatus              _status;
        private ParryController           _parry;
        private PlayerAttack              _attack;
        private PlayerAnimationController _animation;
        private PlayerPresentation        _presentation;
        private LockOnController          _lockOn;

        private uint             _localRequestId = 0;
        private RequestIdTracker _requestIds;

        [SyncVar(hook = nameof(OnRollingChanged))] private bool _isRolling = false;

        // 서버 전용
        private Vector2 _rollDirection     = Vector2.zero;
        private double  _rollEndServerTime = 0.0;

        private Vector2 _pendingDirection = Vector2.zero;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정하고 관찰자에게 동기화한 구르기 진행 여부 </summary>
        public bool IsRolling => _isRolling;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _movement  = GetComponent<PlayerController>();
            _status    = GetComponent<PlayerStatus>();
            _parry     = GetComponent<ParryController>();
            _attack    = GetComponent<PlayerAttack>();
            _animation    = GetComponent<PlayerAnimationController>();
            _presentation = GetComponent<PlayerPresentation>();
            _lockOn       = GetComponent<LockOnController>();
        }

        private void Update()
        {
            if ((!isOwned) || (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!Input.GetKeyDown(Constants.Player.KEY_ROLL)))
            {
                return;
            }

            CmdRequestRoll(NextRequestId(), ResolveRequestedDirection());
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnRollingChanged(bool oldRolling, bool newRolling)
        {
            if (!newRolling)
            {
                return;
            }

            SfxPlayer.Play(SoundKey.Roll, transform.position, isLocalPlayer, netId);

            if (_animation == null)
            {
                return;
            }

            _animation.PlayRoll();
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestRoll(uint requestId, Vector2 requestedDirection)
        {
            _pendingDirection = requestedDirection;

            ActionRejection rejection = PlayerActionPipeline.TryExecute(
                ref _requestIds, requestId, this, NetworkTime.time);

            if (rejection == ActionRejection.None)
            {
                return;
            }

            TargetActionRejected(rejection);
        }

        /****************************************
        *              Player Action
        ****************************************/

        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;

            if ((!GameDB.IsInitialized) || (_movement == null) || (_status == null))
            {
                return false;
            }

            bool isOtherActionInProgress = ((_parry != null) && (_parry.IsActionInProgress(serverTime)))
                || ((_attack != null) && (_attack.IsActionInProgress(serverTime)));

            RunPhase phase    = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool     isPaused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                true,
                ((_isRolling) || (isOtherActionInProgress)),
                serverTime,
                _rollEndServerTime,
                _status.Stamina,
                GameDB.Combat.RollStaminaCost);

            return true;
        }

        public bool TrySpendResource(in PlayerActionContext context)
        {
            return _status.TrySpendStamina(context.ResourceCost);
        }

        public void CommitAction(in PlayerActionContext context)
        {
            CombatConfigSO combat = GameDB.Combat;

            _rollDirection = ResolveServerDirection(_pendingDirection);

            if (_presentation != null)
            {
                _presentation.ServerSetFacing(_rollDirection);
            }

            _isRolling         = true;
            _rollEndServerTime = context.ServerTime + combat.RollTotalDuration;

            _status.GrantTimedInvincibility(context.ServerTime + combat.RollInvincibleDuration);

            GDebug.Server($"구르기, 무적 {combat.RollInvincibleDuration:F2}초 / 전체 {combat.RollTotalDuration:F2}초");
        }

        /// <summary> 요청이 거부된 사유를 요청자에게만 돌려준다 </summary>
        [TargetRpc]
        private void TargetActionRejected(ActionRejection rejection)
        {
            GDebug.Warn($"구르기 거부: {rejection}");
            LocalCombatState.RaiseActionRejected(rejection);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> PlayerController의 단일 권위 틱 진입점에서 구르기 수명을 갱신한다 </summary>
        [Server]
        public void ServerUpdateState(double now, bool canMove)
        {
            if ((!_isRolling) || (_status == null))
            {
                return;
            }

            if ((!canMove) || (now >= _rollEndServerTime))
            {
                _isRolling = false;
            }
        }

        /// <summary> 진행 중인 구르기의 서버 이동 방향과 속도를 반환한다 </summary>
        [Server]
        public bool TryGetMotion(out Vector2 direction, out float speed)
        {
            direction = Vector2.zero;
            speed     = 0f;

            if ((!_isRolling) || (!GameDB.IsInitialized))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.Combat;
            direction = _rollDirection;
            speed     = combat.RollDistance / combat.RollTotalDuration;
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private uint NextRequestId()
        {
            ++_localRequestId;
            if (_localRequestId == 0)
            {
                ++_localRequestId;
            }

            return _localRequestId;
        }

        /// <summary> 구르기 요청 방향을 화면 기준 월드 방향으로 계산한다 </summary>
        private Vector2 ResolveRequestedDirection()
        {
            Vector2 direction = new Vector2(
                Input.GetAxisRaw(Constants.Player.AXIS_HORIZONTAL),
                Input.GetAxisRaw(Constants.Player.AXIS_VERTICAL));

            if (direction.sqrMagnitude >= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return CameraFrame.ToWorld(direction.normalized);
            }

            if (TryGetLockOnBackStep(out Vector2 backStep))
            {
                return backStep;
            }

            return Vector2.zero;
        }

        private bool TryGetLockOnBackStep(out Vector2 direction)
        {
            direction = Vector2.zero;

            if ((_lockOn == null) || (!_lockOn.IsLockedOn) || (_presentation == null))
            {
                return false;
            }

            Vector2 facing = _presentation.FacingDirection;
            if (facing.sqrMagnitude <= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return false;
            }

            direction = -facing.normalized;
            return true;
        }

        private Vector2 ResolveServerDirection(Vector2 requestedDirection)
        {
            if (requestedDirection.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return Vector2.ClampMagnitude(requestedDirection, 1f);
            }

            return _movement.LastFacingDirection;
        }
    }
}
