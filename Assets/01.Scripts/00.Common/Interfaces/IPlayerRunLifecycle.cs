// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 런 경계에서 서버가 플레이어에게 하는 조작. 위치 이동과 상태 초기화만 담는다.
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
    }
}
