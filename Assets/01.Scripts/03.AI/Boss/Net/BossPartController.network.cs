// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    public sealed partial class BossPartController
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(HandlePartSnapshotChanged))]
        private BossPartSnapshot _snapshot;
    }
}
