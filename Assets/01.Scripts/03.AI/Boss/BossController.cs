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
    /// 대상 스캔과 텔레그래프 RPC 발신도 여기서 맡는다, 페이즈 State는 언제 무엇을 쓸지만 정한다.
    /// 패턴 판정 자체는 BossPatternRunner가 한다.
    /// 지금은 1페이즈 MeleeArc 패턴 1개만 있다, 나머지는 후속 슬라이스에서 채운다
    /// </summary>
    [RequireComponent(typeof(BossStatus))]
    public class BossController : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        // 그로기 게이지, 그로기, 면역은 페이즈와 무관하게 보스 전체에 걸리는 상태라 여기서 관리한다
        private enum GroggyPhase
        {
            Active, // 평상시, 카운터가 게이지를 깎는다
            Groggy, // 게이지 0 도달, 패턴 발동을 멈춘다
            Immune, // 그로기 종료 직후, 게이지는 최대치지만 카운터가 안 먹는다
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float TARGET_SCAN_INTERVAL    = 0.5f; // 매 틱 스캔 대신 주기 스캔, EnemyController와 같은 간격
        private const float TARGET_SCAN_RADIUS      = 30f;  // 보스 사거리들보다 넉넉히 잡는다
        private const int   TARGET_SCAN_BUFFER_SIZE = Constants.Network.MAX_PLAYERS + 1;

        [SerializeField] private BossStatus _status;

        private BossPhaseStateBase _currentPhaseState;
        private int                _currentPhaseIndex = 1;
        private Coroutine          _schedulerWaitRoutine;
        private float              _targetScanTimer = 0f;
        private int                _playerLayerMask;
        private uint               _nextAttackInstanceId = 0;
        private readonly Collider[] _targetScanBuffer = new Collider[TARGET_SCAN_BUFFER_SIZE];

        private GroggyPhase _groggyPhase      = GroggyPhase.Active;
        private float       _groggyGauge      = 0f;
        private float       _groggyStateTimer = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _status.Initialize();
            _status.Died += HandleDied;

            _groggyGauge = GameDB.Boss.GroggyGaugeMax;

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

            TickGroggyState(deltaTime);
            CommitPendingPhaseTransitions();

            _targetScanTimer += deltaTime;
            if (_targetScanTimer >= TARGET_SCAN_INTERVAL)
            {
                _targetScanTimer = 0f;
                RefreshTarget();
            }

            if (_groggyPhase == GroggyPhase.Groggy)
            {
                return; // 그로기 중엔 패턴을 새로 고르거나 발동하지 않는다
            }

            _currentPhaseState.Tick(deltaTime);
        }

        /// <summary>
        /// 그로기, 면역 구간의 시간 경과만 담당한다. 게이지를 0으로 만드는 것은
        /// ServerReduceGroggyGauge다, 여기서는 시간만 흐른다
        /// </summary>
        [Server]
        private void TickGroggyState(float deltaTime)
        {
            switch (_groggyPhase)
            {
                case GroggyPhase.Groggy:
                    _groggyStateTimer += deltaTime;
                    if (_groggyStateTimer >= GameDB.Boss.GroggyDuration)
                    {
                        _groggyPhase      = GroggyPhase.Immune;
                        _groggyStateTimer = 0f;
                        _groggyGauge      = GameDB.Boss.GroggyGaugeMax;
                    }
                    break;
                case GroggyPhase.Immune:
                    _groggyStateTimer += deltaTime;
                    if (_groggyStateTimer >= GameDB.Boss.GroggyImmunityDuration)
                    {
                        _groggyPhase      = GroggyPhase.Active;
                        _groggyStateTimer = 0f;
                    }
                    break;
            }
        }

        [Server]
        private void RefreshTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, TARGET_SCAN_RADIUS, _targetScanBuffer, _playerLayerMask);

            Transform nearest         = null;
            float     nearestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                if ((_targetScanBuffer[i].TryGetComponent(out IDamageable damageable)) && (damageable.IsDead))
                {
                    continue; // 관전 중인 플레이어는 노려도 무의미하다
                }

                float distance = Vector3.Distance(transform.position, _targetScanBuffer[i].transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest         = _targetScanBuffer[i].transform;
                }
            }

            _currentPhaseState.Target = nearest;
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
        /// 오버킬은 클램프하지 않는다, 한 방에 두 단계를 건너뛰면 그대로 반영된다.
        /// TODO: 진행 중이던 패턴 취소, 패링 창 닫기, TransitionLockDuration은 후속 슬라이스에서 채운다
        /// </summary>
        [Server]
        private void CommitPendingPhaseTransitions()
        {
            while ((GameDB.Boss.TryGetPhase(_currentPhaseIndex + 1, out BossPatternSO.PhaseEntry nextPhase))
                   && (_status.HealthRatio <= nextPhase.EnterHealthRatio))
            {
                Transform carriedTarget = _currentPhaseState?.Target;

                _currentPhaseIndex  = nextPhase.PhaseIndex;
                _currentPhaseState  = CreatePhaseState(_currentPhaseIndex);
                _currentPhaseState.Target = carriedTarget; // 다음 스캔까지 대상을 잃지 않는다
            }
        }

        [Server]
        private BossPhaseStateBase CreatePhaseState(int phaseIndex)
        {
            switch (phaseIndex)
            {
                case 1:
                    return new Phase1State(transform, _status, this);
                case 2:
                    return new Phase2State(transform, _status, this);
                default:
                    Debug.LogError($"[BossController] {phaseIndex} 페이즈의 State가 아직 없다", this);
                    return null;
            }
        }

        /// <summary> 공격 1회의 단조 증가 ID를 발급한다, 0은 예약값이라 건너뛴다 </summary>
        [Server]
        internal uint ServerNextAttackInstanceId()
        {
            ++_nextAttackInstanceId;
            if (_nextAttackInstanceId == 0u)
            {
                ++_nextAttackInstanceId;
            }

            return _nextAttackInstanceId;
        }

        /// <summary> 서버가 확정한 예고를 모든 클라이언트에 전파한다 </summary>
        [Server]
        internal void ServerBeginTelegraph(BossTelegraphCue cue)
        {
            RpcBeginTelegraph(cue);
        }

        /// <summary>
        /// 카운터 1회가 그로기 게이지를 깎는다. 그로기, 면역 중에는 먹지 않는다.
        /// 지금은 차사 패링 성공만 이 경로를 탄다 - 검 사용자만 패링에 성공할 수 있으므로
        /// 호출부에서 캐릭터 종류를 따로 조회하지 않고 CharacterKind.Chasa를 고정으로 넘긴다.
        /// TODO: 신궁 시전 취소, 무녀 표식 공명 카운터는 각 파트 이벤트 연결 후 추가한다
        /// </summary>
        [Server]
        internal void ServerReduceGroggyGauge(CharacterKind character)
        {
            if (_groggyPhase != GroggyPhase.Active)
            {
                return;
            }

            if (!GameDB.Boss.TryGetGaugeReduction(character, out float amount))
            {
                return;
            }

            _groggyGauge = Mathf.Max(_groggyGauge - amount, 0f);
            if (_groggyGauge <= 0f)
            {
                EnterGroggy();
            }
        }

        /// <summary>
        /// TODO: 진행 중이던 공격 취소, 패링 창 닫기는 후속 슬라이스에서 채운다.
        /// 지금은 진행 중이던 공격이 자연스럽게 끝난 뒤 다음 Idle 전환부터 멈춘다
        /// </summary>
        [Server]
        private void EnterGroggy()
        {
            _groggyPhase      = GroggyPhase.Groggy;
            _groggyStateTimer = 0f;
            RpcGroggyEnter();
        }

        /// <summary> 그로기 진입 연출. 로컬 표현이라 각 클라이언트가 자기 화면에서 재생한다 </summary>
        [ClientRpc]
        private void RpcGroggyEnter()
        {
            CombatFeedbackLocator.Current.PlayGroggyEnter();
        }

        /// <summary>
        /// 예고 표현. 아직 BossTelegraphView가 없어 표현은 하지 않는다. 서버 판정은
        /// 이 RPC와 무관하게 계속 진행되므로 표현 부재가 결과를 바꾸지 않는다.
        /// TODO: BossTelegraphView 연결
        /// </summary>
        [ClientRpc]
        private void RpcBeginTelegraph(BossTelegraphCue cue)
        {
        }

        /// <summary> 공격 취소를 모든 클라이언트에 전파한다. 표현 미연결은 위와 같은 이유로 안전하다 </summary>
        [ClientRpc]
        internal void RpcCancelTelegraph(uint attackInstanceId)
        {
        }
    }
}
