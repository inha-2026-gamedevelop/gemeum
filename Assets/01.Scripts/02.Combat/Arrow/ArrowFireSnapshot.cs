// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 서버가 발사 수락 시점에 확정한 화살 조건 </summary>
    public readonly struct ArrowFireSnapshot
    {
        public Vector3      Origin             { get; }
        public Vector3      Direction          { get; }
        public float        BasePower { get; }
        public CombatStatsSnapshot Talents { get; }
        public float        Damage             { get; }
        public HitStrength  Strength           { get; }
        public float        Speed              { get; }
        public float        HitRadius          { get; }
        public float        GuidanceDuration   { get; }
        public int          PierceCount        { get; }
        public int          BurstShotCount     { get; }
        public float        BurstShotInterval  { get; }
        public float        ResourceCost       { get; }
        public float        RecoveryDuration   { get; }
        public uint         AttackerNetId      { get; }
        public ParticipantId Attacker          { get; }
        public uint         ActionId           { get; }
        public int          ShotIndex          { get; }
        public bool         IsPrecision        { get; }
        public uint         RunIdValue         { get; }
        public uint         SceneRevisionValue { get; }
        public int          TargetMask         { get; }
        public uint         GuidanceTargetNetId { get; }
        public byte         GuidanceTargetPart { get; }

        public ArrowFireSnapshot(
            Vector3 origin,
            Vector3 direction,
            float damage,
            HitStrength strength,
            float speed,
            float hitRadius,
            float guidanceDuration,
            int pierceCount,
            int burstShotCount,
            float burstShotInterval,
            float resourceCost,
            float recoveryDuration,
            uint attackerNetId,
            ParticipantId attacker,
            uint actionId,
            int shotIndex,
            bool isPrecision,
            uint runIdValue,
            uint sceneRevisionValue,
            int targetMask,
            uint guidanceTargetNetId,
            byte guidanceTargetPart, float basePower = 0f, in CombatStatsSnapshot talents = default)
        {
            Talents = talents;
            Origin              = origin;
            Direction           = direction;
            Damage              = damage;
            BasePower = basePower > 0f ? basePower : damage;
            Strength            = strength;
            Speed               = speed;
            HitRadius           = hitRadius;
            GuidanceDuration    = guidanceDuration;
            PierceCount         = pierceCount;
            BurstShotCount      = burstShotCount;
            BurstShotInterval   = burstShotInterval;
            ResourceCost        = resourceCost;
            RecoveryDuration    = recoveryDuration;
            AttackerNetId       = attackerNetId;
            Attacker            = attacker;
            ActionId            = actionId;
            ShotIndex           = shotIndex;
            IsPrecision         = isPrecision;
            RunIdValue          = runIdValue;
            SceneRevisionValue  = sceneRevisionValue;
            TargetMask          = targetMask;
            GuidanceTargetNetId = guidanceTargetNetId;
            GuidanceTargetPart  = guidanceTargetPart;
        }

        /// <summary>
        /// 비용이 확정되는 순간의 공명진 계수를 이 발사에 한 번만 접는다
        /// 이미 날아간 화살은 원천이 사라져도 다시 계산하지 않는다
        /// </summary>
        public ArrowFireSnapshot WithResonance(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
                (multiplier <= 1f))
            {
                return this;
            }

            return new ArrowFireSnapshot(
                Origin,
                Direction,
                Damage * multiplier,
                Strength,
                Speed,
                HitRadius,
                GuidanceDuration,
                PierceCount,
                BurstShotCount,
                BurstShotInterval,
                ResourceCost,
                RecoveryDuration,
                AttackerNetId,
                Attacker,
                ActionId,
                ShotIndex,
                IsPrecision,
                RunIdValue,
                SceneRevisionValue,
                TargetMask,
                GuidanceTargetNetId,
                GuidanceTargetPart, BasePower * multiplier, Talents);
        }

        public ArrowFireSnapshot WithShotIndex(int shotIndex)
        {
            return new ArrowFireSnapshot(
                Origin,
                Direction,
                Damage,
                Strength,
                Speed,
                HitRadius,
                GuidanceDuration,
                PierceCount,
                BurstShotCount,
                BurstShotInterval,
                ResourceCost,
                RecoveryDuration,
                AttackerNetId,
                Attacker,
                ActionId,
                shotIndex,
                IsPrecision,
                RunIdValue,
                SceneRevisionValue,
                TargetMask,
                GuidanceTargetNetId,
                GuidanceTargetPart, BasePower, Talents);
        }
    }
}
