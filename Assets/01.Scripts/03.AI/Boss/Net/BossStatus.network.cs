// 서드파티
using Mirror;

namespace Geumeum.AI
{
    public partial class BossStatus
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthSynced))] private float _health;
    }
}
