// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 데미지 숫자를 합칠 수 있는지 가르는 키(COMBAT_STYLE_DESIGN S10 합산 규칙)
    /// 같은 공격자·공격 실행·대상(부위 포함)·피해 종류·치명 여부만 한 숫자로 합친다
    /// 서로 다른 평타 실행, 본체와 꼬리, 일반과 치명은 합치지 않는다
    /// </summary>
    public readonly struct DamageNumberKey : IEquatable<DamageNumberKey>
    {
        /****************************************
        *                Fields
        ****************************************/

        public readonly uint             AttackerNetId;
        public readonly uint             ExecutionId;
        public readonly uint             TargetNetId;
        public readonly sbyte            PartIndex;
        public readonly DamageSourceKind Source;
        public readonly bool             IsCritical;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 실행 번호를 모르는(0) 피해는 서로 다른 공격일 수 있어 합치지 않는다 </summary>
        public bool CanMerge => (ExecutionId != 0);

        /****************************************
        *              Constructor
        ****************************************/

        public DamageNumberKey(uint attackerNetId, uint executionId, uint targetNetId,
            sbyte partIndex, DamageSourceKind source, bool isCritical)
        {
            AttackerNetId = attackerNetId;
            ExecutionId   = executionId;
            TargetNetId   = targetNetId;
            PartIndex     = partIndex;
            Source        = source;
            IsCritical    = isCritical;
        }

        /****************************************
        *              Public API
        ****************************************/

        public static DamageNumberKey From(in DamageResult result)
        {
            return new DamageNumberKey(result.AttackerNetId, result.ExecutionId, result.TargetNetId,
                result.PartIndex, result.Source, result.IsCritical);
        }

        public bool Equals(DamageNumberKey other)
        {
            return (AttackerNetId == other.AttackerNetId) &&
                (ExecutionId == other.ExecutionId) &&
                (TargetNetId == other.TargetNetId) &&
                (PartIndex == other.PartIndex) &&
                (Source == other.Source) &&
                (IsCritical == other.IsCritical);
        }

        public override bool Equals(object obj)
        {
            return (obj is DamageNumberKey other) && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(AttackerNetId, ExecutionId, TargetNetId, PartIndex, (byte)Source, IsCritical);
        }
    }
}
