namespace Geumeum.Common
{
    /// <summary> 한 번의 경험치 적용 결과. UI와 서버 로그가 같은 계산 결과를 보게 한다 </summary>
    public readonly struct GrowthApplyResult
    {
        public GrowthState Previous             { get; }
        public GrowthState Current              { get; }
        public int         AwardedExperience    { get; }
        public int         LevelsGained         { get; }
        public float       MaxHealthIncrease    { get; }
        public bool        Changed              => (Previous != Current);

        public GrowthApplyResult(GrowthState previous, GrowthState current, int awardedExperience,
            int levelsGained, float maxHealthIncrease)
        {
            Previous          = previous;
            Current           = current;
            AwardedExperience = awardedExperience;
            LevelsGained      = levelsGained;
            MaxHealthIncrease = maxHealthIncrease;
        }
    }
}
