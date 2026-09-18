// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 보스 트라이 전실. 편성 제출을 모아 서버가 검증하고, 다 모이기 전에는 보스를 열지 않는다.
    /// 정식 런에서는 아무 일도 하지 않는다 - 전실은 두 경로가 함께 쓰는 공간이다
    /// </summary>
    public partial class BossTrialDirector : NetworkBehaviour, IBossTrialGate
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 이 화면이 말을 걸 전실. 편성 UI와 개발 패널이 쓴다 </summary>
        public static BossTrialDirector LocalInstance { get; private set; }

        /// <summary> 편성을 모으는 중인가. 전실 UI가 열려 있어야 하는 구간이다 </summary>
        public bool IsCollecting => _isCollecting;

        /// <summary> 제출을 마친 인원 </summary>
        public int SubmittedCount => _submittedCount;

        /// <summary> 제출해야 하는 인원 </summary>
        public int RequiredCount => _requiredCount;

        /// <summary> 편성 예산이 다 모여 보스가 열릴 때까지 막는다 </summary>
        public bool IsBossStartBlocked => (_isCollecting && (_submittedCount < _requiredCount));

        /// <summary> 다음 런을 보스 트라이로 시작할지. 로비가 이 값을 보고 모드를 정한다 </summary>
        public bool IsTrialRequested => _isTrialRequested;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 모으는 상태나 제출 인원이 바뀌었다 </summary>
        public event Action<BossTrialCollectionState> CollectionChanged;

        /// <summary> 내 제출의 판정이 돌아왔다 </summary>
        public event Action<BossTrialLoadoutResult> SubmissionResolved;

        /// <summary> 다음 런을 보스 트라이로 시작할지가 바뀌었다 </summary>
        public event Action<bool> TrialModeChanged;

        /****************************************
        *              Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            LocalInstance = this;
            NotifyCollectionChanged();
        }

        public override void OnStopClient()
        {
            if (ReferenceEquals(LocalInstance, this))
            {
                LocalInstance = null;
            }
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnCollectingChanged(bool oldValue, bool newValue)
        {
            NotifyCollectionChanged();
        }

        private void OnSubmittedCountChanged(int oldValue, int newValue)
        {
            NotifyCollectionChanged();
        }

        private void OnRequiredCountChanged(int oldValue, int newValue)
        {
            NotifyCollectionChanged();
        }

        private void OnTrialRequestedChanged(bool oldValue, bool newValue)
        {
            ObserverDispatch.Invoke(TrialModeChanged, newValue, this,
                nameof(BossTrialDirector), "트라이 모드 요청");
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 늦게 열린 화면이 현재 값을 한 번 더 받는다 </summary>
        public void RaiseCurrentState()
        {
            NotifyCollectionChanged();
        }

        private void NotifyCollectionChanged()
        {
            var state = new BossTrialCollectionState
            {
                IsCollecting   = _isCollecting,
                SubmittedCount = _submittedCount,
                RequiredCount  = _requiredCount,
            };

            ObserverDispatch.Invoke(CollectionChanged, state, this,
                nameof(BossTrialDirector), "편성 모으기 상태");
        }

        [TargetRpc]
        private void TargetSubmissionResolved(NetworkConnectionToClient target, BossTrialLoadoutResult result)
        {
            ObserverDispatch.Invoke(SubmissionResolved, result, this,
                nameof(BossTrialDirector), "편성 제출 결과");
        }
    }
}
