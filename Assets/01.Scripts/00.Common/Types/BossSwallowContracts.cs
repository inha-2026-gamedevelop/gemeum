namespace Geumeum.Common
{
    /// <summary> 최종 구간 보스 피해를 Combat 생명 원본에 넘기는 값 </summary>
    public readonly struct BossSwallowDamageContext
    {
        public bool   ConvertLethalToSwallowed { get; }
        public double GameplayTime             { get; }
        public float  SwallowDuration           { get; }

        public BossSwallowDamageContext(bool convertLethalToSwallowed,
            double gameplayTime, float swallowDuration)
        {
            ConvertLethalToSwallowed = convertLethalToSwallowed;
            GameplayTime             = gameplayTime;
            SwallowDuration           = swallowDuration;
        }
    }

    /// <summary> Combat이 실제 피해와 삼켜짐 전이를 확정해 AI에 돌려주는 값 </summary>
    public readonly struct BossSwallowDamageResult
    {
        public float         AppliedDamage      { get; }
        public bool          EnteredSwallowed   { get; }
        public ParticipantId Participant        { get; }
        public uint          SwallowGeneration  { get; }

        public BossSwallowDamageResult(float appliedDamage, bool enteredSwallowed,
            ParticipantId participant, uint swallowGeneration)
        {
            AppliedDamage     = appliedDamage;
            EnteredSwallowed  = enteredSwallowed;
            Participant       = participant;
            SwallowGeneration = swallowGeneration;
        }
    }
}
