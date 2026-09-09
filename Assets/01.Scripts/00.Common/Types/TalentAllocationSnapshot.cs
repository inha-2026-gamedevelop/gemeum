// System
using System;

namespace Geumeum.Common
{
    /// <summary> 한 참가자의 서버 확정 특성 배분 </summary>
    [Serializable]
    public struct TalentAllocationSnapshot : IEquatable<TalentAllocationSnapshot>
    {
        /****************************************
        *              Properties
        ****************************************/

        public uint Revision { get; }
        public bool HasWeapon { get; }
        public WeaponKind Weapon { get; }
        public int SpentPoints { get; }
        public int[] NodeIds { get; }

        public bool IsValid => (Revision != 0u) && (SpentPoints >= 0) && (NodeIds != null) &&
            ((!HasWeapon) || Enum.IsDefined(typeof(WeaponKind), Weapon));

        /****************************************
        *              Constructors
        ****************************************/

        public TalentAllocationSnapshot(uint revision, bool hasWeapon, WeaponKind weapon,
            int spentPoints, int[] nodeIds)
        {
            Revision    = revision;
            HasWeapon   = hasWeapon;
            Weapon      = weapon;
            SpentPoints = spentPoints;
            NodeIds     = (nodeIds == null) ? Array.Empty<int>() : (int[])nodeIds.Clone();
        }

        /****************************************
        *                Methods
        ****************************************/

        public static TalentAllocationSnapshot Empty(WeaponKind weapon)
        {
            return new TalentAllocationSnapshot(1u, true, weapon, 0, Array.Empty<int>());
        }

        public bool Contains(int nodeId)
        {
            if (NodeIds == null)
            {
                return false;
            }

            for (int i = 0; i < NodeIds.Length; ++i)
            {
                if (NodeIds[i] == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Equals(TalentAllocationSnapshot other)
        {
            if ((Revision != other.Revision) || (HasWeapon != other.HasWeapon) ||
                (Weapon != other.Weapon) || (SpentPoints != other.SpentPoints))
            {
                return false;
            }

            int leftCount  = (NodeIds == null) ? 0 : NodeIds.Length;
            int rightCount = (other.NodeIds == null) ? 0 : other.NodeIds.Length;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; ++i)
            {
                if (NodeIds[i] != other.NodeIds[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return (obj is TalentAllocationSnapshot other) && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Revision * 397) ^ SpentPoints;
        }
    }
}
