// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 화면 표현이 쓸 이동 방향과 속도를 명시적으로 보관한다.
    /// Transform 위치 차분으로 역산하면 네트워크 보간과 재조정 보정까지 이동으로 오인하므로,
    /// 역할별로 신뢰할 수 있는 소스에서 받는다. 판정에는 관여하지 않는다
    /// </summary>
    public class PlayerPresentation : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버가 확정한 표현 상태. 원격 관전자는 이 값만 본다.
        // SyncVar는 값이 바뀔 때만 전송하므로 정지, 직진 구간에서는 대역폭을 쓰지 않는다.
        // 속력이 아니라 속도(방향 + 크기)인 이유는 락온 스트레이프 때문이다 -
        // "옆으로 걸으며 앞을 본다"라 바라보는 방향만으로는 어느 클립을 틀지 정해지지 않는다.
        // 필드 수는 그대로고 syncInterval 0.1초 기준 3인이 늘리는 대역폭은 초당 120바이트다
        [SyncVar] private Vector2 _serverFacing   = Vector2.up;
        [SyncVar] private Vector2 _serverVelocity = Vector2.zero;

        // 소유 클라이언트가 서버 확인을 기다리지 않고 즉시 반영하는 값
        private Vector2 _localFacing   = Vector2.up;
        private Vector2 _localVelocity = Vector2.zero;
        private bool    _hasLocalState = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>
        /// 실제로 움직이는 평면 방향(단위 벡터). 정지 중에는 0이다.
        /// 락온 중에는 <see cref="FacingDirection"/>과 달라진다 - 그것이 스트레이프다
        /// </summary>
        public Vector2 MoveDirection
        {
            get
            {
                Vector2 velocity = Velocity;
                return (velocity.sqrMagnitude > 0f) ? velocity.normalized : Vector2.zero;
            }
        }

        /// <summary>
        /// 모델이 바라볼 평면 방향. x가 월드 X, y가 월드 Z다.
        /// 락온이 붙으면 여기가 <b>대상 방향</b>이 되고, 그 전까지는 이동 방향과 같다
        /// </summary>
        public Vector2 FacingDirection => UseLocalState ? _localFacing : _serverFacing;

        /// <summary>
        /// 공격 판정이 향하는 평면 방향. 바라보는 방향을 따른다.
        /// <b>지금은 <see cref="FacingDirection"/>과 늘 같지만 이름을 가른다</b> -
        /// 이름이 하나면 락온을 붙이는 사람이 셋 중 무엇을 고쳐야 하는지 알 수 없다
        /// </summary>
        public Vector2 AimDirection => FacingDirection;

        /// <summary> 표현에 쓸 평면 이동 속도(m/s) </summary>
        public float MoveSpeed => Velocity.magnitude;

        private Vector2 Velocity => UseLocalState ? _localVelocity : _serverVelocity;

        // 호스트는 스스로가 서버라 서버 값이 곧 자기 값이다
        private bool UseLocalState => (isOwned) && (!isServer) && (_hasLocalState);

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 서버가 이번 틱에 실제로 적용한 이동을 표현 상태로 확정한다.
        /// PlayerController의 권위 틱에서만 호출한다
        /// </summary>
        [Server]
        public void ServerSetMotion(Vector2 direction, float speed)
        {
            _serverVelocity = ResolveVelocity(direction, speed);
            _serverFacing   = ResolveFacing(direction, _serverFacing);
        }

        /// <summary>
        /// 구르기처럼 이동을 서버가 대신 정하는 행동의 방향을 먼저 확정한다.
        /// 진행 여부 SyncVar보다 먼저 호출해야 관전자가 같은 배치에서 방향을 함께 받는다
        /// </summary>
        [Server]
        public void ServerSetFacing(Vector2 direction)
        {
            _serverFacing = ResolveFacing(direction, _serverFacing);
        }

        /****************************************
        *              Client API
        ****************************************/

        /// <summary>
        /// 소유 클라이언트가 자기 입력을 즉시 표현에 반영한다.
        /// 서버 왕복을 기다리면 방향 전환이 지연으로 느껴진다
        /// </summary>
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

        /// <summary>
        /// 이동을 서버가 대신 정하는 구간에서는 로컬 예측을 버리고 서버 값을 따른다.
        /// 구르기 방향을 클라이언트가 계속 덮으면 관전자와 다른 방향으로 구른다
        /// </summary>
        public void ClientReleaseLocalState()
        {
            _hasLocalState = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 방향과 속력을 속도 하나로 합친다. 곱하는 순서가 PlayerController의 ApplyPlanarVelocity와
        /// 같아야 표현 속도와 실제 물리 속도가 어긋나지 않는다 - 그쪽도 방향을 정규화하지 않고 그대로 곱한다
        /// </summary>
        private static Vector2 ResolveVelocity(Vector2 direction, float speed)
        {
            bool isMoving = (direction.sqrMagnitude > (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
                && (speed > 0f);

            return (isMoving) ? (direction * speed) : Vector2.zero;
        }

        // 정지 중에는 마지막 유효 방향을 유지한다. 0으로 되돌리면 멈출 때마다 모델이 기준 방향으로 홱 돈다
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
