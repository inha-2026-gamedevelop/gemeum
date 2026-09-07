// Unity
using UnityEngine;

namespace Geumeum.Combat
{
    /// <summary> 이동 입력 snapshot </summary>
    public struct PlayerMoveInput
    {
        public Vector2 MoveDirection;
        public bool    SprintHeld;
        public double  ClientEstimatedServerTime;
        public uint    Sequence;
    }
}
