namespace Geumeum.Common
{
    /// <summary> 잡귀 종류. EnemyController가 상속 대신 이 값으로 구동된다 </summary>
    public enum EnemyKind
    {
        Heokkaebi = 0, // 허깨비, 기본 근접
        Changgwi  = 1, // 창귀, 돌진 후 패링 가능
        Aekgwi    = 2, // 액귀, 원거리 저주 투사체
    }
}
