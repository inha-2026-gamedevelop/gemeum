// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 한 프레임에 도착한 피해 결과를 공격 실행 단위로 묶는다(COMBAT_STYLE_DESIGN 5.1)
    /// 한 공격이 여러 적을 맞혀도 내 멈춤, 셰이크, 플래시는 그 실행에서 가장 높은 단계 한 번만 재생한다
    /// 고정 버퍼라 프레임마다 할당하지 않는다
    /// </summary>
    public sealed class ImpactExecutionMerger
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 실행 하나의 대표 결과. 가장 높은 단계를 만든 결과를 남긴다 </summary>
        public struct Entry
        {
            public uint         AttackerNetId;
            public uint         ExecutionId;
            public ImpactTier   Tier;
            public DamageResult Result;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Entry[] _entries;
        private int              _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count    => _count;
        public int Capacity => _entries.Length;

        /****************************************
        *              Constructor
        ****************************************/

        public ImpactExecutionMerger(int capacity)
        {
            _entries = new Entry[(capacity < 1) ? 1 : capacity];
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 결과 하나를 더한다. ExecutionId가 0이면 결과 하나를 한 실행으로 본다
        /// 버퍼가 가득 차 새 실행을 담지 못하면 false
        /// </summary>
        public bool Add(in DamageResult result, ImpactTier tier)
        {
            if (result.ExecutionId != 0u)
            {
                for (int i = 0; i < _count; ++i)
                {
                    if ((_entries[i].ExecutionId == result.ExecutionId) &&
                        (_entries[i].AttackerNetId == result.AttackerNetId))
                    {
                        // 같은 단계면 먼저 온 결과를 대표로 둔다
                        if (tier > _entries[i].Tier)
                        {
                            _entries[i].Tier   = tier;
                            _entries[i].Result = result;
                        }

                        return true;
                    }
                }
            }

            if (_count >= _entries.Length)
            {
                return false;
            }

            _entries[_count] = new Entry
            {
                AttackerNetId = result.AttackerNetId,
                ExecutionId   = result.ExecutionId,
                Tier          = tier,
                Result        = result,
            };
            ++_count;
            return true;
        }

        public Entry Get(int index)
        {
            return _entries[index];
        }

        public void Clear()
        {
            _count = 0;
        }
    }
}
