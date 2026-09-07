namespace Geumeum.Common
{
    /// <summary> 무기에서 파생되는 규칙. 무기가 곧 직업이라 판정 방식도 여기서 유도한다 </summary>
    public static class WeaponKindExtensions
    {
        /// <summary>
        /// 이 무기의 기본 공격 판정 방식.
        /// 방울은 치유, 버프 설계가 끝나기 전까지 기본 공격을 두지 않는다
        /// </summary>
        public static AttackReach ToReach(this WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword: return AttackReach.Melee;
                case WeaponKind.Bow:   return AttackReach.Ranged;
                case WeaponKind.Bell:  return AttackReach.Area;
                default:               return AttackReach.None;
            }
        }
    }
}
