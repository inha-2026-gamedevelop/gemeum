namespace Geumeum.Common
{
    /// <summary> 런 시간을 읽는 창구. 결계, 스폰, 보스가 RunManager의 구체 타입을 몰라도 시간을 읽는다 </summary>
    public interface IRunTimeSource
    {
        /// <summary> 필드 구간의 경과 시간(초). 보스전에서는 더 이상 늘지 않는다 </summary>
        double FieldElapsedSeconds { get; }

        /// <summary> 보스전 경과 시간(초). 보스 시계가 열리기 전에는 0이다 </summary>
        double BossElapsedSeconds { get; }

        /// <summary> 게임 진행 시간(초). 두 시계의 합이다 </summary>
        double GameplayElapsedSeconds { get; }

        /// <summary> 지금 걸려 있는 정지 사유. 하나라도 있으면 런은 멈춰 있다 </summary>
        RunPauseReason PauseReasons { get; }

        /// <summary> 멈춰 있는지. PauseReasons가 None이 아닌 것과 같다 </summary>
        bool IsPaused { get; }
    }
}
