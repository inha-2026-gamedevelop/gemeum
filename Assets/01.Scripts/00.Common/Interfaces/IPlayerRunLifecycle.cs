// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 런 경계에서 서버가 플레이어에게 하는 조작. 위치 이동과 상태 초기화만 담는다
    /// 역할 배분은 IPlayerRoleAssignment가 따로 맡는다
    /// </summary>
    public interface IPlayerRunLifecycle
    {
        /// <summary> 서버 기준 현재 위치. 이탈 직전 자리를 기억할 때 읽는다 </summary>
        Vector3 Position { get; }

        /// <summary> 서버 위치를 즉시 옮기고 소유 클라이언트에 알린다 </summary>
        void Teleport(Vector3 position);

        /// <summary> HP, 자원, 생명 상태를 런 시작값으로 되돌린다 </summary>
        void ResetForNewRun();

        /// <summary>
        /// 지금 값을 그릇에 담는다. 이탈 직전과 씬을 옮기기 직전에 부른다
        /// 상태를 바꾸지 않으며, 담을 것이 하나도 없으면 false다
        /// </summary>
        bool CaptureRunState(RunParticipantSnapshot snapshot);

        /// <summary>
        /// 그릇의 값을 되붓는다. 되부을 수 없는 값이 하나라도 있으면 아무것도 바꾸지 않고 false다 -
        /// 반쯤 복구된 상태로 런을 재개하면 그 판이 통째로 이상해진다
        /// </summary>
        bool RestoreRunState(RunParticipantSnapshot snapshot);
    }
}
