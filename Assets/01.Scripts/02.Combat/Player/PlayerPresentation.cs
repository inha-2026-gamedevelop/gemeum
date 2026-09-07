// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 화면 표현이 쓸 이동 방향과 속도를 명시적으로 보관한다 </summary>
    public class PlayerPresentation : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버가 확정한 표현 상태
        [SyncVar] private Vector2 _serverFacing   = Vector2.up;
        [SyncVar] private Vector2 _serverVelocity = Vector2.zero;

        // 소유 클라이언트가 즉시 반영하는 값
        private Vector2 _localFacing   = Vector2.up;
        private Vector2 _localVelocity = Vector2.zero;
        private bool    _hasLocalState = false;

        // 락온 대상
        private Transform _lockOnTarget = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 실제로 움직이는 평면 방향(단위 벡터). 정지 중에는 0이다 </summary>
        public Vector2 MoveDirection
        {
            get
            {
                Vector2 velocity = Velocity;
                return (velocity.sqrMagnitude > 0f) ? velocity.normalized : Vector2.zero;
            }
        }

        /// <summary> 모델이 바라볼 평면 방향. x가 월드 X, y가 월드 Z다 </summary>
        public Vector2 FacingDirection
        {
            get
            {
                if (TryGetLockOnFacing(out Vector2 lockOnFacing))
                {
                    return lockOnFacing;
                }

                return UseLocalState ? _localFacing : _serverFacing;
            }
        }

        /// <summary> 공격 판정이 향하는 평면 방향 </summary>
        public Vector2 AimDirection => FacingDirection;

        /// <summary> 락온이 걸려 있는지 </summary>
        public bool IsLockedOn => (_lockOnTarget != null);

        /// <summary> 표현에 쓸 평면 이동 속도(m/s) </summary>
        public float MoveSpeed => Velocity.magnitude;

        private Vector2 Velocity => UseLocalState ? _localVelocity : _serverVelocity;

        private bool UseLocalState => (isOwned) && (!isServer) && (_hasLocalState);

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 락온 대상을 건네받는다. null이면 해제다 </summary>
        public void SetLockOnTarget(Transform target)
        {
            _lockOnTarget = target;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 서버가 이번 틱에 실제로 적용한 이동을 표현 상태로 확정한다 </summary>
        [Server]
        public void ServerSetMotion(Vector2 direction, float speed)
        {
            _serverVelocity = ResolveVelocity(direction, speed);
            _serverFacing   = ResolveFacing(direction, _serverFacing);
        }

        /// <summary> 구르기처럼 이동을 서버가 대신 정하는 행동의 방향을 먼저 확정한다 </summary>
        [Server]
        public void ServerSetFacing(Vector2 direction)
        {
            _serverFacing = ResolveFacing(direction, _serverFacing);
        }

        /****************************************
        *              Client API
        ****************************************/

        /// <summary> 소유 클라이언트가 자기 입력을 즉시 표현에 반영한다 </summary>
        public void ClientPredictMotion(Vector2 direction, float speed)
        {
            if ((!isOwned) || (isServer))
            {
                return;
            }

            _localVelocity = ResolveVelocity(direction, speed);
            _localFacing   = ResolveFacing(direction, _localFacing);
            _hasLocalState = true;
        }

        /// <summary> 이동을 서버가 대신 정하는 구간에서는 로컬 예측을 버리고 서버 값을 따른다 </summary>
        public void ClientReleaseLocalState()
        {
            _hasLocalState = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 락온 중 바라볼 방향. 대상 위치에서 그때그때 만든다 </summary>
        private bool TryGetLockOnFacing(out Vector2 facing)
        {
            facing = Vector2.zero;

            if (_lockOnTarget == null)
            {
                return false;
            }

            Vector3 delta = _lockOnTarget.position - transform.position;
            Vector2 planar = new Vector2(delta.x, delta.z);

            if (planar.sqrMagnitude <= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return false;
            }

            facing = planar.normalized;
            return true;
        }

        /// <summary> 방향과 속력을 속도 하나로 합친다 </summary>
        private static Vector2 ResolveVelocity(Vector2 direction, float speed)
        {
            bool isMoving = (direction.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
                && (speed > 0f);

            return (isMoving) ? (direction * speed) : Vector2.zero;
        }

        private static Vector2 ResolveFacing(Vector2 direction, Vector2 previousFacing)
        {
            if (direction.sqrMagnitude <= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return previousFacing;
            }

            return direction.normalized;
        }
    }
}
