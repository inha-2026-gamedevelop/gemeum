namespace Geumeum.Common
{
    /// <summary> 참가자 상태를 담고 되붓는 계약 </summary>
    public interface IRunStateCarrier
    {
        /// <summary> 낮은 값이 먼저 담기고 먼저 되부어진다 </summary>
        int CarrierOrder { get; }

        /// <summary> 자기 값을 그릇에 담는다. 상태를 바꾸지 않는다 </summary>
        void Capture(RunParticipantSnapshot snapshot);

        /// <summary> 이 그릇을 되부을 수 있는지 본다 </summary>
        bool Validate(RunParticipantSnapshot snapshot, out string failure);

        /// <summary> 그릇의 값을 자기 상태에 붓는다. Validate가 통과한 뒤에만 부른다 </summary>
        bool Restore(RunParticipantSnapshot snapshot);
    }
}
