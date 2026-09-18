// System
using System;

namespace Geumeum.Common
{
    /// <summary> 전실에서 편성을 모으는 진행 상태. 화면은 이 값만 보고 그린다 </summary>
    [Serializable]
    public struct BossTrialCollectionState
    {
        public bool IsCollecting;
        public int  SubmittedCount;
        public int  RequiredCount;

        public bool IsComplete => (SubmittedCount >= RequiredCount);
    }
}
