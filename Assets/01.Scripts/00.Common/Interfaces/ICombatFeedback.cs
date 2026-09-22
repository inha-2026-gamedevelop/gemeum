namespace Geumeum.Common
{
    /// <summary>
    /// 타격감 호출부. 전투가 타이밍만 호출하고 표현은 아트가 구현한다
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

        /// <summary>
        /// 보스 그로기 진입 순간. 파티 셋이 동시에 달려드는 신호라 피격보다 세다
        /// </summary>
        void PlayGroggyEnter();

        /// <summary>
        /// 보스 부위 파괴 순간(꼬리 한 덩이). 이 게임에서 가장 센 셰이크다 -
        /// 25% 하드 게이트를 한 칸 여는 순간이라 결과가 분명해야 한다
        /// </summary>
        void PlayPartBreak();
    }
}
