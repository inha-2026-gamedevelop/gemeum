// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    public sealed partial class BossSoulController
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(HandleSoulSnapshotChanged))]
        private BossSoulSnapshot _snapshot;

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            ApplyHitboxes(_snapshot);
            NotifySoulChanged(_snapshot);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandleSoulSnapshotChanged(BossSoulSnapshot previous, BossSoulSnapshot current)
        {
            ApplyHitboxes(current);
            NotifySoulChanged(current);
        }
    }
}
