// Unity
using UnityEngine;
using UnityEngine.InputSystem;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> 이동 입력을 서버로 보내고, 서버가 검증한 뒤 위치를 갱신한다. 원격 상태는 NetworkTransform으로 보간 </summary>
    public class PlayerController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private InputActionAsset _inputActions;

        private InputAction _moveAction;
        private float       _moveSpeed          = 0f;
        private double      _lastMoveServerTime = -1.0; // 서버 전용, 이 커넥션의 직전 CmdMove 처리 시각

        /****************************************
        *              Unity Event
        ****************************************/

        public override void OnStartServer()
        {
            // TODO(민성): 달리기 입력이 붙으면 WalkSpeed와 전환하도록 바꾼다
            _moveSpeed = GameDB.Combat.RunSpeed;
        }

        public override void OnStartAuthority()
        {
            if (_inputActions == null)
            {
                Debug.LogError("[PlayerController] InputActionAsset이 비어 있어 이동 입력을 받을 수 없다", this);
                return;
            }

            _moveAction = _inputActions.FindActionMap(Constants.Player.ACTION_MAP).FindAction(Constants.Player.ACTION_MOVE);
            _moveAction.Enable();
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

            Vector2 moveDirection = _moveAction.ReadValue<Vector2>();
            CmdMove(moveDirection);
        }

        /****************************************
        *              Commands
        ****************************************/

        // TODO(민성): 생명 상태와 런 단계가 생기면 여기서 먼저 검증한다
        [Command]
        private void CmdMove(Vector2 moveDirection)
        {
            Vector2 clampedDirection = Vector2.ClampMagnitude(moveDirection, 1f); // 클라 입력값을 신뢰하지 않고 서버가 크기를 검증

            // Time.deltaTime을 쓰면 한 프레임에 몰려 들어온 Command마다 중복 적용돼 속도가 배로 뛴다
            double now       = NetworkTime.time;
            float  deltaTime = (_lastMoveServerTime < 0.0)
                ? 0f
                : Mathf.Clamp((float) (now - _lastMoveServerTime), 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
            _lastMoveServerTime = now;

            transform.position = PlayerMotionModel.ComputeNextPosition(transform.position, clampedDirection, _moveSpeed, deltaTime);
        }
    }
}
