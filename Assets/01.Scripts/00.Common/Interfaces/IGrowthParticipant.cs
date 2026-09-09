namespace Geumeum.Common
{
    /// <summary> 서버 성장 관리자가 플레이어 성장과 최대 HP를 적용하는 창구 </summary>
    public interface IGrowthParticipant
    {
        ParticipantId Participant { get; }

        GrowthState Growth { get; }

        event System.Action<GrowthState> GrowthChanged;

        /// <summary> 공용 성장 상태를 적용한다. 사망 상태는 최대 HP만 갱신하고 HP를 살리지 않는다 </summary>
        void ServerApplyGrowth(GrowthState state);
    }
}
