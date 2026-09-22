namespace Geumeum.Common
{
    /// <summary> 일반 적만 구현하는 스킬 경직 창구. 보스 카운터와 분리한다 </summary>
    public interface IWeaponSkillStaggerTarget
    {
        bool TryApplyWeaponSkillStagger(float duration, float resistance);
    }
}
