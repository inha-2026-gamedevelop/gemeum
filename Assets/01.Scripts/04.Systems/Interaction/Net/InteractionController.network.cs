// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class InteractionController
    {
        /****************************************
        *               Commands
        ****************************************/

        /// <summary>
        /// 상호작용 요청. 소유 플레이어 오브젝트에 붙어 있어 requiresAuthority 기본값(true)으로 충분하다.
        /// 대상이 실제로 상호작용 가능한지, 거리와 비전투 조건을 만족하는지는 대상이 다시 검사한다
        /// </summary>
        [Command]
        private void CmdInteract(uint targetNetId)
        {
            if (targetNetId == 0u)
            {
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            {
                return;
            }

            var interactable = identity.GetComponent<IInteractable>();
            if (interactable == null)
            {
                return;
            }

            // 자기 자신이나 다른 플레이어를 대상으로 위조하는 요청을 막는다
            if (identity == netIdentity)
            {
                return;
            }

            interactable.Interact(netId);
        }
    }
}
