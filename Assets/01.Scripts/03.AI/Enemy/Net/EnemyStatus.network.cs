// 서드파티
using Mirror;

namespace Geumeum.AI
{
    public partial class EnemyStatus
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthSynced))] private float _health;
    }
}
