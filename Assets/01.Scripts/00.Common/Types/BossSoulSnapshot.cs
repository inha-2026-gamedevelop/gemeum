// System
using System;

namespace Geumeum.Common
{
    public enum BossFinalState
    {
        Dormant       = 0,
        SoulWindow    = 1,
        FailureAttack = 2,
        Killable      = 3,
    }

    /// <summary> 최종 구간 원혼 셋의 누적 제거 상태 </summary>
    [Serializable]
    public struct BossSoulSnapshot
    {
        public BossFinalState State;
        public float          Soul1Health;
        public float          Soul2Health;
        public float          Soul3Health;
        public byte           RemovedMask;
        public int            FailureBranchCount;
        public double         WindowDeadlineGameplayTime;
        public uint           Revision;
        public double         ChangedServerTime;

        public int RemainingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < 3; ++i)
                {
                    if ((RemovedMask & (1 << i)) == 0)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        public bool IsRemoved(byte soulId)
        {
            return ((soulId >= 1) && (soulId <= 3) &&
                    ((RemovedMask & (1 << (soulId - 1))) != 0));
        }

        public float GetHealth(byte soulId)
        {
            switch (soulId)
            {
                case 1: return Soul1Health;
                case 2: return Soul2Health;
                case 3: return Soul3Health;
                default: return 0f;
            }
        }
    }
}
