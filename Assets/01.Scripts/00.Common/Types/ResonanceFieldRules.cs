// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 공명진 하나의 원천 신원. 같은 시전자가 다시 깔아도 새 FieldId를 받으므로
    /// 반복 입장이나 재설치로 계수가 누적되지 않는다
    /// </summary>
    [Serializable]
    public struct ResonanceFieldSource : IEquatable<ResonanceFieldSource>
    {
        public ParticipantId Caster;
        public uint          FieldId;

        public bool IsValid => (Caster.IsValid) && (FieldId != 0u);

        public bool Equals(ResonanceFieldSource other)
        {
            return (FieldId == other.FieldId) && (Caster == other.Caster);
        }

        public override bool Equals(object obj) =>
            ((obj is ResonanceFieldSource other) && Equals(other));

        public override int GetHashCode() => FieldId.GetHashCode();
    }

    /// <summary>
    /// 공명진의 반경, 수명, 체류 판정을 서버 판정과 표현이 함께 쓰는 순수 규칙
    /// 화면이 계산한 값과 서버가 확정한 값이 갈라지지 않도록 한곳에서만 만든다
    /// </summary>
    public static class ResonanceFieldRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 진 반경. 퍼뜨리는 길의 보조 노드와 끝노드가 함께 더해진다 </summary>
        public static float ResolveRadius(float baseRadius, float radiusBonus,
            float wideningBonus)
        {
            float radius = baseRadius + Positive(radiusBonus) + Positive(wideningBonus);
            return (IsFinite(radius) && (radius > 0f)) ? radius : 0f;
        }

        /// <summary>
        /// 진 지속 시간. 「함께 걷는 울림」은 반경을 넓히는 대신 지속을 깎으므로
        /// 끝노드를 샀을 때만 벌점을 적용한다
        /// </summary>
        public static float ResolveDuration(float baseDuration, float wideningBonus,
            float wideningPenalty)
        {
            if ((!IsFinite(baseDuration)) || (baseDuration <= 0f))
            {
                return 0f;
            }

            if (Positive(wideningBonus) <= 0f)
            {
                return baseDuration;
            }

            return Mathf.Max(0.01f, baseDuration - Positive(wideningPenalty));
        }

        /// <summary>
        /// 이탈 후 유지 시간. 「1.5초 x (1 + 장비 지속 보너스%/100) + 특성 추가」이며
        /// 상한을 넘지 않는다. 진 자체 수명과 공격력 계수에는 적용하지 않는다
        /// </summary>
        public static float ResolveTailDuration(float baseTail,
            float equipmentDurationPercent, float talentSeconds, float maximum)
        {
            if ((!IsFinite(baseTail)) || (baseTail < 0f))
            {
                return 0f;
            }

            float percent = IsFinite(equipmentDurationPercent)
                ? Mathf.Max(-100f, equipmentDurationPercent)
                : 0f;

            float tail = (baseTail * (1f + (percent * 0.01f))) + Positive(talentSeconds);
            if (!IsFinite(tail))
            {
                return 0f;
            }

            tail = Mathf.Max(0f, tail);
            return ((IsFinite(maximum)) && (maximum > 0f)) ? Mathf.Min(tail, maximum) : tail;
        }

        /// <summary>
        /// 발 위치가 진 안인가. 수평 반경과 높이차를 따로 본다 -
        /// 구체 하나로 재면 위층에 선 사람이 아래 진의 버프를 받는다
        /// </summary>
        public static bool IsWithinField(Vector3 center, Vector3 foot, float radius,
            float heightTolerance)
        {
            if ((!IsFinite(radius)) || (radius <= 0f) || (!IsFinite(center)) ||
                (!IsFinite(foot)) || (!IsFinite(heightTolerance)))
            {
                return false;
            }

            float dx = foot.x - center.x;
            float dz = foot.z - center.z;
            if (((dx * dx) + (dz * dz)) > (radius * radius))
            {
                return false;
            }

            return Mathf.Abs(foot.y - center.y) <= Mathf.Max(0f, heightTolerance);
        }

        /// <summary> 진 중심과 발 위치를 각각 지면에서 같은 높이만큼 올린 시야 검사선 </summary>
        public static void BuildSightSegment(Vector3 center, Vector3 foot, float offset,
            out Vector3 from, out Vector3 to)
        {
            Vector3 lift = Vector3.up * Mathf.Max(0f, offset);
            from = center + lift;
            to   = foot + lift;
        }

        private static float Positive(float value)
        {
            return ((IsFinite(value)) && (value > 0f)) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
