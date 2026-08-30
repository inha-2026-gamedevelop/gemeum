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

        /****************************************
        *              Methods
        ****************************************/

        /// <summary>
        /// 입력을 즉시 로컬에 반영하고, 서버 확인 전까지 재조정용으로 보관한다.
        /// resolveSpeed는 SprintHeld 여부로 그때그때 다른 속도를 계산하기 위한 콜백이다
        /// </summary>
        public void Predict(PlayerMoveInput input, Func<bool, float> resolveSpeed)
        {
            float deltaTime = ComputeDeltaTime(input.ClientEstimatedServerTime, _lastInputTime);
            _lastInputTime  = input.ClientEstimatedServerTime;

            float moveSpeed = resolveSpeed(input.SprintHeld);
            transform.position = PlayerMotionModel.ComputeNextPosition(transform.position, input.MoveDirection, moveSpeed, deltaTime);
            _pendingInputs.Add(input);
        }

        /// <summary>
        /// 서버가 확정한 위치로 맞춘다. 오차가 임계값 이하면 그대로 두어 매 틱 떨림을 막고,
        /// 넘으면 서버 위치에서 미확인 입력만 다시 적용해 예측 위치를 재구성한다
        /// </summary>
        public void Reconcile(Vector3 serverPosition, uint lastProcessedSequence, Func<bool, float> resolveSpeed)
        {
            DiscardAcknowledged(lastProcessedSequence);

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
