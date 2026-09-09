namespace Geumeum.Common
{
    /// <summary> 런별 서버 사실을 시작하고 결과 snapshot으로 한 번 동결하는 계약 </summary>
    public interface IRunResultFactsProvider
    {
        bool BeginRun(RunId run);
        bool TryFreeze(ref RunResultSnapshot snapshot);
        void ResetRun(RunId run);
    }
}
