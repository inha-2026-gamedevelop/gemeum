namespace Geumeum.Combat
{
    /// <summary> 단발 행동 요청이 거부된 사유. None이면 수락이다 </summary>
    public enum ActionRejection
    {
        /// <summary> 수락됐다 </summary>
        None = 0,

        /// <summary> 중복이거나 역순으로 도착한 RequestId </summary>
        Duplicate = 1,

        /// <summary> GameDB나 필수 컴포넌트가 아직 준비되지 않았다 </summary>
        NotReady = 2,

        /// <summary> 이 행동을 쓸 무기가 없다 </summary>
        NoWeapon = 3,

        /// <summary> 런 단계, 생명 상태, 일시정지로 지금은 행동할 수 없다 </summary>
        State = 4,

        /// <summary> 다른 행동이 진행 중이거나 쿨다운이 끝나지 않았다 </summary>
        Busy = 5,

        /// <summary> 자원이 모자란다 </summary>
        Resource = 6,

        /// <summary> 방향이나 조준 입력이 유효하지 않다 </summary>
        InvalidInput = 7,
    }
}
