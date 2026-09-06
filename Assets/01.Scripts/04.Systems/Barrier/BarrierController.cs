// System
using System.Collections.Generic;

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
    /// <summary> 결계 상태 머신. 1단계 — phase 전환 감지, 반경 트윈 값 커밋, 결계 밖 1초 피해 </summary>
    public class BarrierController : NetworkBehaviour, IBarrierState
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

        [SyncVar]
        private BarrierTransitionState _transitionState;

        private RunPhase _currentPhase = RunPhase.Lobby; // 서버 전용, 디버그 메뉴가 다음 phase를 고를 때 씀

        // 결계 밖 피해를 1초에 한 번만 적용한다. 다음 예정 시각을 now+interval로 재예약해
        // 호스트 hitch 뒤에도 밀린 피해를 한 프레임에 몰아넣지 않는다
        private double _nextDamageServerTime = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsActive
        {
            get
            {
                switch (_currentPhase)
                {
                    case RunPhase.Farming:
                    case RunPhase.Shrink1:
                    case RunPhase.Shrink2:
                    case RunPhase.Shrink3:
                        return true;
                    default:
                        return false;
                }
            }
        }

        // 목표 중심은 아직 원점 고정이다 — RunManager(다른 파트 소유)의 시작점·보스점 SyncVar 확정이 선행 조건
        public Vector3 Center => BarrierJudge.GetCenter(_transitionState, NetworkTime.time);

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
            AuthorityTickScheduler.ServerInstance?.Register(TICK_PRIORITY_BARRIER, ServerTickDamage);

            if (RunManager.ServerInstance != null)
            {
                ServerCommitPhase(RunManager.ServerInstance.Phase);
            }
        }

        public override void OnStopServer()
        {
            GameEventBus.Instance?.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            AuthorityTickScheduler.ServerInstance?.Unregister(ServerTickDamage);
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

            return BarrierJudge.IsOutside(worldPosition, _transitionState, NetworkTime.time);
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

            if (!GameDB.Barrier.TryGetPhase(_currentPhase, out BarrierPhaseSO.PhaseEntry entry))
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
            ServerCommitPhase(evt.Current);
        }

        [Server]
        private void ServerCommitPhase(RunPhase phase)
        {
            bool  hasEntry    = GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry entry);
            float targetRadius = hasEntry ? (entry.RadiusRatio * GameDB.Barrier.MaxRadius) : 0f;

            double now = NetworkTime.time;

            // 중심은 원점 고정이다 — 시작점→보스점 보간은 RunManager의 지점 SyncVar가 생긴 뒤 채운다
            _transitionState = new BarrierTransitionState
            {
                FromCenter      = _transitionState.TargetCenter,
                TargetCenter    = Vector3.zero,
                FromRadius      = _transitionState.TargetRadius,
                TargetRadius    = targetRadius,
                StartServerTime = now,
                EndServerTime   = now + GameDB.Barrier.ShrinkDuration,
                Revision        = _transitionState.Revision + 1,
            };

            _currentPhase = phase;
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

            double now = NetworkTime.time;
            if (now < _nextDamageServerTime)
            {
                return;
            }

            _nextDamageServerTime = now + GameDB.Barrier.DamageTickInterval;

            EnsureTargetAdaptersRegistered();

            if (!GameDB.Barrier.TryGetPhase(_currentPhase, out BarrierPhaseSO.PhaseEntry entry))
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

        /****************************************
        *          Gizmos (Editor Only)
        ****************************************/

        // NetworkTime.time은 런타임에만 유효하다 — 에디트 모드에서 호출하면 예외가 난다
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
            DrawWireCircleXZ(center, radius);

            // 목표 반경도 옅게 같이 그려서 축소가 어디까지 갈지 미리 본다
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.25f);
            DrawWireCircleXZ(_transitionState.TargetCenter, _transitionState.TargetRadius);
        }

        private static void DrawWireCircleXZ(Vector3 center, float radius, int segments = 64)
        {
            if (radius <= 0f)
            {
                return;
            }

            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; ++i)
            {
                float   angle = (i / (float) segments) * Mathf.PI * 2f;
                Vector3 next  = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
