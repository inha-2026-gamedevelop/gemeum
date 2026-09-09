namespace Geumeum.Common
{
    /// <summary> 정밀 화살 명중으로 현재 공격을 중단할 수 있는 서버 대상 </summary>
    public interface IPrecisionInterruptTarget
    {
        /// <summary> 이 구현이 나타내는 실제 서버 대상과 부위 </summary>
        TargetRef PrecisionTargetRef { get; }

        /// <summary> 현재 중단 창이 이 명중을 받아들였으면 true를 반환한다 </summary>
        bool TryInterruptPrecisionShot(in PrecisionInterruptContext context);
    }
}
