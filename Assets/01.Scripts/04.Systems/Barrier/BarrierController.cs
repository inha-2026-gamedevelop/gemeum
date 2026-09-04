// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary> 결계 상태 머신. 1단계 — phase 전환 감지하고 반경 트윈 값을 커밋한다 </summary>
    public class BarrierController : NetworkBehaviour, IBarrierState
    {
        /****************************************
        *                Fields
        ****************************************/

        // "지금부터 몇 초 뒤까지, A에서 B로 변한다"는 스케줄 값 하나.
        // [SyncVar]가 클라이언트로 자동 복제해준다 — 우리가 전송 코드를 따로 안 짜도 된다 (2단계는 이게 다)
        [SyncVar]
        private BarrierTransitionState _transitionState;

        /****************************************
        *              Properties
        ****************************************/

        public bool    IsActive => false; // TODO — 다음 단계
        public Vector3 Center   => Vector3.zero; // TODO — 다음 단계

        // 실제 값은 저장해두지 않고, 스케줄(_transitionState)과 "지금 몇 시냐"를 넣어서 그때그때 계산한다
        public float Radius => BarrierJudge.GetRadius(_transitionState, NetworkTime.time);

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()  => BarrierStateLocator.Register(this);
        private void OnDisable() => BarrierStateLocator.Unregister(this);

        /****************************************
        *              Server Callbacks
        ****************************************/
        // 이 메서드는 서버 역할을 하는 프로세스에서만 실행된다 (호스트 1명)
        public override void OnStartServer()
        {
            // 이벤트 수신 등록 — "앞으로 phase가 바뀌면 나 불러줘"
            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);

            // 등록은 지금부터의 이벤트만 받는다. 구독 전에 이미 정해져 있던 phase는
            // 이벤트로 안 오므로, 지금 phase를 직접 한 번 처리해서 놓친 과거를 메꾼다
            if (RunManager.ServerInstance != null)
            {
                ServerCommitPhase(RunManager.ServerInstance.Phase);
            }

            
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public bool IsOutside(Vector3 worldPosition) => false; // TODO — 다음 단계

        public float GetHealingMultiplier(Vector3 worldPosition) => 1f; // TODO — 다음 단계

        /****************************************
        *                Methods
        ****************************************/

        // 이벤트 처리 — 진짜 이벤트로 오든, 위에서 수동으로 부르든 여기로 모인다
        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            ServerCommitPhase(evt.Current);
        }

        [Server]
        private void ServerCommitPhase(RunPhase phase)
        {
            // 1) config 조회 — GameDB.Barrier가 config 테이블, TryGetPhase가 WHERE절 역할
            bool hasEntry = GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry entry);

            // 2) 목표 반경 계산
            float targetRadius = hasEntry ? (entry.RadiusRatio * GameDB.Barrier.MaxRadius) : 0f;

            // 3) "지금부터 목표까지, N초짜리 트윈" 하나로 저장 — 직전 목표값에서 이어서 시작한다
            double now = NetworkTime.time;
            _transitionState = new BarrierTransitionState
            {
                FromRadius      = _transitionState.TargetRadius,
                TargetRadius    = targetRadius,
                StartServerTime = now,
                EndServerTime   = now + GameDB.Barrier.ShrinkDuration,
                Revision        = _transitionState.Revision + 1,
            };
        }
    }
}
