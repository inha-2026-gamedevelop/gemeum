// 서드파티
using Mirror;

namespace Geumeum.Systems
{
    public partial class ShopInteractionPoint
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private bool _isOpen;

        /****************************************
        *               TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetOpenShop(NetworkConnection target, uint shopNetId)
        {
            OpenRequested?.Invoke(shopNetId);
        }
    }
}
