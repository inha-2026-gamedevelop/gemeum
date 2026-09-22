namespace Geumeum.Common
{
    /// <summary>
    /// 런 시작 시 배분된 무기와 캐릭터를 플레이어에게 확정하는 계약
    /// 로비는 이 계약만 알고 Combat의 개별 컴포넌트를 직접 참조하지 않는다
    /// </summary>
    public interface IPlayerRoleAssignment
    {
        /// <summary> 무기와 캐릭터가 이미 확정되어 있다 </summary>
        bool HasRole { get; }

        /// <summary>
        /// 무기와 캐릭터를 함께 확정한다. 한쪽만 적용된 상태를 남기지 않으려고
        /// 둘 다 확정 가능한지 먼저 확인한 뒤에 적용한다
        /// </summary>
        bool TryCommitRole(WeaponKind weapon, CharacterKind character);

        /// <summary> 런이 끝나 로비로 돌아갈 때 확정을 되돌린다. 다음 런의 재배분을 위해 필요하다 </summary>
        void ClearRole();
    }
}
