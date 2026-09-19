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

        // 그로기 저항은 서버가 타격마다 깎는다. 매 틱이 아니라 깎일 때만 바뀌므로 SyncVar로 충분하다.
        // 값 자체를 내리고 비율은 클라가 만든다 - 서버는 원본 하나만 보낸다
        [SyncVar] private float _groggyGauge = 0f;
    }
}
