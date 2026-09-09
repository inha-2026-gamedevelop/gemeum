// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> 서버 플레이어가 필드의 정식 보스 입구에 닿은 사실을 RunManager에 전달한다 </summary>
    [RequireComponent(typeof(Collider))]
    public class BossEntranceTrigger : MonoBehaviour
    {
        /****************************************
        *              Unity Event
        ****************************************/

        private void OnTriggerEnter(Collider other)
        {
            if ((!NetworkServer.active) || (other == null))
            {
                return;
            }

            NetworkIdentity identity = other.GetComponentInParent<NetworkIdentity>();
            if ((identity == null) || (identity.connectionToClient == null))
            {
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                return;
            }

            run.ServerTryStartBossEntranceCountdown(identity.connectionToClient);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            Collider trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }
#endif
    }
}
