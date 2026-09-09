namespace Geumeum.Common
{
    /// <summary> Systems가 현재 무기 종류를 읽고 서버 장착 값을 적용하는 공통 창구 </summary>
    public interface IEquipmentWeaponTarget
    {
        bool HasWeapon { get; }
        WeaponKind Weapon { get; }

        event System.Action<WeaponKind> WeaponChanged;

        /// <summary> 서버가 확정한 snapshot의 무기 종류를 적용한다 </summary>
        bool ServerSetEquippedWeapon(in WeaponCombatSnapshot snapshot);

        /// <summary> 현재 역할 무기와 일치할 때 공개 없는 변경 계획을 잠근다 </summary>
        bool ServerTryAcquireEquippedWeapon(in WeaponCombatSnapshot snapshot,
            out EquipmentWeaponWrite write);

        /// <summary> 잠근 계획을 서버 원본에만 적용한다 </summary>
        void ServerApplyEquippedWeapon(in EquipmentWeaponWrite write);

        /// <summary> 관련 원본이 모두 확정된 뒤 역할 무기를 공개한다 </summary>
        void ServerPublishEquippedWeapon(in EquipmentWeaponWrite write);

        void ServerReleaseEquippedWeapon();
    }
}
