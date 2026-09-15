// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    public partial class BossController
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private int _currentPhaseIndex = 1;
        [SyncVar(hook = nameof(HandleTelegraphChanged))]
        private BossTelegraphCue _telegraphCue;
        private readonly SyncList<BossTelegraphCue> _telegraphCues =
            new SyncList<BossTelegraphCue>();
        [SyncVar(hook = nameof(HandlePhaseSnapshotChanged))]
        private BossPhaseSnapshot _phaseSnapshot;
        [SyncVar(hook = nameof(HandleEnrageWarningChanged))]
        private bool _isEnrageWarning = false;
        [SyncVar(hook = nameof(HandleEnragedChanged))]
        private bool _isEnraged = false;
    }
}
