namespace Geumeum.Common
{
    /// <summary> 정밀 화살이 서버 대상에 실제로 명중한 사실 </summary>
    public readonly struct PrecisionInterruptContext
    {
        public uint          ActionId       { get; }
        public ParticipantId Attacker       { get; }
        public uint          AttackerNetId  { get; }
        public TargetRef     Target         { get; }
        public bool          IsPrecision    { get; }
        public double        HitServerTime    { get; }

        public PrecisionInterruptContext(
            uint actionId,
            ParticipantId attacker,
            uint attackerNetId,
            TargetRef target,
            bool isPrecision,
            double hitServerTime)
        {
            ActionId        = actionId;
            Attacker        = attacker;
            AttackerNetId   = attackerNetId;
            Target          = target;
            IsPrecision     = isPrecision;
            HitServerTime   = hitServerTime;
        }
    }
}
