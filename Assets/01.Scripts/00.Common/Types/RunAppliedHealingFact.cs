namespace Geumeum.Common
{
    /// <summary> 서버 HP 적용 뒤 확정된 실제 회복량 </summary>
    public readonly struct RunAppliedHealingFact
    {
        public RunId         Run           { get; }
        public ParticipantId Healer        { get; }
        public ParticipantId Target        { get; }
        public float         AppliedAmount { get; }

        public bool IsValid => (Run.IsValid && Healer.IsValid && Target.IsValid &&
            IsFinitePositive(AppliedAmount));

        public RunAppliedHealingFact(RunId run, ParticipantId healer, ParticipantId target,
            float appliedAmount)
        {
            Run           = run;
            Healer        = healer;
            Target        = target;
            AppliedAmount = appliedAmount;
        }

        private static bool IsFinitePositive(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f));
        }
    }
}
