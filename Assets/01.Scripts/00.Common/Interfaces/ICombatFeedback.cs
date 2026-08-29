namespace Geumeum.Common
{
    /// <summary>
    /// 타격감 호출부. 전투가 타이밍만 호출하고 표현은 아트가 구현한다.
    /// 로컬 표현이므로 서버에서 호출하지 않고, 판정 결과를 받은 각 클라이언트가 자기 화면에서 호출한다
    /// </summary>
    public interface ICombatFeedback
    {
        /// <summary> 타격 순간, duration은 히트스톱 길이(초) </summary>
        void PlayHit(HitStrength strength, float duration);

        /// <summary> 패링 성공 순간 </summary>
        void PlayParrySuccess();

        /// <summary> 치명타 순간, multiplier는 피해 배수 </summary>
        void PlayCritical(float multiplier);
    }
}
