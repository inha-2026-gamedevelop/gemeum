namespace Geumeum.Common
{
    /// <summary>
    /// 회복을 받을 수 있는 대상. 플레이어가 구현한다.
    /// 무녀 치유(전투)와 회복 물약(시스템)이 서로의 구현을 모른 채 같은 창구를 쓴다.
    /// 구현체의 TryRestoreHealth는 서버 전용이며 클라이언트에서 호출하면 결과가 사람마다 갈린다
    /// </summary>
    public interface IRecoveryTarget
    {
        /// <summary> 이미 죽었는지, 쓰러진 대상에게 들어간 회복은 부활 규칙을 우회하므로 호출부가 먼저 확인한다 </summary>
        bool IsDead { get; }

        /// <summary>
        /// 회복을 적용한다, 서버에서만 호출한다.
        /// 쇠약과 회복 봉쇄는 구현체가 IBarrierState에 물어 적용하므로 호출부는 원본 회복량만 넘긴다.
        /// 실제로 오른 양이 없으면(사망, 회복 봉쇄, 이미 최대치) false
        /// </summary>
        bool TryRestoreHealth(float amount);
    }
}
