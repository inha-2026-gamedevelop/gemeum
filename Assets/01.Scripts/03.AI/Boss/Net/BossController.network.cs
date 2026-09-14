// Unity
using UnityEngine;

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

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            BossPhaseSourceLocator.Register(this);
            _telegraphCues.Callback += HandleTelegraphListChanged;
            for (int i = 0; i < _telegraphCues.Count; ++i)
            {
                NotifyTelegraphChanged(_telegraphCues[i]);
            }

            NotifyPhaseChanged(_phaseSnapshot);
        }

        public override void OnStopClient()
        {
            BossPhaseSourceLocator.Unregister(this);
            _telegraphCues.Callback -= HandleTelegraphListChanged;
            BossTelegraphCue cue = _telegraphCue;
            cue.IsActive = false;
            NotifyTelegraphChanged(cue);
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        // 판정과 별개로 각 클라이언트가 그로기 진입 피드백을 재생한다.
        [ClientRpc]
        private void RpcGroggyEnter()
        {
            CombatFeedbackLocator.Current.PlayGroggyEnter();
        }

        // 근접 부채꼴 패턴 임팩트 VFX 신호(러프, 이슈 검증용) — 표현 전용 컴포넌트가 구독한다
        [ClientRpc]
        private void RpcMeleeArcImpact(Vector3 origin, Vector3 forward)
        {
            MeleeArcImpact?.Invoke(origin, forward);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandleTelegraphChanged(BossTelegraphCue previous, BossTelegraphCue current)
        {
            NotifyTelegraphChanged(current);
        }

        private void HandlePhaseSnapshotChanged(BossPhaseSnapshot previous,
            BossPhaseSnapshot current)
        {
            NotifyPhaseChanged(current);
        }

        private void HandleEnrageWarningChanged(bool previous, bool current)
        {
            ObserverDispatch.Invoke(EnrageWarningChanged, current, this,
                nameof(BossController), nameof(EnrageWarningChanged));
        }

        private void HandleEnragedChanged(bool previous, bool current)
        {
            ObserverDispatch.Invoke(EnragedChanged, current, this,
                nameof(BossController), nameof(EnragedChanged));
        }
    }
}
