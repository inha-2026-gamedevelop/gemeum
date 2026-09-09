namespace Geumeum.Combat
{
    /// <summary> 같은 입력으로 구조할 수 있는 생명 상태 종류 </summary>
    public enum PlayerRescueTargetKind
    {
        None        = 0,
        Spectating = 1,
        Swallowed  = 2,
    }

    /// <summary> 같은 서버 틱에 끝난 부활 요청의 우선순위를 정한다 </summary>
    public enum ReviveResolutionKind
    {
        None      = 0,
        Automatic = 1,
        Rescue    = 2,
    }

    /// <summary> 구조와 자동 부활의 경합을 순수하게 판정한다 </summary>
    public static class ReviveResolutionRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구조가 준비됐으면 자동 부활보다 먼저 선택한다 </summary>
        public static ReviveResolutionKind Select(bool rescueReady, bool automaticReady)
        {
            if (rescueReady)
            {
                return ReviveResolutionKind.Rescue;
            }

            if (automaticReady)
            {
                return ReviveResolutionKind.Automatic;
            }

            return ReviveResolutionKind.None;
        }
    }
}
