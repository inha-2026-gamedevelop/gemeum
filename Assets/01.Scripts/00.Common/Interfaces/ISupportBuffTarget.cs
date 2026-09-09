namespace Geumeum.Common
{
    /// <summary> 무녀 치유가 부여하는 서버 전투 지원 버프 대상 </summary>
    public interface ISupportBuffTarget
    {
        bool ServerApplySupportBuff(float damageMultiplier, float duration);
    }
}
