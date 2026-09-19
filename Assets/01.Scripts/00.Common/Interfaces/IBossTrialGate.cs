namespace Geumeum.Common
{
    /// <summary> 보스 트라이 전실이 아직 보스 개시를 막고 있는지 런 진행이 읽는 창구 </summary>
    public interface IBossTrialGate
    {
        /// <summary> 편성이 다 모이지 않아 보스를 열면 안 되는 상태인가 </summary>
        bool IsBossStartBlocked { get; }

        /// <summary> 다음 런을 보스 트라이로 시작해 달라고 방장이 요청했는가 </summary>
        bool IsTrialRequested { get; }
    }
}
