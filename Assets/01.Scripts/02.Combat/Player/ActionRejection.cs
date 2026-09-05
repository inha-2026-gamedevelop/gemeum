namespace Geumeum.Combat
{
    /// <summary>
    /// 단발 행동 요청이 거부된 사유. None이면 수락이다.
    /// 직렬화될 수 있으므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    // 사유를 돌려주는 이유는 3인 테스트 때문이다. 지금까지 공격과 구르기는 거부되면 조용히
    // return이라, 입력이 씹힌 것인지 지연인지 거부인지 사람도 로그도 구분할 수 없었다.
    // 패링은 처음부터 ParryResultCode로 이것을 돌려주고 있었고, 그 부분만 뼈대로 끌어올린다
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
    }
}
