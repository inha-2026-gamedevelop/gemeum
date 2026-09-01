// Unity
using UnityEngine;

namespace Geumeum.Combat
{
    /// <summary>
    /// 이동 입력 snapshot. 소유 클라이언트가 일정 주기로 보내고, 서버는 Sequence로 순서, 중복을 가린다
    /// </summary>
    public struct PlayerMoveInput
    {
        public Vector2 MoveDirection;
        public bool    SprintHeld;
        public double  ClientEstimatedServerTime;
        public uint    Sequence;
    }
}
