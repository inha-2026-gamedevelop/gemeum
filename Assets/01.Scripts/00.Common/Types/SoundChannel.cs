namespace Geumeum.Common
{
    /// <summary>
    /// 소리가 어느 믹서 그룹으로 갈지 정하는 분류.
    /// Player로 시작하는 둘은 재생 시점의 isLocalPlayer에 따라 Local과 Remote로 다시 갈린다 (SOUND_DESIGN 4.2)
    /// </summary>
    public enum SoundChannel
    {
        /// <summary> 패링, 치명타처럼 내가 낸 것을 반드시 구분해야 하는 소리 </summary>
        PlayerCritical = 0,

        /// <summary> 타격, 피격, 구르기, 발소리, 화살 </summary>
        PlayerNormal = 1,

        /// <summary> 보스, 잡귀, 결계, 환경, UI. 낸 주체가 플레이어가 아니라 로컬 구분이 없다 </summary>
        World = 2,
    }
}
