namespace Geumeum.Common
{
    /// <summary>
    /// 공명진이 주는 공격력 버프를 받는 쪽
    /// 진마다 원천 ID가 달라 만료가 서로 독립이고, 적용값은 유효한 것 중 가장 큰 하나다
    /// </summary>
    public interface ISupportBuffTarget
    {
        /// <summary> 진 안에서 유효 체류를 확인할 때마다 만료 시각을 미룬다 </summary>
        bool ServerApplyResonance(ResonanceFieldSource source, float damageMultiplier,
            double expiresAtGameplayTime);

        /// <summary> 시전자 쪽 사유로 그 원천의 남은 버프까지 즉시 회수한다 </summary>
        bool ServerRemoveResonance(ResonanceFieldSource source);

        /// <summary> 수혜자 쪽 사유(사망, 삼킴, 런/씬 이동)로 받은 버프를 전부 지운다 </summary>
        void ServerClearResonance();
    }
}
