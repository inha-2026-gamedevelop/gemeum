namespace Geumeum.Common
{
    /// <summary> 특성 원본과 장비 전투 수치 revision을 같은 서버 확정 구간에서 갱신하는 창구 </summary>
    public interface ICombatLoadoutRevisionTarget
    {
        bool ServerRegisterTalentRevision(uint revision);

        bool ServerTryCommitTalentRevision(uint previousRevision, uint nextRevision);
    }
}
