namespace Geumeum.Common
{
    /// <summary> 성장 시스템의 공용 조회 창구. 전투와 네트워크가 Systems 구현을 직접 참조하지 않게 한다 </summary>
    public interface IGrowthProvider
    {
        bool TryGetState(ParticipantId participant, out GrowthState state);
    }
}
