namespace Geumeum.Common
{
    /// <summary> 잡귀 공통 표현 상태. Brain의 세부 상태를 애니메이터가 이해할 수 있는 값으로 단순화한다 </summary>
    public enum EnemyVisualState
    {
        Idle      = 0, // 대상 없음 또는 인지 범위 밖
        Move      = 1, // 추적 · 후퇴 등 이동 중
        Telegraph = 2, // 공격 선딜 진행 중
        Charge    = 3, // 고정 방향 돌진 중 — 창귀 전용, 다른 종은 쓰지 않는다
        Attack    = 4, // 공격 판정 실행
        Recovery  = 5, // 공격 직후 재행동 대기
        Stagger   = 6, // 일반 피격 경직
        Knockback = 7, // 피격 누적 문턱 도달 또는 패링당해 넉백
    }
}
