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
        /// 상호작용 요청. 소유 플레이어 오브젝트에 붙어 있어 requiresAuthority 기본값(true)으로 충분하다
        /// 대상이 실제로 상호작용 가능한지, 거리와 비전투 조건을 만족하는지는 대상이 다시 검사한다
        /// </summary>
        [Command]
        private void CmdInteract(uint targetNetId)
        {
            ServerRequestInteract(targetNetId);
        }

        /// <summary> 후보를 서버에 전달한다. true는 전달 성공이며 실제 승인은 대상이 판정한다 </summary>
        [Server]
        public bool ServerRequestInteract(uint targetNetId)
        {
            if (targetNetId == 0u)
            {
                return false;
            }

            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            {
                ReportInteraction(InteractionResult.Unavailable, InventoryResult.InvalidItem, default);
                return false;
            }

            var interactable = identity.GetComponent<IInteractable>();
            if (interactable == null)
            {
                return false;
            }

            // 자기 자신이나 다른 플레이어를 대상으로 위조하는 요청을 막는다
            if (identity == netIdentity)
            {
                return false;
            }

            if (interactable is DroppedItem dropped)
            {
                WeaponCombatSnapshot equipment = dropped.Equipment;
                InteractionResult eligibility = dropped.ServerEvaluate(netId);
                InventoryResult stored = InventoryResult.InvalidItem;
                if (eligibility == InteractionResult.Success) { dropped.ServerTryPickup(netId, out stored); }
                ReportInteraction(eligibility, stored, equipment);
                return eligibility == InteractionResult.Success && stored == InventoryResult.Success;
            }
            if (interactable is ShopInteractionPoint shop)
            {
                InteractionResult eligibility = shop.ServerEvaluate(netId);
                if (eligibility != InteractionResult.Success)
                {
                    ReportInteraction(eligibility, InventoryResult.InvalidItem, default);
                    return false;
                }
            }
            interactable.Interact(netId);
            return true;
        }

        [Server]
        private void ReportInteraction(InteractionResult result, InventoryResult inventory, WeaponCombatSnapshot equipment)
        {
            if (connectionToClient != null) { TargetInteractionCompleted(connectionToClient, result, inventory, equipment); }
        }

        [TargetRpc]
        private void TargetInteractionCompleted(NetworkConnectionToClient target, InteractionResult result,
            InventoryResult inventory, WeaponCombatSnapshot equipment)
        { InteractionCompleted?.Invoke(result, inventory, equipment); }
    }
}
