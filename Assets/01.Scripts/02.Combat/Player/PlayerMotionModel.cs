// Unity
using UnityEngine;

namespace Geumeum.Combat
{
    /// <summary> 서버와 클라 예측이 공유하는 순수 이동 계산 </summary>
    public static class PlayerMotionModel
    {
        /// <summary> 현재 위치에 이동 방향, 속도, 경과 시간을 적용한 다음 위치를 계산한다 </summary>
        public static Vector3 ComputeNextPosition(Vector3 currentPosition, Vector2 moveDirection, float moveSpeed, float deltaTime)
        {
            Vector3 delta = new Vector3(moveDirection.x, 0f, moveDirection.y) * moveSpeed * deltaTime;
            return currentPosition + delta;
        }
    }
}
