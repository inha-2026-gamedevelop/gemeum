// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// phase·생명 상태의 공통 허용 여부만 계산하는 순수 판정. Mirror에 의존하지 않는다.
    /// 쿨다운·자원 검증은 각 능력의 상태 소유자(PlayerAttack · ParryController 등)가 스스로 한다
    /// </summary>
    public static class PlayerActionGate
    {
        /// <summary> 이동 · 공격 · 패링 · 구르기가 공통으로 요구하는 최소 조건 </summary>
        public static bool CanAct(RunPhase phase, PlayerLifeState lifeState)
        {
            if ((phase == RunPhase.Lobby) || (phase == RunPhase.Result))
            {
                return false;
            }

            return ((lifeState == PlayerLifeState.Alive) || (lifeState == PlayerLifeState.ReviveInvulnerable));
        }
    }
}
