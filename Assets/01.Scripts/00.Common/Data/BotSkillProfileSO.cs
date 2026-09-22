using System;
using UnityEngine;

namespace Geumeum.Data
{
    [CreateAssetMenu(menuName = "Geumeum/Bot Skill Profile")]
    public sealed class BotSkillProfileSO : ScriptableObject
    {
        [Range(1f, 360f)] public float VisionAngle = 150f;
        [Min(0.1f)] public float VisionRange = 25f;
        [Min(0f)] public float ReactionMean = 0.28f;
        [Min(0f)] public float ReactionSigma = 0.06f;
        [Min(0f)] public float ReactionMin = 0.15f;
        [Min(0f)] public float ReactionMax = 0.45f;
        [Range(0f, 1f)] public float MissChance = 0.08f;
        [Min(0.1f)] public float ObservationRetrySeconds = 1f;
        [Min(0f)] public float AimSigmaDegrees = 1.5f;
        [Min(0f)] public float StaminaReserve = 25f;
        [Min(0f)] public float TimingSigma = 0.09f;
        [Range(0f, 1f)] public float WrongDirectionChance = 0.1f;
        [Range(0f, 1f)] public float FalseParryChance = 0.1f;
        [Range(1, 2)] public int MaxTrackedThreats = 2;
        [Min(0f)] public float RollLeadSeconds = 0.3f;
        [Min(0f)] public float AttackCommitSeconds = 0.7f;
        [Min(0f)] public float HitMemorySeconds = 3f;

        public double SampleReaction(System.Random random) => Math.Max(0d,
            Math.Min(Math.Max(ReactionMin, ReactionMax),
                Math.Max(ReactionMin, ReactionMean + Gaussian(random) * ReactionSigma)));

        public static double Gaussian(System.Random random)
        {
            return Math.Sqrt(-2d * Math.Log(1d - random.NextDouble())) *
                Math.Cos(2d * Math.PI * random.NextDouble());
        }
    }
}
