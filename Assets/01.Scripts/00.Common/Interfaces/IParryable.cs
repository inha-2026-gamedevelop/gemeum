namespace Geumeum.Common
{
    /// <summary>
    /// 패링당할 수 있는 공격의 계약. 창귀 돌진과 보스의 패링 가능 패턴이 구현한다.
    /// 공격 측이 판정 창을 열고, 성공 통지를 받아 자기 경직을 처리한다
    /// </summary>
    public interface IParryable
    {
        /// <summary> 이 공격이 여는 판정 창 길이(초) </summary>
        float ParryWindowDuration { get; }

        /// <summary> 패링당했을 때 서버가 호출한다, 경직 등 공격 측 반응은 구현체가 정한다 </summary>
        void OnParried(uint parriedByNetId);
    }
}
