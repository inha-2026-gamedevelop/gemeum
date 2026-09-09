namespace Geumeum.Common
{
    /// <summary>
    /// Combat이 구현할 삼켜짐 생명 원본. AI는 실제 HP나 PlayerLifeState를 직접 고치지 않고
    /// 피해, 구조, 만료, 기믹 성공만 이 계약으로 요청한다.
    /// </summary>
    public interface IBossSwallowTarget : IDamageable
    {
        ParticipantId Participant       { get; }
        bool          IsSwallowed       { get; }
        uint          SwallowGeneration { get; }

        BossSwallowDamageResult ServerApplyBossDamage(in DamageInfo damage,
            in BossSwallowDamageContext context);

        bool ServerTryRescueSwallowed(ParticipantId rescuer, uint expectedGeneration,
            float restoreHealthRatio);

        bool ServerExpireSwallowed(double gameplayTime);

        bool ServerReleaseSwallowed(float restoreHealthRatio);
    }
}
