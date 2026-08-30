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

        private readonly List<PlayerMoveInput> _pendingInputs = new List<PlayerMoveInput>();

        private double _lastInputTime = -1.0;
        private bool   _isSuspended   = false;

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
                _lastInputTime = -1.0;
            }
        }

        /// <summary>
        /// 입력을 즉시 로컬에 반영하고, 서버 확인 전까지 재조정용으로 보관한다.
        /// resolveSpeed는 SprintHeld 여부로 그때그때 다른 속도를 계산하기 위한 콜백이다
        /// </summary>
        public void Predict(PlayerMoveInput input, Func<bool, float> resolveSpeed)
        {
            if (_isSuspended)
            {
                return;
            }

            float deltaTime = ComputeDeltaTime(input.ClientEstimatedServerTime, _lastInputTime);
            _lastInputTime  = input.ClientEstimatedServerTime;

            float moveSpeed = resolveSpeed(input.SprintHeld);
            transform.position = PlayerMotionModel.ComputeNextPosition(transform.position, input.MoveDirection, moveSpeed, deltaTime);

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
        /// 서버가 확정한 위치로 맞춘다. 오차가 임계값 이하면 그대로 두어 매 틱 떨림을 막고,
        /// 넘으면 서버 위치에서 미확인 입력만 다시 적용해 예측 위치를 재구성한다
        /// </summary>
        public void Reconcile(Vector3 serverPosition, uint lastProcessedSequence, Func<bool, float> resolveSpeed)
        {
            DiscardAcknowledged(lastProcessedSequence);

            // 예측을 멈춘 구간에는 다시 적용할 입력도, 지켜야 할 로컬 위치도 없다.
            // 임계값을 두면 서버가 움직이는 동안 오차가 그 아래로 계속 남아 따라가지 못한다
            if (_isSuspended)
            {
                transform.position = serverPosition;
                return;
            }

            float error = Vector3.Distance(transform.position, serverPosition);
            if (error <= Constants.Network.PLAYER_RECONCILE_THRESHOLD)
            {
                return;
            }

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

            transform.position = replayedPosition;
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
