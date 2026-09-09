namespace Geumeum.Common
{
    /// <summary> 개인 기여에서 구분하는 보스 카운터 종류 </summary>
    public enum RunCounterKind
    {
        Parry     = 0,
        Precision = 1,
        Resonance = 2,
    }

    /// <summary> 공격 실행 ID와 참가자별로 한 번만 인정하는 실제 카운터 사실 </summary>
    public readonly struct RunCounterFact
    {
        public RunId         Run             { get; }
        public RunCounterKind Kind            { get; }
        public ParticipantId Actor           { get; }
        public TargetRef      Target          { get; }
        public uint           AttackInstanceId { get; }
        public float          AppliedAmount   { get; }

        public bool IsValid => (Run.IsValid && Actor.IsValid && Target.IsValid &&
            ((Kind == RunCounterKind.Parry) || (Kind == RunCounterKind.Precision) ||
             (Kind == RunCounterKind.Resonance)) &&
            (AttackInstanceId != 0u) && IsFinitePositive(AppliedAmount));

        public RunCounterFact(RunId run, RunCounterKind kind, ParticipantId actor,
            TargetRef target, uint attackInstanceId, float appliedAmount)
        {
            Run              = run;
            Kind             = kind;
            Actor            = actor;
            Target           = target;
            AttackInstanceId = attackInstanceId;
            AppliedAmount    = appliedAmount;
        }

        private static bool IsFinitePositive(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f));
        }
    }
}
