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
    /// <summary> 현재 원과 경고 때 확정한 목표 원을 동기화하고 결계 밖 피해를 판정한다 </summary>
    public partial class BarrierController : NetworkBehaviour, IBarrierTransitionSource
    {
        /****************************************
        *                Constants
        ****************************************/

        // AuthorityTickScheduler 등록 순서. ability(300) 뒤, status 회복(400) 전에 결계 판정이 확정되게 한다.
        // Constants.Network는 01.Network(다른 파트 소유)라 팀장 승인 전까지는 이 파일 안에 로컬로 둔다
        private const int TICK_PRIORITY_BARRIER = 350;

        /****************************************
        *                Fields
        ****************************************/

        // 결계 밖 피해를 1초에 한 번만 적용한다. 다음 예정 시각을 now+interval로 재예약해
        // 호스트 hitch 뒤에도 밀린 피해를 한 프레임에 몰아넣지 않는다
        private double _nextDamageGameplayTime = 0.0;
        private Coroutine _schedulerWaitRoutine;
        private BarrierTransitionState _lastNotifiedState;
        private bool _hasNotifiedState = false;
        private bool _hasPendingActivation;
        private RunPhase _pendingActivationPhase = RunPhase.Lobby;
        private bool _hasReportedInitializationFailure;

        private readonly List<Vector3> _candidateBuffer = new List<Vector3>();
        private readonly List<Vector3> _fallbackBuffer = new List<Vector3>();
        private readonly List<Vector3> _landmarkBuffer = new List<Vector3>();
        private NavMeshPath _navigationPath;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsActive => _transitionState.IsActive;

        public Vector3 Center => BarrierJudge.GetCenter(_transitionState, CurrentFieldTime);

        public float Radius => BarrierJudge.GetRadius(_transitionState, CurrentFieldTime);

        public BarrierTransitionState Snapshot => _transitionState;

        private double CurrentFieldTime => RunTimeLocator.Current.FieldElapsedSeconds;

        public event Action<BarrierTransitionState> SnapshotChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            BarrierStateLocator.Register(this);
        }

        private void OnDisable()
        {
            BarrierStateLocator.Unregister(this);
        }

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
        *           Interface Members
        ****************************************/

        public bool IsOutside(Vector3 worldPosition)
        {
            if (!IsActive)
            {
                return false;
            }

            return BarrierJudge.IsOutside(worldPosition, _transitionState, CurrentFieldTime);
        }

        public float GetHealingMultiplier(Vector3 worldPosition)
        {
            if (!IsActive)
            {
                return 1f;
            }

            if (!IsOutside(worldPosition))
            {
                return 1f;
            }

            if (!GameDB.Barrier.TryGetPhase(_transitionState.CurrentPhase,
                    out BarrierPhaseSO.PhaseEntry entry))
            {
                return 1f;
            }

            // 봉쇄가 쇠약보다 우선한다, 둘 다 켜져 있으면 0
            if (entry.BlocksHealing)
            {
                return 0f;
            }

            if (entry.AppliesWeakened)
            {
                return GameDB.Barrier.WeakenedHealingMultiplier;
            }

            return 1f;
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

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.Register(TICK_PRIORITY_BARRIER, ServerTick);
            _schedulerWaitRoutine = null;
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

        private void CancelPendingActivation()
        {
            _hasPendingActivation             = false;
            _pendingActivationPhase           = RunPhase.Lobby;
            _hasReportedInitializationFailure = false;
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
                // Unity native object는 MonoBehaviour field initializer/constructor에서 만들 수 없다.
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

        private void CommitSnapshot(in BarrierTransitionState snapshot, bool publishPrepared)
        {
            _transitionState = snapshot;
            NotifySnapshot(snapshot);

            if (publishPrepared && GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Publish(new BarrierTransitionPreparedEvent(snapshot));
            }
        }

        private void NotifySnapshot(in BarrierTransitionState snapshot)
        {
            if (_hasNotifiedState && _lastNotifiedState.Equals(snapshot))
            {
                return;
            }

            _lastNotifiedState = snapshot;
            _hasNotifiedState  = true;
            ObserverDispatch.Invoke(SnapshotChanged, snapshot, this,
                nameof(BarrierController), nameof(SnapshotChanged));
        }

        private static uint NextRevision(uint current)
        {
            uint next = current + 1u;
            return (next == 0u) ? 1u : next;
        }

        private static bool IsBarrierPhase(RunPhase phase)
        {
            return (phase == RunPhase.Farming) || (phase == RunPhase.Shrink1) ||
                (phase == RunPhase.Shrink2) || (phase == RunPhase.Shrink3);
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

        // Player.prefab(다른 파트 소유)을 건드리지 않으려고, 접속자 오브젝트에 서버가 직접 어댑터를 붙인다.
        // BarrierTargetAdapter는 NetworkBehaviour가 아닌 순수 MonoBehaviour라 AddComponent가 스폰 동기화에 관여하지 않는다.
        // 1초 피해 tick 안에서만 호출해 매 프레임 GetComponent를 반복하지 않는다
        [Server]
        private void EnsureTargetAdaptersRegistered()
        {
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkIdentity identity = pair.Value?.identity;
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

#if UNITY_EDITOR
        /****************************************
        *           Debug (Editor Only)
        ****************************************/

        // RunManager의 타임라인을 안 건드리고 phase를 바로 다음 단계로 넘긴다.
        // BarrierPhaseSO의 StartSeconds/EndSeconds를 테스트용으로 고치면 RunPhaseTimeline.Validate가
        // 실패해 서버가 부트 검증에서 튕긴다, 그 함정을 피하려고 만든 테스트 전용 경로다
        // BarrierTestHarness가 자동 전환 코루틴에서 직접 호출한다. public이지만 UNITY_EDITOR 밖에서는 존재 자체가 없다
        [ContextMenu("Debug: Force Next Phase (Test)")]
        public void DebugForceNextPhase()
        {
            RunPhase current = _transitionState.CurrentPhase;
            RunPhase next    = DebugGetNextPhase(current);
            Debug.Log($"[BarrierController] 테스트로 phase를 강제 전환한다: {current} -> {next}", this);
            ServerActivatePhase(next, true);
        }

        // 랜드마크 배선·NavMesh 없이 원을 바로 세운다. Dev_Barrier처럼 맵이 갖춰지지 않은 씬에서
        // 결계 '모양'만 눈으로 볼 때 쓴다 — 정상 경로(ServerInitializeCircle)는 BarrierMapBinding과
        // 베이크된 NavMesh를 요구하므로, 그것들이 없으면 활성화가 pending으로 남아 반경이 0에 머문다.
        // 판정 자체는 평소와 같은 SyncVar를 타므로 결계 밖 데미지·치유 제한은 그대로 동작한다.
        // 검증을 건너뛰는 경로이므로 이걸로 확인한 것은 결계 배치의 정합성 근거가 되지 않는다
        [ContextMenu("Debug: Force Circle Without Map (Test)")]
        public void DebugForceCircleWithoutMap()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[BarrierController] 서버(호스트)가 아니라 강제 원을 세울 수 없다", this);
                return;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[BarrierController] GameDB가 아직 초기화되지 않았다", this);
                return;
            }

            RunPhase phase = IsBarrierPhase(_transitionState.CurrentPhase)
                ? _transitionState.CurrentPhase
                : RunPhase.Farming;

            if (!GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry entry))
            {
                Debug.LogError($"[BarrierController] {phase} 결계 설정을 찾지 못했다", this);
                return;
            }

            Vector3 center = DebugResolveFallbackCenter();
            float   radius = entry.RadiusRatio * GameDB.Barrier.MaxRadius;
            double  now    = CurrentFieldTime;

            BarrierTransitionState forced = new BarrierTransitionState
            {
                FromCenter          = center,
                TargetCenter        = center,
                FromRadius          = radius,
                TargetRadius        = radius,
                WarningGameplayTime = now,
                StartGameplayTime   = now,
                EndGameplayTime     = now,
                Revision            = NextRevision(_transitionState.Revision),
                CurrentPhase        = phase,
                TargetPhase         = phase,
                IsActive            = true,
            };

            CancelPendingActivation();
            CommitSnapshot(forced, true);

            Debug.Log($"[BarrierController] 검증을 건너뛰고 결계를 세웠다: phase={phase}, " +
                $"중심={center}, 반경={radius:F1}m — 맵 검증을 통과한 배치가 아니다", this);
        }

        // BarrierCenter 태그가 있으면 그 자리를, 없으면 이 오브젝트 자리를 중심으로 쓴다
        private Vector3 DebugResolveFallbackCenter()
        {
            GameObject spot = GameObject.FindGameObjectWithTag(Constants.Tag.BARRIER_CENTER);
            return (spot == null) ? transform.position : spot.transform.position;
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

        /****************************************
        *          Gizmos (Editor Only)
        ****************************************/

        // 결계 벽의 실제 표현은 BarrierVisual(05.Art)이 담당한다. 여기서는 Scene 뷰 확인용
        // 와이어 원만 그린다 — 예전에는 Game 뷰에도 반투명 기둥을 띄웠으나, 표현 계층이
        // 생긴 뒤로 같은 자리에 두 개가 겹쳐 그려져서 제거했다

        // 런 시계는 플레이 중 RunManager가 공급하므로 에디트 모드에서는 그리지 않는다
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Vector3 center = Center;
            float   radius = Radius;

            if (radius <= 0f)
            {
                return;
            }

            Gizmos.color = IsActive ? new Color(1f, 0.25f, 0.25f, 0.9f) : Color.gray;
            BarrierGizmoUtility.DrawWireCircleXZ(center, radius);

            // 목표 반경도 옅게 같이 그려서 축소가 어디까지 갈지 미리 본다
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.25f);
            BarrierGizmoUtility.DrawWireCircleXZ(_transitionState.TargetCenter, _transitionState.TargetRadius);
        }

#endif
    }
}
