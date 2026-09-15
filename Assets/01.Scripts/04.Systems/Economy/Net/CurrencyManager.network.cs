// 서드파티
using Mirror;

namespace Geumeum.Systems
{
    public partial class CurrencyManager
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnBalanceChanged))] private int  _balance         = 0;
        [SyncVar]                                      private uint _balanceRevision = 0u;
    }
}
