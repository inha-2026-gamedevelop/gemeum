// System
using System;

namespace Geumeum.Common
{
    /// <summary> 한 런에서 파티가 함께 보는 성장 상태 </summary>
    [Serializable]
    public readonly struct GrowthState : IEquatable<GrowthState>
    {
        /****************************************
        *              Properties
        ****************************************/

        public int   Level                  { get; }
        public int   Experience             { get; }
        public float MaxHealth              { get; }
        public uint  Revision               { get; }
        public int   TalentPoints           { get; }

        /// <summary> UI와 저장 계층에서 누적 경험치라는 뜻을 드러내는 별칭 </summary>
        public int TotalExperience => Experience;

        /// <summary> 현재 배분하지 않은 특성 포인트 </summary>
        public int AvailableTalentPoints => TalentPoints;

        /// <summary> 데이터 창구에 전달할 수 있는 최소 유효성 </summary>
        public bool IsValid
        {
            get
            {
                return ((Level > 0) && (Experience >= 0) &&
                        (!float.IsNaN(MaxHealth)) && (!float.IsInfinity(MaxHealth)) &&
                        (MaxHealth > 0f) && (Revision > 0u) && (TalentPoints >= 0));
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthState(int level, int experience, float maxHealth, uint revision,
            int talentPoints)
        {
            Level        = level;
            Experience   = experience;
            MaxHealth    = maxHealth;
            Revision     = revision;
            TalentPoints = talentPoints;
        }

        public bool Equals(GrowthState other)
        {
            return ((Level == other.Level) && (Experience == other.Experience) &&
                    (MaxHealth.Equals(other.MaxHealth)) && (Revision == other.Revision) &&
                    (TalentPoints == other.TalentPoints));
        }

        public override bool Equals(object obj)
        {
            return ((obj is GrowthState other) && Equals(other));
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Level;
                hash = (hash * 397) ^ Experience;
                hash = (hash * 397) ^ MaxHealth.GetHashCode();
                hash = (hash * 397) ^ (int)Revision;
                hash = (hash * 397) ^ TalentPoints;
                return hash;
            }
        }

        public static bool operator ==(GrowthState left, GrowthState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GrowthState left, GrowthState right)
        {
            return !left.Equals(right);
        }
    }
}
