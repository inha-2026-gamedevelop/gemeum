namespace Geumeum.Common
{
    /// <summary> 플레이어 역할 무기의 서버 원본과 공개값을 나눠 적용하는 계획 </summary>
    public readonly struct EquipmentWeaponWrite
    {
        public readonly bool       ExpectedHasWeapon;
        public readonly WeaponKind ExpectedWeapon;
        public readonly WeaponKind NextWeapon;

        public bool IsValid =>
            ((!ExpectedHasWeapon || System.Enum.IsDefined(typeof(WeaponKind), ExpectedWeapon)) &&
             System.Enum.IsDefined(typeof(WeaponKind), NextWeapon));

        public EquipmentWeaponWrite(bool expectedHasWeapon, WeaponKind expectedWeapon,
            WeaponKind nextWeapon)
        {
            ExpectedHasWeapon = expectedHasWeapon;
            ExpectedWeapon    = expectedWeapon;
            NextWeapon        = nextWeapon;
        }
    }
}
