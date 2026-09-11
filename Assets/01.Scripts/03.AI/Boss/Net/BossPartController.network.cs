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

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            ApplyHitboxes(_snapshot);
            NotifyPartChanged(_snapshot);
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        [ClientRpc]
        private void RpcPlayPartBreak()
        {
            CombatFeedbackLocator.Current.PlayPartBreak();
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandlePartSnapshotChanged(BossPartSnapshot previous, BossPartSnapshot current)
        {
            ApplyHitboxes(current);
            NotifyPartChanged(current);
        }
    }
}
