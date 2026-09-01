// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 소유 클라이언트 전용 로컬 예측. 서버 상태의 원본이 아니며 조작감을 위한 표현이다.
    /// 호스트(isServer && isOwned)에서는 지연이 없으므로 사용하지 않는다
    /// </summary>
    public class PlayerPrediction : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float CORRECTION_EPSILON = 0.001f; // m, 이 아래로 남은 보정은 끝난 것으로 본다

        private readonly List<PlayerMoveInput> _pendingInputs = new List<PlayerMoveInput>();

        private bool _isSuspended = false;

        // 서버 쪽으로 아직 밀어 넣지 못한 보정량. 한 번에 옮기면 튀므로 매 프레임 나눠 적용한다
        private Vector3 _correctionRemaining = Vector3.zero;

        // 입력 발신 주기(30Hz)보다 프레임이 빠르므로 마지막으로 보낸 입력을 다음 발신까지 이어서 적용한다.
        // 발신 시점에만 위치를 옮기면 화면상 30Hz 계단으로 끊겨 보인다
        private Vector2 _lastDirection      = Vector2.zero;
        private bool    _lastSprintHeld     = false;
        private float   _timeSinceLastInput = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예측을 멈추고 서버 위치를 그대로 따르는 중인지 여부 </summary>
        public bool IsSuspended => _isSuspended;

        /****************************************
        *              Methods
        ****************************************/

        /// <summary>
        /// 구르기처럼 서버가 이동을 대신 결정하거나 아예 거부하는 구간에서 예측을 멈춘다.
        /// 이 구간의 로컬 입력은 서버 위치와 어긋나기만 하므로 보관하지 않는다
        /// </summary>
        public void SetSuspended(bool suspended)
        {
            if (_isSuspended == suspended)
            {
                return;
            }

            _isSuspended = suspended;

            if (suspended)
            {
                _pendingInputs.Clear();
                _lastDirection       = Vector2.zero;
                _lastSprintHeld      = false;
                _timeSinceLastInput  = 0f;
                _correctionRemaining = Vector3.zero;
            }
        }

        /// <summary>
        /// 매 프레임 현재 입력만큼 로컬 위치를 전진시킨다. 서버에 보내는 주기와 무관하게 호출해
        /// 발신 간격 사이가 비지 않게 한다. resolveSpeed는 SprintHeld별 속도 계산 콜백이다
        /// </summary>
        public void Advance(Vector2 moveDirection, bool sprintHeld, float deltaTime, Func<bool, float> resolveSpeed)
        {
            if (_isSuspended)
            {
                return;
            }

            _lastDirection       = moveDirection;
            _lastSprintHeld      = sprintHeld;
            _timeSinceLastInput += deltaTime;

            float clampedDelta = Mathf.Clamp(deltaTime, 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
            float moveSpeed    = resolveSpeed(sprintHeld);

            transform.position = PlayerMotionModel.ComputeNextPosition(transform.position, moveDirection, moveSpeed, clampedDelta);

            ApplyCorrection(clampedDelta);
        }

        /// <summary>
        /// 서버로 보낸 입력을 서버 확인 전까지 재조정용으로 보관한다.
        /// 위치는 이미 Advance가 옮겼으므로 여기서는 만지지 않는다
        /// </summary>
        public void RecordInput(PlayerMoveInput input)
        {
            if (_isSuspended)
            {
                return;
            }

            // 여기까지의 이동은 이 입력이 대표한다. 재조정 때 이중으로 더하지 않도록 잔여 시간을 비운다
            _timeSinceLastInput = 0f;

            // 정지 입력은 위치를 바꾸지 않으므로 보관하지 않는다, 장시간 정지 시 버퍼가 자라지 않게 한다
            if (input.MoveDirection.sqrMagnitude <= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return;
            }

            // 스냅샷이 오래 끊겨도 버퍼가 무한히 자라지 않게 한다.
            // 상한을 넘길 만큼 오래된 입력은 어차피 재적용해도 서버 위치를 따라잡지 못한다
            if (_pendingInputs.Count >= Constants.Network.PLAYER_MAX_PENDING_INPUTS)
            {
                _pendingInputs.RemoveAt(0);
            }

            _pendingInputs.Add(input);
        }

        /// <summary>
        /// 서버가 확정한 위치로 맞춘다. 오차가 작으면 클라이언트 값을 그대로 보여 주고,
        /// 중간이면 서버 쪽으로 서서히 수렴시키며, 크게 벌어졌을 때만 즉시 서버 위치로 바꾼다
        /// </summary>
        public void Reconcile(Vector3 serverPosition, uint lastProcessedSequence, Func<bool, float> resolveSpeed)
        {
            DiscardAcknowledged(lastProcessedSequence);

            // 예측을 멈춘 구간에는 다시 적용할 입력도, 지켜야 할 로컬 위치도 없다.
            // 임계값을 두면 서버가 움직이는 동안 오차가 그 아래로 계속 남아 따라가지 못한다
            if (_isSuspended)
            {
                transform.position   = serverPosition;
                _correctionRemaining = Vector3.zero;
                return;
            }

            // 오차는 서버 스냅샷이 아니라 재구성 위치와 비교한다. 클라이언트는 서버가 아직 처리하지 못한
            // 입력만큼 정당하게 앞서 있으므로, 스냅샷과 직접 비교하면 움직이는 내내 임계값을 넘는다
            Vector3 rebasedPosition = ReplayPendingInputs(serverPosition, resolveSpeed);
            Vector3 correction      = rebasedPosition - transform.position;
            float   error           = correction.magnitude;

            // 1단계 - 오차가 작으면 클라이언트 값을 그대로 둔다
            if (error <= Constants.Network.PLAYER_RECONCILE_THRESHOLD)
            {
                _correctionRemaining = Vector3.zero;
                return;
            }

            // 3단계 - 텔레포트, 부활처럼 크게 벌어졌으면 서서히 따라가도 계속 어긋난다
            if (error >= Constants.Network.PLAYER_RECONCILE_SNAP)
            {
                transform.position   = rebasedPosition;
                _correctionRemaining = Vector3.zero;
                return;
            }

            // 2단계 - 그 사이는 Advance가 매 프레임 조금씩 밀어 넣는다
            _correctionRemaining = correction;
        }

        /// <summary> 서버 확정 위치에서 미확인 입력과 마지막 발신 이후의 잔여 시간을 다시 적용한다 </summary>
        private Vector3 ReplayPendingInputs(Vector3 serverPosition, Func<bool, float> resolveSpeed)
        {
            Vector3 replayedPosition = serverPosition;
            double  previousTime     = -1.0;

            for (int i = 0; i < _pendingInputs.Count; ++i)
            {
                PlayerMoveInput pending   = _pendingInputs[i];
                float           deltaTime = ComputeDeltaTime(pending.ClientEstimatedServerTime, previousTime);
                previousTime = pending.ClientEstimatedServerTime;

                float moveSpeed = resolveSpeed(pending.SprintHeld);
                replayedPosition = PlayerMotionModel.ComputeNextPosition(replayedPosition, pending.MoveDirection, moveSpeed, deltaTime);
            }

            // 마지막 발신 이후 Advance가 이미 옮겨 놓은 만큼을 다시 더한다.
            // 이걸 빼면 재조정할 때마다 최대 한 발신 주기(약 0.03초)만큼 뒤로 당겨져 고무줄이 된다
            float residual = Mathf.Clamp(_timeSinceLastInput, 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
            if (residual > 0f)
            {
                replayedPosition = PlayerMotionModel.ComputeNextPosition(
                    replayedPosition, _lastDirection, resolveSpeed(_lastSprintHeld), residual);
            }

            return replayedPosition;
        }

        // 남은 보정량을 시간 상수에 맞춰 지수적으로 밀어 넣는다.
        // 프레임 수에 비례하지 않아 60프레임과 200프레임에서 같은 속도로 수렴한다
        private void ApplyCorrection(float deltaTime)
        {
            if (_correctionRemaining == Vector3.zero)
            {
                return;
            }

            float   ratio = 1f - Mathf.Exp(-deltaTime / Constants.Network.PLAYER_RECONCILE_SMOOTH_TIME);
            Vector3 step  = _correctionRemaining * ratio;

            transform.position   += step;
            _correctionRemaining -= step;

            // 남은 양이 눈에 보이지 않을 만큼 줄면 매 프레임 미세하게 흔드느니 끝낸다
            if (_correctionRemaining.sqrMagnitude <= (CORRECTION_EPSILON * CORRECTION_EPSILON))
            {
                _correctionRemaining = Vector3.zero;
            }
        }

        private void DiscardAcknowledged(uint lastProcessedSequence)
        {
            int keepFromIndex = 0;

            for (int i = 0; i < _pendingInputs.Count; ++i)
            {
                if (_pendingInputs[i].Sequence <= lastProcessedSequence)
                {
                    keepFromIndex = i + 1;
                }
            }

            if (keepFromIndex > 0)
            {
                _pendingInputs.RemoveRange(0, keepFromIndex);
            }
        }

        private static float ComputeDeltaTime(double currentTime, double previousTime)
        {
            if (previousTime < 0.0)
            {
                return 0f;
            }

            return Mathf.Clamp((float) (currentTime - previousTime), 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
        }
    }
}
