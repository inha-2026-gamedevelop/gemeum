namespace Geumeum.Common
{
    /// <summary> 기본 공격의 판정 방식. 무기가 이 값을 정하고 PlayerAttack이 경로를 가른다 </summary>
    public enum AttackReach
    {
        None   = 0, // 기본 공격 없음
        Melee  = 1, // 정면 부채꼴 즉시 판정
        Ranged = 2, // 투사체를 띄워 비행 중 판정
        Area   = 3, // 자기 주변 원형 즉시 판정, 바라보는 방향을 가리지 않는다
    }
}
