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

        [SyncVar]
        private BarrierTransitionState _transitionState;

        private RunPhase _currentPhase = RunPhase.Lobby; // 서버 전용, 디버그 메뉴가 다음 phase를 고를 때 씀

        /****************************************
        *              Properties
        ****************************************/

        public bool    IsActive => false; // TODO — 다음 단계
        public Vector3 Center   => Vector3.zero; // TODO — 다음 단계

        public float Radius => BarrierJudge.GetRadius(_transitionState, NetworkTime.time);

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()  => BarrierStateLocator.Register(this);
        private void OnDisable() => BarrierStateLocator.Unregister(this);

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);

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

        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            ServerCommitPhase(evt.Current);
        }

        [Server]
        private void ServerCommitPhase(RunPhase phase)
        {
            bool  hasEntry    = GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry entry);
            float targetRadius = hasEntry ? (entry.RadiusRatio * GameDB.Barrier.MaxRadius) : 0f;

            double now = NetworkTime.time;
            _transitionState = new BarrierTransitionState
            {
                FromRadius      = _transitionState.TargetRadius,
                TargetRadius    = targetRadius,
                StartServerTime = now,
                EndServerTime   = now + GameDB.Barrier.ShrinkDuration,
                Revision        = _transitionState.Revision + 1,
            };

            _currentPhase = phase;
        }

#if UNITY_EDITOR
        /****************************************
        *           Debug (Editor Only)
        ****************************************/

        // RunManager의 타임라인을 안 건드리고 phase를 바로 다음 단계로 넘긴다.
        // BarrierPhaseSO의 StartSeconds/EndSeconds를 테스트용으로 고치면 RunPhaseTimeline.Validate가
        // 실패해 서버가 부트 검증에서 튕긴다 — 그 함정을 피하려고 만든 테스트 전용 경로다
        [ContextMenu("Debug: Force Next Phase (Test)")]
        private void DebugForceNextPhase()
        {
            RunPhase next = DebugGetNextPhase(_currentPhase);
            Debug.Log($"[BarrierController] 테스트로 phase를 강제 전환한다: {_currentPhase} -> {next}", this);
            ServerCommitPhase(next);
        }

        private static RunPhase DebugGetNextPhase(RunPhase current)
        {
            switch (current)
            {
                case RunPhase.Lobby:   return RunPhase.Farming;
                case RunPhase.Farming: return RunPhase.Shrink1;
                case RunPhase.Shrink1: return RunPhase.Shrink2;
                case RunPhase.Shrink2: return RunPhase.Shrink3;
                case RunPhase.Shrink3: return RunPhase.Boss;
                default:               return RunPhase.Farming;
            }
        }
#endif
    }
}
