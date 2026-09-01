// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 패링 창 식별자. 공격자마다 WindowId를 따로 증가시키므로 ID 하나만으로는
    /// 서로 다른 공격자의 창이 충돌한다. 항상 공격자와 묶어 복합 키로 쓴다
    /// </summary>
    [Serializable]
    public struct ParryWindowKey : IEquatable<ParryWindowKey>
    {
        /****************************************
        *                Fields
        ****************************************/

        // Mirror Weaver가 기본 직렬화할 수 있도록 readonly가 아닌 public 값 필드를 쓴다
        public uint AttackerNetId;
        public uint WindowId;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 0은 두 필드 모두 "관찰한 창 없음" 예약값이다 </summary>
        public bool IsValid => (AttackerNetId != 0u) && (WindowId != 0u);

        /****************************************
        *              Constructors
        ****************************************/

        public ParryWindowKey(uint attackerNetId, uint windowId)
        {
            AttackerNetId = attackerNetId;
            WindowId      = windowId;
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(ParryWindowKey other)
        {
            return (AttackerNetId == other.AttackerNetId) && (WindowId == other.WindowId);
        }

        public override bool Equals(object obj)
        {
            return (obj is ParryWindowKey other) && (Equals(other));
        }

        public override int GetHashCode()
        {
            return (int) ((AttackerNetId * 397u) ^ WindowId);
        }

        public override string ToString()
        {
            return $"(attacker={AttackerNetId}, window={WindowId})";
        }

        public static bool operator ==(ParryWindowKey left, ParryWindowKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ParryWindowKey left, ParryWindowKey right)
        {
            return !left.Equals(right);
        }
    }
}
