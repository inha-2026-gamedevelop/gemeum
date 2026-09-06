namespace Geumeum.Common
{
    /// <summary>
    /// 참가자 상태를 담고 되붓는 계약. 자기 값을 아는 컴포넌트가 직접 구현한다.
    ///
    /// 이탈과 재접속, 씬 전환이 각자 다른 방식으로 상태를 옮기면 다섯 자리에서 각각 다르게 틀린다.
    /// 셋 다 이 계약 하나를 쓴다.
    ///
    /// 담기와 검사는 아무것도 바꾸지 않는다. 필수 값이 없으면 되붓기 전에 실패로 알려야 하고,
    /// 반쯤 복구된 상태로 런을 재개해서는 안 되기 때문이다
    /// </summary>
    public interface IRunStateCarrier
    {
        /// <summary> 낮은 값이 먼저 담기고 먼저 되부어진다. 성장과 최대값이 현재 자원보다 앞서야 한다 </summary>
        int CarrierOrder { get; }

        /// <summary> 자기 값을 그릇에 담는다. 상태를 바꾸지 않는다 </summary>
        void Capture(RunParticipantSnapshot snapshot);

        /// <summary> 이 그릇을 되부을 수 있는지 본다. 상태를 바꾸지 않고, 실패하면 이유를 남긴다 </summary>
        bool Validate(RunParticipantSnapshot snapshot, out string failure);

        /// <summary> 그릇의 값을 자기 상태에 붓는다. Validate가 통과한 뒤에만 부른다 </summary>
        bool Restore(RunParticipantSnapshot snapshot);
    }
}
