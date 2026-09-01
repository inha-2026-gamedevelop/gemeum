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
    /// <summary> 구르기 요청 검증·방향·지속 시간·무적 전이를 소유한다 </summary>
    public class PlayerRollController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private PlayerController          _movement;
        private PlayerStatus              _status;
        private ParryController           _parry;
        private PlayerAnimationController _animation;
        private PlayerPresentation        _presentation;

        private uint             _localRequestId = 0;
        private RequestIdTracker _requestIds;

        [SyncVar(hook = nameof(OnRollingChanged))] private bool _isRolling = false;

        // 서버 전용
        private Vector2 _rollDirection               = Vector2.zero;
        private double  _rollEndServerTime           = 0.0;
        private double  _rollInvincibleEndServerTime = 0.0;

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
            _animation    = GetComponent<PlayerAnimationController>();
            _presentation = GetComponent<PlayerPresentation>();
        }

        private void Update()
        {
            if ((!isOwned) || (!Input.GetKeyDown(Constants.Player.KEY_ROLL)))
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
            if ((!newRolling) || (_animation == null))
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
            if (!_requestIds.TryAccept(requestId))
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (_movement == null) || (_status == null))
            {
                return;
            }

            double          now       = NetworkTime.time;
            RunPhase        phase     = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            PlayerLifeState lifeState = _status.LifeState;
            CombatConfigSO  combat    = GameDB.Combat;

            bool isOtherActionInProgress = ((_parry != null) && (_parry.IsActionInProgress(now)));
            PlayerActionContext context = new PlayerActionContext(
                phase,
                lifeState,
                true,
                ((_isRolling) || (isOtherActionInProgress)),
                now,
                _rollEndServerTime,
                _status.Stamina,
                combat.RollStaminaCost);

            if (!PlayerActionGate.CanStartAction(context))
            {
                return;
            }

            if (!_status.TrySpendStamina(combat.RollStaminaCost))
            {
                return;
            }

            _rollDirection = ResolveServerDirection(requestedDirection);

            // 방향을 진행 여부보다 먼저 확정한다. 같은 배치로 도착해야 관전자가 구르기 첫 프레임부터
            // 서버가 정한 방향으로 재생한다. 순서를 바꾸면 직전 방향으로 한 프레임 구르고 꺾인다
            if (_presentation != null)
            {
                _presentation.ServerSetFacing(_rollDirection);
            }

            _isRolling                   = true;
            _rollEndServerTime           = now + combat.RollTotalDuration;
            _rollInvincibleEndServerTime = now + combat.RollInvincibleDuration;

            _status.SetInvincible(true);
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

            if ((_status.IsInvincible) && (now >= _rollInvincibleEndServerTime))
            {
                _status.SetInvincible(false);
            }

            if ((!canMove) || (now >= _rollEndServerTime))
            {
                _isRolling = false;

                if (_status.IsInvincible)
                {
                    _status.SetInvincible(false); // 구르기 도중 사망 등으로 끝나면 무적도 같이 끈다
                }
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

        private static Vector2 ResolveRequestedDirection()
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
