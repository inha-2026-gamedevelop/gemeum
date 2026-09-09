namespace Geumeum.Common
{
    /// <summary> 재접속과 결과 동결이 읽는 참가자의 마지막 서버 상태 </summary>
    public readonly struct RunParticipantStateFact
    {
        public RunId         Run           { get; }
        public ParticipantId Participant   { get; }
        public bool          HasCharacter  { get; }
        public CharacterKind Character     { get; }
        public float         RemainingHealth { get; }

        public bool IsValid => (Run.IsValid && Participant.IsValid &&
            (!float.IsNaN(RemainingHealth)) && (!float.IsInfinity(RemainingHealth)) &&
            (RemainingHealth >= 0f));

        public RunParticipantStateFact(RunId run, ParticipantId participant,
            float remainingHealth)
        {
            Run             = run;
            Participant     = participant;
            HasCharacter    = false;
            Character       = default;
            RemainingHealth = remainingHealth;
        }

        public RunParticipantStateFact(RunId run, ParticipantId participant,
            CharacterKind character, float remainingHealth)
        {
            Run             = run;
            Participant     = participant;
            HasCharacter    = true;
            Character       = character;
            RemainingHealth = remainingHealth;
        }
    }
}
