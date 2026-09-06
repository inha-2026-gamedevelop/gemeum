// System
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 실행부. HP 비율로 페이즈 전환을 커밋하고 현재 페이즈에 Tick을 위임한다.
    /// 패턴 실행은 아직 없다 — BossPatternSO 확정 후 4단계에서 채운다 (docs/unity/BOSS_DESIGN.md 15장)
    /// </summary>
    [RequireComponent(typeof(BossStatus))]
    public class BossController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossStatus _status;

        private BossPhaseStateBase _currentPhaseState;
        private int                _currentPhaseIndex = 1;
        private Coroutine          _schedulerWaitRoutine;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _status.Initialize();
            _status.Died += HandleDied;

            _currentPhaseIndex = 1;
            _currentPhaseState = CreatePhaseState(_currentPhaseIndex);

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
                return;
            }

            // 씬 오브젝트 간 OnStartServer 호출 순서는 Mirror가 보장하지 않는다 —
            // 스케줄러가 아직 안 떴을 수 있으니, 뜰 때까지 기다렸다가 등록한다
            _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
        }

        public override void OnStopServer()
        {
            _status.Died -= HandleDied;

            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
        }

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
            _schedulerWaitRoutine = null;
        }

        // AuthorityTickScheduler가 물리 스텝마다 TICK_PRIORITY_AI 순서로 호출한다
        private void ServerTick(float deltaTime)
        {
            if (_status.IsDead)
            {
                return;
            }

            CommitPendingPhaseTransitions();
            _currentPhaseState.Tick(deltaTime);
        }

        /****************************************
        *              Server API
        ****************************************/

        [Server]
        private void HandleDied(uint killerNetId)
        {
            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
            }
        }

        /// <summary>
        /// HP 비율이 다음 EnterHealthRatio 이하로 내려간 페이즈를 전부 한 틱에 커밋한다.
        /// 오버킬은 클램프하지 않는다(BOSS_PATTERN_CONTRACT.md 8.1) — 한 방에 두 단계를 건너뛰면 그대로 반영된다.
        /// TODO: 진행 중이던 패턴 취소, 패링 창 닫기, TransitionLockDuration은 패턴 시스템(4단계)에서 채운다
        /// </summary>
        [Server]
        private void CommitPendingPhaseTransitions()
        {
            while ((GameDB.Boss.TryGetPhase(_currentPhaseIndex + 1, out BossPatternSO.PhaseEntry nextPhase))
                   && (_status.HealthRatio <= nextPhase.EnterHealthRatio))
            {
                _currentPhaseIndex = nextPhase.PhaseIndex;
                _currentPhaseState = CreatePhaseState(_currentPhaseIndex);
            }
        }

        [Server]
        private BossPhaseStateBase CreatePhaseState(int phaseIndex)
        {
            switch (phaseIndex)
            {
                case 1:
                    return new Phase1State(transform, _status);
                case 2:
                    return new Phase2State(transform, _status);
                default:
                    Debug.LogError($"[BossController] {phaseIndex} 페이즈의 State가 아직 없다", this);
                    return null;
            }
        }
    }
}
