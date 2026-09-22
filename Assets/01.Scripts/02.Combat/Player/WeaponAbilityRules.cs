using System;
using UnityEngine;

namespace Geumeum.Combat
{
    public static class WeaponAbilityRules
    {
        /// <summary> 넓은 몸통 명중 보조와 분리된 화살 중심선-쐐기 판정 </summary>
        public static bool IntersectsWedge(Vector3 origin, Vector3 direction, float distance,
            Vector3 point, float radius)
        {
            if (!Finite(origin) || !Finite(direction) || !Finite(point) ||
                !float.IsFinite(distance) || !float.IsFinite(radius) || distance < 0f || radius <= 0f ||
                direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }
            Vector3 unit = direction.normalized;
            float along = Mathf.Clamp(Vector3.Dot(point - origin, unit), 0f, distance);
            return (point - (origin + unit * along)).sqrMagnitude <= radius * radius;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    public readonly struct DirectAttackKey : IEquatable<DirectAttackKey>
    {
        private readonly uint _attacker;
        private readonly uint _action;
        private readonly bool _precision;

        public DirectAttackKey(uint attacker, uint action, bool precision)
        {
            _attacker = attacker;
            _action = action;
            _precision = precision;
        }
        public bool Equals(DirectAttackKey other) => _attacker == other._attacker &&
            _action == other._action && _precision == other._precision;
        public override bool Equals(object obj) => obj is DirectAttackKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_attacker, _action, _precision);
    }
}
