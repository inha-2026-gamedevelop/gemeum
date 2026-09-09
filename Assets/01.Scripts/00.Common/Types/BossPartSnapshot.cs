// System
using System;

namespace Geumeum.Common
{
    /// <summary> 서버가 확정한 보스 꼬리 세 덩이의 현재 상태 </summary>
    [Serializable]
    public struct BossPartSnapshot
    {
        public float  Part1Health;
        public float  Part2Health;
        public float  Part3Health;
        public byte   BrokenMask;
        public byte   ActivePartId;
        public uint   GroggyGeneration;
        public bool   BrokePartThisGeneration;
        public float  AttackDamageMultiplier;
        public uint   Revision;
        public double ChangedServerTime;

        public bool IsBroken(byte partId)
        {
            return ((partId >= BossTargetPartIds.Tail1) && (partId <= BossTargetPartIds.Tail3) &&
                    ((BrokenMask & (1 << (partId - 1))) != 0));
        }

        public float GetHealth(byte partId)
        {
            switch (partId)
            {
                case BossTargetPartIds.Tail1: return Part1Health;
                case BossTargetPartIds.Tail2: return Part2Health;
                case BossTargetPartIds.Tail3: return Part3Health;
                default:                      return 0f;
            }
        }

        public int BrokenCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < 3; ++i)
                {
                    if ((BrokenMask & (1 << i)) != 0)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }
    }

    /// <summary> 보스 본체와 고정 부위가 공유하는 TargetRef PartId </summary>
    public static class BossTargetPartIds
    {
        public const byte Tail1 = 1;
        public const byte Tail2 = 2;
        public const byte Tail3 = 3;
        public const byte Soul1 = 11;
        public const byte Soul2 = 12;
        public const byte Soul3 = 13;

        public static byte SoulTargetPartId(byte soulId)
        {
            return ((soulId >= 1) && (soulId <= 3)) ? (byte) (10 + soulId) : (byte) 0;
        }
    }
}
