namespace Geumeum.Common
{
    /// <summary>
    /// 피해를 받을 수 있는 대상. 플레이어, 잡귀, 보스가 구현한다.
    /// 구현체의 TakeDamage는 서버 전용이며 클라이언트에서 호출하면 결과가 사람마다 갈린다
    /// </summary>
    public interface IDamageable
    {
        /// <summary> 이미 죽었는지, 사망 후 들어온 피해는 드랍과 점수 귀속을 중복시키므로 호출부가 먼저 확인한다 </summary>
        bool IsDead { get; }

        /// <summary> 피해를 적용한다, 서버에서만 호출한다 </summary>
        void TakeDamage(in DamageInfo damage);
    }
}
