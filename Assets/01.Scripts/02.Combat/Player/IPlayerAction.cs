namespace Geumeum.Combat
{
    /// <summary>
    /// 단발 행동 하나가 PlayerActionPipeline에 제공해야 하는 세 조각.
    /// 실행 순서는 파이프라인이 소유하고, 행동은 자기 값과 자기 상태 확정만 책임진다
    /// </summary>
    // 공격, 구르기, 패링이 각자 "중복 검사 -> 게이트 -> 자원 차감 -> 커밋"을 다시 쓰고 있었다.
    // 순서를 손으로 다시 쓰면 한 단계를 빼먹어도 컴파일이 되고, 실제로 그렇게 났던 적이 있다 -
    // RequestIdTracker를 readonly 필드로 잡아 방어 복사본이 생기는 바람에
    // 중복 차단이 통째로 동작하지 않았다. 순서를 한 군데로 모으면 빼먹을 자리가 사라진다.
    //
    // 구현체는 NetworkBehaviour 자신이다. 요청마다 커맨드 객체를 새로 만들면
    // 입력할 때마다 GC 할당이 생기고, 전투는 초당 여러 번 도는 경로다
    public interface IPlayerAction
    {
        /// <summary>
        /// 이번 요청의 게이트 입력을 만든다. 자원 비용도 여기서 정한다.
        /// 무기가 없거나 값을 읽을 수 없으면 false를 돌려 거부시킨다
        /// </summary>
        bool TryBuildContext(double serverTime, out PlayerActionContext context);

        /// <summary>
        /// 게이트를 통과한 뒤 자원을 실제로 차감한다.
        /// 어떤 자원인지는 행동이 정한다 - 스태미나를 쓰는 행동과 신력을 쓰는 행동이 섞인다
        /// </summary>
        bool TrySpendResource(in PlayerActionContext context);

        /// <summary> 자원까지 통과한 요청의 상태를 확정하고 전파한다 </summary>
        void CommitAction(in PlayerActionContext context);
    }
}
