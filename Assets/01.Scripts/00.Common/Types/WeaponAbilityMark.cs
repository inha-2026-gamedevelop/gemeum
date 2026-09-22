using System;
using UnityEngine;

namespace Geumeum.Common
{
    public enum WeaponAbilityEvent : byte
    {
        ArrowContact = 0,
        WedgePlaced = 1,
        WedgeCracked = 2,
        WedgeBurst = 3,
        EchoCharged = 4,
        EchoRecall = 5,
        EchoBurst = 6,
        BowReleased = 7,
        BellContact = 8,
        SwordSkill = 9,
        BowStab = 10,
        BellField = 11,
        BellFieldBurst = 12,
    }

    /// <summary> 소유 플레이어에 동기화하는 약점/잔향. 시각 효과는 클라이언트가 만든다 </summary>
    [Serializable]
    public struct WeaponAbilityMark
    {
        public TargetRef Target;
        public Vector3 LocalPoint;
        public uint Generation;
        public byte Count;
        public double ExpiresAt;
        public uint Reservation;

        public bool IsActive(double now) => Generation != 0u && Target.IsValid && now < ExpiresAt;
    }
}
