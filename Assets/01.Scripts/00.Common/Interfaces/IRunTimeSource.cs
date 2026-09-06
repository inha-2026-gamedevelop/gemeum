namespace Geumeum.Common
{
    /// <summary>
    /// 런 시간을 읽는 창구. 결계, 스폰, 보스가 RunManager의 구체 타입을 몰라도 시간을 읽는다.
    ///
    /// 시계는 둘이고 소유자는 하나다. 각 파트가 자기 경과 시간을 따로 누적하면
    /// 일시정지와 씬 전환에서 저마다 다른 시간을 갖게 된다
    /// </summary>
    public interface IRunTimeSource
    {
        /// <summary> 필드 구간의 경과 시간(초). 보스전에서는 더 이상 늘지 않는다 </summary>
        double FieldElapsedSeconds { get; }

        /// <summary> 보스전 경과 시간(초). 보스 시계가 열리기 전에는 0이다 </summary>
        double BossElapsedSeconds { get; }

        /// <summary>
        /// 게임 진행 시간(초). 두 시계의 합이다.
        /// 표식 만료, 관전과 삼켜짐, 구조 홀드, AI 쿨다운처럼 "게임이 도는 동안"만 흘러야 하는 것이 이 시간을 쓴다
        /// </summary>
        double GameplayElapsedSeconds { get; }

        /// <summary> 지금 걸려 있는 정지 사유. 하나라도 있으면 런은 멈춰 있다 </summary>
        RunPauseReason PauseReasons { get; }

        /// <summary> 멈춰 있는지. PauseReasons가 None이 아닌 것과 같다 </summary>
        bool IsPaused { get; }
    }
}
