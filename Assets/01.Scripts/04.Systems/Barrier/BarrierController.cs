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

        // AuthorityTickScheduler 등록 순서. ability(300) 뒤, status 회복(400) 전에 결계 판정이 확정되게 한다
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

        private void CancelPendingActivation()
        {
            _hasPendingActivation             = false;
            _pendingActivationPhase           = RunPhase.Lobby;
            _hasReportedInitializationFailure = false;
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

#if UNITY_EDITOR
        /****************************************
        *           Debug (Editor Only)
        ****************************************/

        // RunManager의 타임라인을 안 건드리고 phase를 바로 다음 단계로 넘긴다
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

        // 랜드마크 배선, NavMesh 없이 원을 바로 세운다. Dev_Barrier처럼 맵이 갖춰지지 않은 씬에서
        // 결계 '모양'만 눈으로 볼 때 쓴다 - 정상 경로(ServerInitializeCircle)는 BarrierMapBinding과
        // 베이크된 NavMesh를 요구하므로, 그것들이 없으면 활성화가 pending으로 남아 반경이 0에 머문다
        // 판정 자체는 평소와 같은 SyncVar를 타므로 결계 밖 데미지, 치유 제한은 그대로 동작한다
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

        // 맵 배선 없이 다음 단계로 '서서히' 줄인다. Force Circle Without Map은 원을 즉시 갈아 끼우고
        // Force Next Phase는 반경을 아예 건드리지 않아, 둘 다 축소 애니메이션을 볼 수 없다
        // 정상 경로(ServerPrepareTarget)는 BarrierMapBinding과 NavMesh를 요구하므로 Dev 씬에서는 진입하지 못한다
        // 여기서는 랜드마크 검증과 NavMesh 검증만 건너뛰고, 중심을 고르는 방식과 보간 구간은 정상 경로와 같게 둔다
        // 검증을 건너뛰므로 이걸로 확인한 것은 결계 배치의 정합성 근거가 되지 않는다
        [ContextMenu("Debug: Force Next Phase Animated (Test)")]
        public void DebugForceNextPhaseAnimated()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[BarrierController] 서버(호스트)가 아니라 강제 전환할 수 없다", this);
                return;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[BarrierController] GameDB가 아직 초기화되지 않았다", this);
                return;
            }

            RunPhase current = IsBarrierPhase(_transitionState.CurrentPhase)
                ? _transitionState.CurrentPhase
                : RunPhase.Farming;

            RunPhase next = DebugGetNextPhase(current);
            if (!IsBarrierPhase(next))
            {
                Debug.LogWarning($"[BarrierController] {current} 다음은 결계 단계가 아니라 축소할 것이 없다: {next}", this);
                return;
            }

            if (!GameDB.Barrier.TryGetPhase(next, out BarrierPhaseSO.PhaseEntry entry))
            {
                Debug.LogError($"[BarrierController] {next} 결계 설정을 찾지 못했다", this);
                return;
            }

            double  now        = CurrentFieldTime;
            Vector3 fromCenter = BarrierJudge.GetCenter(_transitionState, now);
            float   fromRadius = BarrierJudge.GetRadius(_transitionState, now);

            // 아직 한 번도 원이 서지 않았으면 현재 단계의 원을 출발점으로 삼는다
            if (fromRadius <= 0f)
            {
                if (!GameDB.Barrier.TryGetPhase(current, out BarrierPhaseSO.PhaseEntry currentEntry))
                {
                    Debug.LogError($"[BarrierController] {current} 결계 설정을 찾지 못했다", this);
                    return;
                }

                fromCenter = DebugResolveFallbackCenter();
                fromRadius = currentEntry.RadiusRatio * GameDB.Barrier.MaxRadius;
            }

            float targetRadius = entry.RadiusRatio * GameDB.Barrier.MaxRadius;

            // 정상 경로의 BuildRandomCandidates와 같은 규칙이다. 새 원이 이전 원 안에 온전히 들어간다
            Vector2 offset       = UnityEngine.Random.insideUnitCircle * Mathf.Max(fromRadius - targetRadius, 0f);
            Vector3 targetCenter = fromCenter + new Vector3(offset.x, 0f, offset.y);

            double startTime = now + GameDB.Barrier.WarningLeadTime;

            BarrierTransitionState forced = new BarrierTransitionState
            {
                FromCenter          = fromCenter,
                TargetCenter        = targetCenter,
                FromRadius          = fromRadius,
                TargetRadius        = targetRadius,
                WarningGameplayTime = now,
                StartGameplayTime   = startTime,
                EndGameplayTime     = startTime + GameDB.Barrier.ShrinkDuration,
                Revision            = NextRevision(_transitionState.Revision),
                CurrentPhase        = next,
                TargetPhase         = next,
                IsActive            = true,
            };

            CancelPendingActivation();
            CommitSnapshot(forced, true);

            Debug.Log($"[BarrierController] 검증을 건너뛰고 축소를 예약했다: {current} -> {next}, " +
                $"반경 {fromRadius:F1}m -> {targetRadius:F1}m, " +
                $"예고 {GameDB.Barrier.WarningLeadTime:F0}초 뒤 {GameDB.Barrier.ShrinkDuration:F0}초 동안 줄어든다 " +
                "- 맵 검증을 통과한 배치가 아니다", this);
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
        // 와이어 원만 그린다 - 예전에는 Game 뷰에도 반투명 기둥을 띄웠으나, 표현 계층이
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
