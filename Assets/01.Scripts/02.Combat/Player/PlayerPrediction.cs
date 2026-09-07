// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 소유 클라이언트 전용 로컬 예측 </summary>
    public class PlayerPrediction : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float CORRECTION_EPSILON = 0.001f; // m

        private readonly List<PlayerMoveInput> _pendingInputs = new List<PlayerMoveInput>();

        private bool _isSuspended = false;

        private Vector3 _correctionRemaining = Vector3.zero;

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

        /// <summary> 구르기처럼 서버가 이동을 대신 결정하거나 거부하는 구간에서 예측을 멈춘다 </summary>
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

        /// <summary> 매 프레임 현재 입력만큼 로컬 위치를 전진시킨다 </summary>
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

        /// <summary> 서버로 보낸 입력을 서버 확인 전까지 재조정용으로 보관한다 </summary>
        public void RecordInput(PlayerMoveInput input)
        {
            if (_isSuspended)
            {
                return;
            }

            _timeSinceLastInput = 0f;

            if (input.MoveDirection.sqrMagnitude <= (Constants.Player.MOVE_INPUT_EPSILON * Constants.Player.MOVE_INPUT_EPSILON))
            {
                return;
            }

            if (_pendingInputs.Count >= Constants.Network.PLAYER_MAX_PENDING_INPUTS)
            {
                _pendingInputs.RemoveAt(0);
            }

            _pendingInputs.Add(input);
        }

        /// <summary> 서버가 확정한 위치로 맞춘다 </summary>
        public void Reconcile(Vector3 serverPosition, uint lastProcessedSequence, Func<bool, float> resolveSpeed)
        {
            DiscardAcknowledged(lastProcessedSequence);

            if (_isSuspended)
            {
                transform.position   = serverPosition;
                _correctionRemaining = Vector3.zero;
                return;
            }

            Vector3 rebasedPosition = ReplayPendingInputs(serverPosition, resolveSpeed);
            Vector3 correction      = rebasedPosition - transform.position;
            float   error           = correction.magnitude;

            if (error <= Constants.Network.PLAYER_RECONCILE_THRESHOLD)
            {
                _correctionRemaining = Vector3.zero;
                return;
            }

            if (error >= Constants.Network.PLAYER_RECONCILE_SNAP)
            {
                transform.position   = rebasedPosition;
                _correctionRemaining = Vector3.zero;
                return;
            }

            _correctionRemaining = correction;
        }

        /// <summary> 서버 확정 위치에서 미확인 입력과 잔여 시간을 다시 적용한다 </summary>
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

            float residual = Mathf.Clamp(_timeSinceLastInput, 0f, Constants.Network.MAX_INPUT_DELTA_TIME);
            if (residual > 0f)
            {
                replayedPosition = PlayerMotionModel.ComputeNextPosition(
                    replayedPosition, _lastDirection, resolveSpeed(_lastSprintHeld), residual);
            }

            return replayedPosition;
        }

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
