// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class BarrierController
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnTransitionStateChanged))]
        private BarrierTransitionState _transitionState;

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnTransitionStateChanged(BarrierTransitionState oldState,
            BarrierTransitionState newState)
        {
            NotifySnapshot(newState);
        }
    }
}
