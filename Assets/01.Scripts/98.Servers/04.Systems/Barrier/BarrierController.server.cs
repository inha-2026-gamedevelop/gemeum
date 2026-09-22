// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    public partial class BarrierController
    {
        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            CancelPendingActivation();
            _nextDamageGameplayTime = 0.0;
            CommitSnapshot(default, false);

            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Register(TICK_PRIORITY_BARRIER, ServerTick);
            }
            else
            {
                _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
            }

            if (RunManager.ServerInstance != null)
            {
                ServerActivatePhase(RunManager.ServerInstance.Phase, false);
            }
        }

        public override void OnStopServer()
        {
            GameEventBus bus = GameEventBus.Instance;
            if (bus != null)
            {
                bus.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
                bus.Unsubscribe<RunResetEvent>(HandleRunReset);
            }

            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
            }

            CancelPendingActivation();
            _nextDamageGameplayTime = 0.0;
            CommitSnapshot(default, false);
        }

        /****************************************
        *                Methods
        ****************************************/

        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            ServerActivatePhase(evt.Current, false);
        }

        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            CancelPendingActivation();
            _nextDamageGameplayTime = 0.0;
            CommitSnapshot(default, false);
        }

        [Server]
        private void ServerTick(float deltaTime)
        {
            if (_hasPendingActivation && !ServerTryCompletePendingActivation())
            {
                return;
            }

            ServerPrepareUpcomingTarget();
            ServerTickDamage(deltaTime);
        }

        [Server]
        private void ServerPrepareUpcomingTarget()
        {
            RunPhase current = _transitionState.CurrentPhase;
            if (!IsBarrierPhase(current) ||
                !TryGetNextBarrierPhase(current, out BarrierPhaseSO.PhaseEntry next))
            {
                return;
            }

            if ((_transitionState.TargetPhase == next.Phase) &&
                (_transitionState.Revision != 0u))
            {
                return;
            }

            double now = CurrentFieldTime;
            if (now < (next.StartSeconds - GameDB.Barrier.WarningLeadTime))
            {
                return;
            }

            ServerPrepareTarget(next, next.StartSeconds);
        }

        [Server]
        private void ServerActivatePhase(RunPhase phase, bool forceImmediate)
        {
            bool active = IsBarrierPhase(phase);
            if (!active)
            {
                CancelPendingActivation();
                BarrierTransitionState inactive = _transitionState;
                inactive.CurrentPhase = phase;
                inactive.IsActive     = false;
                CommitSnapshot(inactive, false);
                return;
            }

            if (!GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry entry))
            {
                Debug.LogError($"[BarrierController] {phase} 결계 설정을 찾지 못했다", this);
                return;
            }

            double now = CurrentFieldTime;
            if (_transitionState.Revision == 0u)
            {
                QueuePendingActivation(phase);
                ServerTryCompletePendingActivation();
                return;
            }
            else if (_transitionState.TargetPhase != phase)
            {
                double startTime = forceImmediate ? now : Math.Max(entry.StartSeconds, now);
                if (!ServerPrepareTarget(entry, startTime))
                {
                    BarrierTransitionState failed = _transitionState;
                    failed.CurrentPhase = phase;
                    failed.IsActive     = true;
                    CommitSnapshot(failed, false);
                    return;
                }
            }

            BarrierTransitionState activated = _transitionState;
            activated.CurrentPhase = phase;
            activated.IsActive     = true;
            CommitSnapshot(activated, false);
        }

        [Server]
        private bool ServerTryCompletePendingActivation()
        {
            if (!_hasPendingActivation)
            {
                return _transitionState.Revision != 0u;
            }

            if (!GameDB.IsInitialized || !IsFieldSceneReady())
            {
                return false;
            }

            if (!GameDB.Barrier.TryGetPhase(_pendingActivationPhase,
                    out BarrierPhaseSO.PhaseEntry entry))
            {
                if (!_hasReportedInitializationFailure)
                {
                    Debug.LogError($"[BarrierController] {_pendingActivationPhase} 결계 설정을 찾지 못했다", this);
                    _hasReportedInitializationFailure = true;
                }

                return false;
            }

            bool reportFailure = !_hasReportedInitializationFailure;
            if (!ServerInitializeCircle(entry, CurrentFieldTime, reportFailure))
            {
                _hasReportedInitializationFailure = true;
                return false;
            }

            CancelPendingActivation();
            return true;
        }

        [Server]
        private bool ServerInitializeCircle(in BarrierPhaseSO.PhaseEntry entry, double now,
            bool reportFailure)
        {
            BarrierMapBinding binding = BarrierMapBindingLocator.Current;
            if (!TryPrepareMapInputs(binding, entry, out BarrierLandmarkKind missing))
            {
                if (reportFailure)
                {
                    Debug.LogError($"[BarrierController] 초기 결계 랜드마크 배선이 유효하지 않다: missing={missing}", this);
                }

                return false;
            }

            Vector3 center = binding.InitialCenter;
            float   radius = entry.RadiusRatio * GameDB.Barrier.MaxRadius;
            if (!ContainsAllLandmarks(center, radius) || !ValidateNavigation(center))
            {
                if (reportFailure)
                {
                    Debug.LogError("[BarrierController] 초기 결계 중심이 랜드마크 또는 NavMesh 경로 조건을 만족하지 않는다", this);
                }

                return false;
            }

            BarrierTransitionState initial = new BarrierTransitionState
            {
                FromCenter         = center,
                TargetCenter       = center,
                FromRadius         = radius,
                TargetRadius       = radius,
                WarningGameplayTime = now,
                StartGameplayTime  = now,
                EndGameplayTime    = now,
                Revision           = NextRevision(_transitionState.Revision),
                CurrentPhase       = entry.Phase,
                TargetPhase        = entry.Phase,
                IsActive           = true,
            };
            CommitSnapshot(initial, true);
            return true;
        }

        [Server]
        private bool ServerPrepareTarget(in BarrierPhaseSO.PhaseEntry entry,
            double startGameplayTime)
        {
            BarrierMapBinding binding = BarrierMapBindingLocator.Current;
            if (!TryPrepareMapInputs(binding, entry, out BarrierLandmarkKind missing))
            {
                Debug.LogError($"[BarrierController] {entry.Phase} 목표 랜드마크 배선이 유효하지 않다: missing={missing}", this);
                return false;
            }

            double  now            = CurrentFieldTime;
            Vector3 previousCenter = BarrierJudge.GetCenter(_transitionState, now);
            float   previousRadius = BarrierJudge.GetRadius(_transitionState, now);
            float   targetRadius   = entry.RadiusRatio * GameDB.Barrier.MaxRadius;

            BuildRandomCandidates(previousCenter, previousRadius, targetRadius);
            binding.CollectFallbackCenters(entry.Phase, _fallbackBuffer);

            if (!BarrierTargetSelector.TrySelect(previousCenter, previousRadius,
                    targetRadius, _candidateBuffer, _fallbackBuffer, _landmarkBuffer,
                    GameDB.Barrier.FallbackSegmentSamples, ValidateNavigation,
                    out Vector3 targetCenter, out bool usedFallback))
            {
                Debug.LogError($"[BarrierController] {entry.Phase} 목표 원 후보와 대체 후보가 모두 유효하지 않다", this);
                return false;
            }

            BarrierTransitionState prepared = new BarrierTransitionState
            {
                FromCenter          = previousCenter,
                TargetCenter        = targetCenter,
                FromRadius          = previousRadius,
                TargetRadius        = targetRadius,
                WarningGameplayTime = now,
                StartGameplayTime   = startGameplayTime,
                EndGameplayTime     = startGameplayTime + GameDB.Barrier.ShrinkDuration,
                Revision            = NextRevision(_transitionState.Revision),
                CurrentPhase        = _transitionState.CurrentPhase,
                TargetPhase         = entry.Phase,
                IsActive            = _transitionState.IsActive,
            };

            CommitSnapshot(prepared, true);
            Debug.Log($"[BarrierController] {entry.Phase} 목표 원 확정: revision={prepared.Revision}, fallback={usedFallback}, center={targetCenter}, radius={targetRadius:F1}", this);
            return true;
        }

        // AuthorityTickScheduler가 물리 스텝마다 호출한다. 최대 3명만 순회하고, 밖에 있는 대상에게만
        // 1초 간격으로 DamageInfo.FromEnvironment를 적용한다. action 무적을 관통시키는 flag는
        // PlayerStatus/DamageInfo(다른 파트 소유) 확장이 선행 조건이라 이번 단계에서는 걸지 않는다
        [Server]
        private void ServerTickDamage(float deltaTime)
        {
            if (!IsActive)
            {
                return;
            }

            double now = CurrentFieldTime;
            if (now < _nextDamageGameplayTime)
            {
                return;
            }

            _nextDamageGameplayTime = now + GameDB.Barrier.DamageTickInterval;

            EnsureTargetAdaptersRegistered();

            if (!GameDB.Barrier.TryGetPhase(_transitionState.CurrentPhase,
                    out BarrierPhaseSO.PhaseEntry entry))
            {
                return;
            }

            float damageAmount = entry.DamagePerSecond * GameDB.Barrier.DamageTickInterval;
            if (damageAmount <= 0f)
            {
                return;
            }

            IReadOnlyList<BarrierTargetAdapter> adapters = BarrierTargetRegistry.Adapters;
            for (int i = 0; i < adapters.Count; ++i)
            {
                BarrierTargetAdapter adapter = adapters[i];
                IDamageable          target  = adapter.Damageable;

                if ((target == null) || target.IsDead)
                {
                    continue;
                }

                if (!BarrierJudge.IsOutside(adapter.transform.position, _transitionState, now))
                {
                    continue;
                }

                target.TakeDamage(DamageInfo.FromEnvironment(damageAmount));
            }
        }

        // Player.prefab(다른 파트 소유)을 건드리지 않으려고, 접속자 오브젝트에 서버가 직접 어댑터를 붙인다
        // BarrierTargetAdapter는 NetworkBehaviour가 아닌 순수 MonoBehaviour라 AddComponent가 스폰 동기화에 관여하지 않는다
        // 1초 피해 tick 안에서만 호출해 매 프레임 GetComponent를 반복하지 않는다
        [Server]
        private void EnsureTargetAdaptersRegistered()
        {
            foreach (var pair in Geumeum.Network.ParticipantDirectory.Actors)
            {
                NetworkIdentity identity = pair.Value;
                if (identity == null)
                {
                    continue;
                }

                if (identity.gameObject.GetComponent<BarrierTargetAdapter>() != null)
                {
                    continue;
                }

                identity.gameObject.AddComponent<BarrierTargetAdapter>();
            }
        }

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.Register(TICK_PRIORITY_BARRIER, ServerTick);
            _schedulerWaitRoutine = null;
        }

        private void QueuePendingActivation(RunPhase phase)
        {
            if (_hasPendingActivation && (_pendingActivationPhase == phase))
            {
                return;
            }

            _hasPendingActivation             = true;
            _pendingActivationPhase           = phase;
            _hasReportedInitializationFailure = false;
        }

        private static bool IsFieldSceneReady()
        {
            if (BarrierMapBindingLocator.Current == null)
            {
                return false;
            }

            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return (director == null) ||
                ((director.CurrentScene == RunSceneId.Field) &&
                 (director.State == RunTransitionState.Idle));
        }

        private bool TryPrepareMapInputs(BarrierMapBinding binding,
            in BarrierPhaseSO.PhaseEntry entry, out BarrierLandmarkKind missing)
        {
            missing = entry.RequiredLandmarks;
            _landmarkBuffer.Clear();
            _fallbackBuffer.Clear();

            if ((binding == null) || (entry.RequiredLandmarks == BarrierLandmarkKind.None))
            {
                return false;
            }

            return binding.TryCollectLandmarks(entry.RequiredLandmarks,
                _landmarkBuffer, out missing);
        }

        private void BuildRandomCandidates(Vector3 previousCenter, float previousRadius,
            float targetRadius)
        {
            _candidateBuffer.Clear();
            int   attempts  = Mathf.Max(GameDB.Barrier.TargetCandidateAttempts, 0);
            float maxOffset = Mathf.Max(previousRadius - targetRadius, 0f);

            for (int i = 0; i < attempts; ++i)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * maxOffset;
                _candidateBuffer.Add(previousCenter + new Vector3(offset.x, 0f, offset.y));
            }
        }

        private bool ValidateNavigation(Vector3 center)
        {
            if (_navigationPath == null)
            {
                // Unity native object는 MonoBehaviour field initializer/constructor에서 만들 수 없다
                _navigationPath = new NavMeshPath();
            }

            float sampleRadius = GameDB.Barrier.NavMeshSampleRadius;
            if (!NavMesh.SamplePosition(center, out NavMeshHit centerHit,
                    sampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            for (int i = 0; i < _landmarkBuffer.Count; ++i)
            {
                if (!NavMesh.SamplePosition(_landmarkBuffer[i], out NavMeshHit landmarkHit,
                        sampleRadius, NavMesh.AllAreas))
                {
                    return false;
                }

                _navigationPath.ClearCorners();
                if (!NavMesh.CalculatePath(centerHit.position, landmarkHit.position,
                        NavMesh.AllAreas, _navigationPath) ||
                    (_navigationPath.status != NavMeshPathStatus.PathComplete))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ContainsAllLandmarks(Vector3 center, float radius)
        {
            for (int i = 0; i < _landmarkBuffer.Count; ++i)
            {
                if (!BarrierTargetSelector.IsInside(_landmarkBuffer[i], center, radius))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetNextBarrierPhase(RunPhase current,
            out BarrierPhaseSO.PhaseEntry next)
        {
            IReadOnlyList<BarrierPhaseSO.PhaseEntry> phases = GameDB.Barrier.Phases;
            for (int i = 0; i < (phases.Count - 1); ++i)
            {
                if ((phases[i].Phase != current) || !IsBarrierPhase(phases[i + 1].Phase))
                {
                    continue;
                }

                next = phases[i + 1];
                return true;
            }

            next = default;
            return false;
        }
    }
}
