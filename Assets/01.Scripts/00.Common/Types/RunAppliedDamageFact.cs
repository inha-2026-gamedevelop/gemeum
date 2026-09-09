// System
using System;

namespace Geumeum.Common
{
    /// <summary> 서버 HP 적용 뒤 확정된 실제 피해량 </summary>
    public readonly struct RunAppliedDamageFact
    {
        public RunId        Run        { get; }
        public ParticipantId Attacker  { get; }
        public TargetRef     Target    { get; }
        public float         AppliedAmount { get; }

        public bool IsValid => (Run.IsValid && Attacker.IsValid && Target.IsValid &&
            IsFinitePositive(AppliedAmount));

        public RunAppliedDamageFact(RunId run, ParticipantId attacker, TargetRef target,
            float appliedAmount)
        {
            Run           = run;
            Attacker      = attacker;
            Target        = target;
            AppliedAmount = appliedAmount;
        }

        private static bool IsFinitePositive(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f));
        }
    }
}
