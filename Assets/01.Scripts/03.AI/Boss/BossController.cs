// System
using System;
using System.Collections;
using System.Collections.Generic;

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
    /// 보스 실행부. HP 비율로 페이즈 전환을 커밋하고 현재 페이즈에 Tick을 위임한다
    /// 대상 스캔과 예고 상태 동기화도 여기서 맡는다. 페이즈 State는 언제 무엇을 쓸지만 정한다
    /// 패턴 판정 자체는 BossPatternRunner가 한다
    /// </summary>
    [RequireComponent(typeof(BossStatus))]
    public partial class BossController : NetworkBehaviour, IBossTelegraphSource, IBossPhaseSource,
        IPrecisionInterruptTarget
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

        private const float TARGET_SCAN_INTERVAL    = 12f;  // 근접 어그로 재평가 주기
        private const float TARGET_SCAN_RADIUS      = 30f;  // 보스 사거리들보다 넉넉히 잡는다
        private const int   TARGET_SCAN_BUFFER_SIZE = Constants.Network.MAX_PLAYERS * 4;
        private const int   MAX_ACTIVE_TELEGRAPHS   = 8;

        [SerializeField] private BossStatus _status;
        [SerializeField] private BossPartController _parts;
        [SerializeField] private BossSoulController _souls;

        private BossPhaseStateBase  _currentPhaseState;
        private Coroutine           _schedulerWaitRoutine;
        private int                 _playerLayerMask;
        private readonly Collider[] _targetScanBuffer = new Collider[TARGET_SCAN_BUFFER_SIZE];
        private BossPatternSO       _bossData;
        private GameEventBus        _eventBus;
        private float               _targetScanTimer         = 0f;
        private IDamageable         _targetDamageable; // 현재 Target의 생존을 매 틱 값싸게 확인하기 위한 캐시
        private uint                _nextAttackInstanceId    = 0u;
        private uint                _nextParryWindowId       = 0u;
        private float               _transitionLockRemaining = 0f;
        private bool                _serverStopped           = false;
        private bool                _serverTerminated        = false;
        private uint                _lastResonanceAttackId   = 0u;
        private uint                _lastPrecisionInterruptedAttackId = 0u;
        private uint                _groggyGeneration = 0u;
        private uint                _telegraphRevision = 0u;
        private readonly List<ParticipantId> _resonanceOwnerBuffer =
            new List<ParticipantId>(Constants.Network.MAX_PLAYERS);

        private GroggyPhase _groggyPhase      = GroggyPhase.Active;
        private float       _groggyStateTimer = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public int CurrentPhaseIndex => _currentPhaseIndex;
        public BossTelegraphCue TelegraphCue => _telegraphCue;
        public IReadOnlyList<BossTelegraphCue> TelegraphCues => _telegraphCues;
        public BossPhaseSnapshot PhaseSnapshot => _phaseSnapshot;
        public TargetRef PrecisionTargetRef => CreateBodyTargetRef();
        public bool IsEnrageWarning => _isEnrageWarning;
        public bool IsEnraged => _isEnraged;

        /// <summary>
        /// 남은 그로기 저항. 0이 되는 순간 그로기에 들어간다
        /// 최대치는 밸런스 데이터(BossPatternSO.GroggyGaugeMax)라 비율은 읽는 쪽에서 만든다
        /// </summary>
        public float GroggyGauge => _groggyGauge;
        public event Action<BossTelegraphCue> TelegraphChanged;
        public event Action<BossPhaseSnapshot> PhaseChanged;
        public event Action<bool> EnrageWarningChanged;
        public event Action<bool> EnragedChanged;

        /// <summary> 패턴이 타격을 실행하는 순간마다 발생한다, 표현 전용(임시 러프 VFX) </summary>
        public event Action<BossPatternSO.PatternTemplate, Vector3, Vector3> PatternImpact;

        /// <summary> 패턴이 예고를 시작하는 순간마다 발생한다, 표현 전용(임시 러프 VFX) </summary>
        public event Action<BossPatternSO.PatternTemplate, Vector3, Vector3> PatternTelegraphVfx;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_status == null)
            {
                TryGetComponent(out _status);
            }

            if (_parts == null)
            {
                TryGetComponent(out _parts);
            }

            if (_souls == null)
            {
                TryGetComponent(out _souls);
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

        /// <summary>
        /// 서버 실행을 멈춘다. terminate면 다시 시작하지 않는다 - 사망, 런 이탈, 스폰 해제가 여기다
        /// 비활성화는 재개 가능한 정지라 페이즈 State를 종료시키지 않고 진행 중인 공격만 취소한다
        /// </summary>
        private void StopServerExecution(bool terminate)
        {
            if (terminate)
            {
                _serverTerminated = true;
                MarkRegistry.ClearEntity(netId);
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

            ServerClearTelegraphs();

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

            // 씬 오브젝트 간 OnStartServer 호출 순서는 Mirror가 보장하지 않는다 -
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

        /****************************************
        *              Server API
        ****************************************/

        private void HandleTelegraphListChanged(SyncList<BossTelegraphCue>.Operation operation,
            int index, BossTelegraphCue oldItem, BossTelegraphCue newItem)
        {
            BossTelegraphCue changed = (operation == SyncList<BossTelegraphCue>.Operation.OP_REMOVEAT)
                ? oldItem
                : newItem;
            if ((operation == SyncList<BossTelegraphCue>.Operation.OP_REMOVEAT) ||
                (operation == SyncList<BossTelegraphCue>.Operation.OP_CLEAR))
            {
                changed.IsActive = false;
            }

            NotifyTelegraphChanged(changed);
        }

        private void NotifyTelegraphChanged(BossTelegraphCue cue)
        {
            ObserverDispatch.Invoke(TelegraphChanged, cue, this,
                nameof(BossController), nameof(TelegraphChanged));
        }

        private void NotifyPhaseChanged(BossPhaseSnapshot snapshot)
        {
            ObserverDispatch.Invoke(PhaseChanged, snapshot, this,
                nameof(BossController), nameof(PhaseChanged));
        }

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            BossPhaseSourceLocator.Register(this);
            _telegraphCues.Callback += HandleTelegraphListChanged;
            for (int i = 0; i < _telegraphCues.Count; ++i)
            {
                NotifyTelegraphChanged(_telegraphCues[i]);
            }

            NotifyPhaseChanged(_phaseSnapshot);
        }

        public override void OnStopClient()
        {
            BossPhaseSourceLocator.Unregister(this);
            _telegraphCues.Callback -= HandleTelegraphListChanged;
            BossTelegraphCue cue = _telegraphCue;
            cue.IsActive = false;
            NotifyTelegraphChanged(cue);
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        // 판정과 별개로 각 클라이언트가 그로기 진입 피드백을 재생한다
        [ClientRpc]
        private void RpcGroggyEnter()
        {
            CombatFeedbackLocator.Current.PlayGroggyEnter();
        }

        // 패턴 임팩트 VFX 신호(러프, 이슈 검증용) - 표현 전용 컴포넌트가 구독한다
        [ClientRpc]
        private void RpcPatternImpact(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            PatternImpact?.Invoke(template, origin, forward);
        }

        // 패턴 예고 VFX 신호(러프, 이슈 검증용) - 표현 전용 컴포넌트가 구독한다
        [ClientRpc]
        private void RpcPatternTelegraphVfx(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            PatternTelegraphVfx?.Invoke(template, origin, forward);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandleTelegraphChanged(BossTelegraphCue previous, BossTelegraphCue current)
        {
            NotifyTelegraphChanged(current);
        }

        private void HandlePhaseSnapshotChanged(BossPhaseSnapshot previous,
            BossPhaseSnapshot current)
        {
            NotifyPhaseChanged(current);
        }

        private void HandleEnrageWarningChanged(bool previous, bool current)
        {
            ObserverDispatch.Invoke(EnrageWarningChanged, current, this,
                nameof(BossController), nameof(EnrageWarningChanged));
        }

        private void HandleEnragedChanged(bool previous, bool current)
        {
            ObserverDispatch.Invoke(EnragedChanged, current, this,
                nameof(BossController), nameof(EnragedChanged));
        }
    }
}
