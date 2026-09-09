namespace Geumeum.Common
{
    /// <summary> 결과 동결 직전에 살아 있는 참가자의 역할과 HP를 제공하는 읽기 전용 창구 </summary>
    public interface IRunResultParticipantSource
    {
        bool TryCaptureResultState(RunId run, out RunParticipantStateFact state);
    }
}
