// System
using System;
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
    /// 대상 스캔과 예고 상태 동기화도 여기서 맡는다. 페이즈 State는 언제 무엇을 쓸지만 정한다.
    /// 패턴 판정 자체는 BossPatternRunner가 한다.
    /// </summary>
    [RequireComponent(typeof(BossStatus))]
    public class BossController : NetworkBehaviour, IBossTelegraphSource
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

        [SyncVar] private int _currentPhaseIndex = 1;
        [SyncVar(hook = nameof(HandleTelegraphChanged))]
        private BossTelegraphCue _telegraphCue;

        private BossPhaseStateBase  _currentPhaseState;
        private Coroutine           _schedulerWaitRoutine;
        private int                 _playerLayerMask;
        private readonly Collider[] _targetScanBuffer = new Collider[TARGET_SCAN_BUFFER_SIZE];
        private BossPatternSO       _bossData;
        private GameEventBus        _eventBus;
        private float               _targetScanTimer         = 0f;
        private uint                _nextAttackInstanceId    = 0u;
        private float               _transitionLockRemaining = 0f;
        private bool                _serverStopped           = false;
        private bool                _serverTerminated        = false;
        private uint                _lastResonanceAttackId   = 0u;

        private GroggyPhase _groggyPhase      = GroggyPhase.Active;
        private float       _groggyGauge      = 0f;
        private float       _groggyStateTimer = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public int CurrentPhaseIndex => _currentPhaseIndex;
        public BossTelegraphCue TelegraphCue => _telegraphCue;
        public event Action<BossTelegraphCue> TelegraphChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_status == null)
            {
                TryGetComponent(out _status);
            }

            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        private void OnEnable()
        {
            // OnStartServer 이전이면 _currentPhaseState가 없다. 그때는 등록도 재개도 하지 않는다
            if ((!isServer) || (_currentPhaseState == null))
            {
                return;
            }

            ResumeServerExecution();
        }

        private void OnDisable()
        {
            if (isServer)
            {
                StopServerExecution(terminate: false);
            }
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _bossData                = GameDB.Boss;
            _serverStopped           = false;
            _serverTerminated        = false;
            _lastResonanceAttackId   = 0u;
            _transitionLockRemaining = 0f;
            _targetScanTimer         = 0f;
            _telegraphCue            = default;
            _groggyPhase             = GroggyPhase.Active;
            _groggyStateTimer        = 0f;
            _status.Initialize();
            _status.Died += HandleDied;

            _groggyGauge = _bossData.GroggyGaugeMax;

            _eventBus = GameEventBus.Instance;
            if (_eventBus != null)
            {
                _eventBus.Subscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                _eventBus.Subscribe<RunResetEvent>(HandleRunReset);
            }

            _currentPhaseIndex = 1;
            _currentPhaseState = CreatePhaseState(_currentPhaseIndex);

            RegisterServerTick();
        }

        public override void OnStopServer()
        {
            _status.Died -= HandleDied;

            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                _eventBus.Unsubscribe<RunResetEvent>(HandleRunReset);
                _eventBus = null;
            }

            StopServerExecution(terminate: true);
        }

        /// <summary>
        /// 서버 실행을 멈춘다. terminate면 다시 시작하지 않는다 - 사망, 런 이탈, 스폰 해제가 여기다.
        /// 비활성화는 재개 가능한 정지라 페이즈 State를 종료시키지 않고 진행 중인 공격만 취소한다
        /// </summary>
        private void StopServerExecution(bool terminate)
        {
            if (terminate)
            {
                _serverTerminated = true;
            }

            if (_serverStopped)
            {
                // 이미 멈춘 뒤 종료가 확정된 경우(비활성화 후 스폰 해제)에도 State는 닫는다
                if (terminate)
                {
                    _currentPhaseState?.Exit();
                }

                return;
            }

            _serverStopped = true;

            if (terminate)
            {
                _currentPhaseState?.Exit();
            }
            else
            {
                _currentPhaseState?.CancelActiveAttack();
            }

            // 그로기 도중 멈추면 배율이 남는다. 멈추는 경로가 항상 되돌린다
            if (NetworkServer.active)
            {
                _status.ServerSetBodyDamageMultiplier(1f);
            }

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

        /// <summary>
        /// 비활성화로 멈춘 실행만 되살린다. 켜지자마자 때리지 않도록 전환 잠금과 같은 유예를 주고,
        /// 멈출 때 되돌렸던 그로기 배율은 아직 그로기면 다시 건다
        /// </summary>
        private void ResumeServerExecution()
        {
            if ((_serverTerminated) || (!_serverStopped) || (_status.IsDead))
            {
                return;
            }

            _serverStopped           = false;
            _targetScanTimer         = 0f;
            _transitionLockRemaining = _bossData.TransitionLockDuration;

            if (_groggyPhase == GroggyPhase.Groggy)
            {
                _status.ServerSetBodyDamageMultiplier(_bossData.GroggyBodyDamageMultiplier);
            }

            RegisterServerTick();
        }

        private void RegisterServerTick()
        {
            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_AI, ServerTick);
                return;
            }

            // 씬 오브젝트 간 OnStartServer 호출 순서는 Mirror가 보장하지 않는다 —
            // 스케줄러가 아직 안 떴을 수 있으니, 뜰 때까지 기다렸다가 등록한다
            if (_schedulerWaitRoutine == null)
            {
                _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
            }
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
            if ((_serverStopped) || (_status.IsDead) || (_currentPhaseState == null))
            {
                return;
            }

            TickGroggyState(deltaTime);
            CommitPendingPhaseTransitions();

            if (_serverStopped)
            {
                return;
            }

            if (_transitionLockRemaining > 0f)
            {
                _transitionLockRemaining = Mathf.Max(0f, _transitionLockRemaining - deltaTime);
                return;
            }

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

        // 그로기와 면역의 시간만 진행한다. 게이지 감소는 카운터 수락 경로가 맡는다.
        [Server]
        private void TickGroggyState(float deltaTime)
        {
            switch (_groggyPhase)
            {
                case GroggyPhase.Groggy:
                    _groggyStateTimer += deltaTime;
                    if (_groggyStateTimer >= _bossData.GroggyDuration)
                    {
                        _groggyPhase      = GroggyPhase.Immune;
                        _groggyStateTimer = 0f;
                        _groggyGauge      = _bossData.GroggyGaugeMax;
                        _status.ServerSetBodyDamageMultiplier(1f);
                    }
                    break;
                case GroggyPhase.Immune:
                    _groggyStateTimer += deltaTime;
                    if (_groggyStateTimer >= _bossData.GroggyImmunityDuration)
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
            StopServerExecution(terminate: true);
        }

        private void HandleRunPhaseChanged(RunPhaseChangedEvent evt)
        {
            if (evt.Current != RunPhase.Boss)
            {
                StopServerExecution(terminate: true);
            }
        }

        private void HandleRunReset(RunResetEvent evt)
        {
            StopServerExecution(terminate: true);
        }

        // HP 조건을 만족하는 다음 페이즈로 넘어가기 전에 이전 실행을 종료한다.
        [Server]
        private void CommitPendingPhaseTransitions()
        {
            while ((_bossData.TryGetPhase(_currentPhaseIndex + 1, out BossPatternSO.PhaseEntry nextPhase))
                   && (_status.HealthRatio <= nextPhase.EnterHealthRatio))
            {
                Transform carriedTarget = _currentPhaseState?.Target;
                BossPhaseStateBase nextState = CreatePhaseState(nextPhase.PhaseIndex);
                if (nextState == null)
                {
                    StopServerExecution(terminate: true);
                    return;
                }

                _currentPhaseState?.Exit();

                _currentPhaseIndex  = nextPhase.PhaseIndex;
                _currentPhaseState  = nextState;
                _currentPhaseState.Target = carriedTarget; // 다음 스캔까지 대상을 잃지 않는다
                _transitionLockRemaining = _bossData.TransitionLockDuration;
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
            if ((_serverStopped) || (cue.AttackInstanceId == 0u))
            {
                return;
            }

            cue.IsActive = true;
            cue.StartServerTime = NetworkTime.time;
            _telegraphCue = cue;
        }

        /// <summary> 같은 공격의 예고만 닫는다. 중복 또는 지난 공격의 취소는 무시한다 </summary>
        [Server]
        internal void ServerCancelTelegraph(uint attackInstanceId)
        {
            if ((!_telegraphCue.IsActive) || (attackInstanceId == 0u)
                || (_telegraphCue.AttackInstanceId != attackInstanceId))
            {
                return;
            }

            BossTelegraphCue cue = _telegraphCue;
            cue.IsActive = false;
            _telegraphCue = cue;
        }

        /// <summary>
        /// 패턴 하나가 끝났다고 알린다. 무녀 표식 공명은 여기서만 판정한다 -
        /// 카운터 패턴이 끝나는 시각 기준으로 공명 창 안에 걸린 표식이 있으면 게이지를 깎는다.
        /// 공격 하나당 한 번만 인정한다, 무녀가 둘이어도 두 번 깎이지 않는다.
        ///
        /// 취소된 공격은 여기까지 오지 않는다. 패턴이 끝나야 카운터라는 계약이라
        /// 패링이나 대상 소실로 끊긴 공격에는 공명이 성립하지 않는다
        /// </summary>
        [Server]
        internal void ServerNotifyPatternEnded(int patternId, uint attackInstanceId)
        {
            if ((_serverStopped) || (attackInstanceId == 0u)
                || (attackInstanceId == _lastResonanceAttackId))
            {
                return;
            }

            if (!_bossData.IsCounterPattern(CharacterKind.Munyeo, patternId))
            {
                return;
            }

            if (!MarkRegistry.IsMarkedWithin(CreateBodyTargetRef(), NetworkTime.time, _bossData.ResonanceMarkWindow))
            {
                return;
            }

            _lastResonanceAttackId = attackInstanceId;
            ServerReduceGroggyGauge(CharacterKind.Munyeo);
        }

        /// <summary>
        /// 표식이 가리키는 본체 참조를 만든다. 런과 씬 세대를 함께 담아
        /// 이전 런에서 남은 표식이 지금 보스에 맞아 떨어지지 않게 한다
        /// </summary>
        [Server]
        private TargetRef CreateBodyTargetRef()
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;

            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                TargetRef.BODY_PART_ID);
        }

        /// <summary>
        /// 카운터 1회가 그로기 게이지를 깎는다. 그로기, 면역 중에는 먹지 않는다.
        /// 지금은 차사 패링 성공만 이 경로를 탄다 - 검 사용자만 패링에 성공할 수 있으므로
        /// 호출부에서 캐릭터 종류를 따로 조회하지 않고 CharacterKind.Chasa를 고정으로 넘긴다.
        /// TODO(민성): 신궁 정밀 사격과 무녀 표식 공명 카운터를 연결한다.
        /// </summary>
        [Server]
        internal void ServerReduceGroggyGauge(CharacterKind character)
        {
            if ((_serverStopped) || (_status.IsDead) || (_groggyPhase != GroggyPhase.Active))
            {
                return;
            }

            if (!_bossData.TryGetGaugeReduction(character, out float amount))
            {
                return;
            }

            _groggyGauge = Mathf.Max(_groggyGauge - amount, 0f);
            if (_groggyGauge <= 0f)
            {
                EnterGroggy();
            }
        }

        // 그로기 동안 이전 공격이 멈췄다가 되살아나지 않게 진입 시 정리한다.
        [Server]
        private void EnterGroggy()
        {
            _groggyPhase      = GroggyPhase.Groggy;
            _groggyStateTimer = 0f;
            _currentPhaseState?.CancelActiveAttack();
            _status.ServerSetBodyDamageMultiplier(_bossData.GroggyBodyDamageMultiplier);
            RpcGroggyEnter();
        }

        // 판정과 별개로 각 클라이언트가 그로기 진입 피드백을 재생한다.
        [ClientRpc]
        private void RpcGroggyEnter()
        {
            CombatFeedbackLocator.Current.PlayGroggyEnter();
        }

        private void HandleTelegraphChanged(BossTelegraphCue previous, BossTelegraphCue current)
        {
            TelegraphChanged?.Invoke(current);
        }

        public override void OnStopClient()
        {
            BossTelegraphCue cue = _telegraphCue;
            cue.IsActive = false;
            TelegraphChanged?.Invoke(cue);
        }
    }
}
