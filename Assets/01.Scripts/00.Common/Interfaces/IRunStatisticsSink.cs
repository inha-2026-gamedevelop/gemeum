namespace Geumeum.Common
{
    /// <summary> 전투와 생명 적용 지점이 서버 확정 사실을 넘기는 창구 </summary>
    public interface IRunStatisticsSink
    {
        bool RecordDamage(in RunAppliedDamageFact fact);
        bool RecordHealing(in RunAppliedHealingFact fact);
        bool RecordLife(in RunPlayerLifeFact fact);
        bool RecordCounter(in RunCounterFact fact);
        bool RecordParticipantState(in RunParticipantStateFact fact);
    }
}
