namespace Geumeum.Common
{
    /// <summary>
    /// 한 참가자에게 지금 적용 중인 서버 전투 버프 계수를 공급한다
    /// 장비, 특성 snapshot과 달리 매 행동마다 다시 물어야 하므로 revision을 두지 않는다
    /// </summary>
    public interface ICombatBuffProvider
    {
        bool TryGetDamageMultiplier(ParticipantId participant, out float multiplier);
    }
}
