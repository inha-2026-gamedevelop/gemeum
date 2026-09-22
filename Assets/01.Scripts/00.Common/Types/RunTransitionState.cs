namespace Geumeum.Common
{
    /// <summary>
    /// 씬 전환 한 번이 지나가는 단계
    /// 직렬화되므로 명시값을 유지하고 기존 값을 재배열하지 않는다
    /// </summary>
    public enum RunTransitionState
    {
        Idle                 = 0, // 전환 중이 아니다
        Freezing             = 1, // 정지 사유를 걸고 판정을 멈춘다
        Capturing            = 2, // 참가자 상태를 그릇에 담는다
        Loading              = 3, // 씬을 옮긴다
        Binding              = 4, // 도착한 씬에 참조를 다시 건다
        Restoring            = 5, // 담아 둔 상태를 되붓는다
        Committing           = 6, // 정지 사유를 풀고 런을 재개한다
        WaitingForParticipant = 7, // 도착하지 않은 사람을 기다린다
        Failed               = 8, // 되부을 수 없어 멈췄다. 사람이 개입해야 한다
    }
}
