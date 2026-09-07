namespace Geumeum.Combat
{
    /// <summary> 플레이어 생명 상태 </summary>
    public enum PlayerLifeState
    {
        Alive              = 0,
        Downed             = 1, // 사망 판정 직후, 같은 죽음을 두 번 처리하지 않기 위한 서버 전이 상태
        Spectating         = 2, // 관전 대기, 전멸 판정에서는 사망자로 센다
        ReviveInvulnerable = 3, // 부활 직후 무적, 행동 가능한 생존자로 센다
    }
}
