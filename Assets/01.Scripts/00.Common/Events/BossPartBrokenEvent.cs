// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 고정 꼬리 부위 하나가 처음 파괴되어 드랍 1회를 청구하는 서버 사실 </summary>
    public readonly struct BossPartBrokenEvent
    {
        public TargetRef Target       { get; }
        public Vector3   Position     { get; }
        public uint      KillerNetId  { get; }
        public int       SealedPatternId { get; }

        public bool IsValid => (Target.IsValid && (!Target.IsBody) &&
            (SealedPatternId > 0) && IsFinite(Position));

        public BossPartBrokenEvent(TargetRef target, Vector3 position, uint killerNetId,
            int sealedPatternId)
        {
            Target          = target;
            Position        = position;
            KillerNetId     = killerNetId;
            SealedPatternId = sealedPatternId;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
