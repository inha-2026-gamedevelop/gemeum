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

namespace Geumeum.Systems
{
    /// <summary>
    /// 한 런의 파티 성장 상태를 서버에서 소유한다.
    /// 적 사망 이벤트만 구독하므로 AI와 성장 시스템 사이에 직접 참조가 생기지 않는다
    /// </summary>
    [DisallowMultipleComponent]
    public class GrowthManager : NetworkBehaviour, IGrowthProvider
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<uint> _awardedEnemyNetIds = new HashSet<uint>();
        private readonly List<IGrowthParticipant> _participants = new List<IGrowthParticipant>();

        private GrowthState _state;
        private bool       _hasState;
        private bool       _isRunActive;
        private bool       _hasEventBusSubscription;
        private Coroutine  _eventBusWaitRoutine;

        // 매니저가 네트워크 오브젝트일 때 클라이언트 HUD가 공용 상태를 읽는 값
        [SyncVar] private int   _networkLevel;
        [SyncVar] private int   _networkExperience;
        [SyncVar] private float _networkMaxHealth;
        [SyncVar] private int   _networkTalentPoints;
        [SyncVar(hook = nameof(OnNetworkGrowthRevisionChanged))] private uint  _networkRevision;

        /****************************************
        *              Properties
        ****************************************/

        public GrowthState State             => _hasState
            ? _state
            : new GrowthState(_networkLevel, _networkExperience, _networkMaxHealth,
                _networkRevision, _networkTalentPoints);
        public bool        HasState          => (_hasState || (_networkRevision > 0u));
        public bool        IsRunActive       => _isRunActive;
        public int         Level             => State.Level;
        public int         Experience        => State.Experience;
        public float       MaxHealth         => State.MaxHealth;
        public int         AwardedEnemyCount => _awardedEnemyNetIds.Count;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 공용 성장 상태가 바뀔 때 서버와 UI 중계자가 받는다 </summary>
        public event Action<GrowthState> GrowthChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        public override void OnStartServer()
        {
            GrowthProviderLocator.Register(this);
            ResetState(false);
            SyncRegisteredParticipants();
            TrySubscribeToEventBus();
        }

        public override void OnStartClient()
        {
            GrowthProviderLocator.Register(this);
        }

        public override void OnStopClient()
        {
            if (!isServer)
            {
                GrowthProviderLocator.Unregister(this);
            }
        }

        public override void OnStopServer()
        {
            if (_eventBusWaitRoutine != null)
            {
                StopCoroutine(_eventBusWaitRoutine);
                _eventBusWaitRoutine = null;
            }

            UnsubscribeFromEventBus();
            GrowthProviderLocator.Unregister(this);
            _awardedEnemyNetIds.Clear();
            _participants.Clear();
            _state      = default;
            _hasState    = false;
            _isRunActive = false;
            _networkLevel = 0;
            _networkExperience = 0;
            _networkMaxHealth = 0f;
            _networkRevision = 0u;
            _networkTalentPoints = 0;
        }

        /****************************************
        *              Provider API
        ****************************************/

        public bool TryGetState(ParticipantId participant, out GrowthState state)
        {
            state = default;

            if ((!participant.IsValid) || (!HasState))
            {
                return false;
            }

            state = State;
            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런 시작 이벤트가 늦게 연결된 개발 fixture가 명시적으로 시작할 때 쓴다 </summary>
        [Server]
        public bool ServerBeginRun()
        {
            return BeginRun();
        }

        /// <summary> 새 런 경계에서 경험치와 중복 처치 기록을 비운다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            ResetState(true);
        }

        /// <summary> 관리자나 테스트가 직접 파티 경험치를 적용할 때 쓴다 </summary>
        [Server]
        public bool ServerTryAddExperience(int amount)
        {
            if (!_isRunActive)
            {
                return false;
            }

            return ApplyExperience(amount);
        }

        /// <summary> 적 사망 보상을 한 번만 파티에 적용한다 </summary>
        [Server]
        public bool ServerAwardEnemyExperience(EnemyDiedEvent enemyDied)
        {
            if ((!_isRunActive) || (!enemyDied.IsValid) || enemyDied.IsBoss ||
                (!enemyDied.Kind.HasValue))
            {
                return false;
            }

            if (_awardedEnemyNetIds.Contains(enemyDied.EnemyNetId))
            {
                return false;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Enemy == null) ||
                (!GameDB.Enemy.TryGetStat(enemyDied.Kind.Value, out EnemySO.StatEntry stat)))
            {
                return false;
            }

            int experience = GrowthRules.ResolveRewardExperience(stat.Experience,
                enemyDied.RewardMultiplier);
            if (!ApplyExperience(experience))
            {
                return false;
            }

            _awardedEnemyNetIds.Add(enemyDied.EnemyNetId);
            return true;
        }

        /****************************************
        *              Event Handlers
        ****************************************/

        private void HandleEnemyDied(EnemyDiedEvent enemyDied)
        {
            ServerAwardEnemyExperience(enemyDied);
        }

        private void HandleRunReset(RunResetEvent _)
        {
            ResetState(true);
        }

        private void HandleRunPhaseChanged(RunPhaseChangedEvent phaseChanged)
        {
            if ((phaseChanged.Current == RunPhase.Lobby) ||
                (phaseChanged.Current == RunPhase.Result))
            {
                _isRunActive = false;
                return;
            }

            if (phaseChanged.Previous == RunPhase.Lobby)
            {
                BeginRun();
                return;
            }

            _isRunActive = true;
        }

        /****************************************
        *                Internals
        ****************************************/

        private bool BeginRun()
        {
            if (!TryCreateStartingState())
            {
                return false;
            }

            _awardedEnemyNetIds.Clear();
            _isRunActive = true;
            ApplyStateToParticipants();
            GrowthChanged?.Invoke(_state);
            return true;
        }

        private void ResetState(bool applyToParticipants)
        {
            _awardedEnemyNetIds.Clear();
            _isRunActive = false;
            if (!TryCreateStartingState())
            {
                return;
            }

            if (applyToParticipants)
            {
                ApplyStateToParticipants();
            }

            GrowthChanged?.Invoke(_state);
        }

        private bool TryCreateStartingState()
        {
            if ((!GameDB.IsInitialized) ||
                (!GrowthRules.TryCreateStartingState(GameDB.LevelCurve, out GrowthState state)))
            {
                _state    = default;
                _hasState = false;
                _networkLevel = 0;
                _networkExperience = 0;
                _networkMaxHealth = 0f;
                _networkTalentPoints = 0;
                _networkRevision = 0u;
                return false;
            }

            _state    = state;
            _hasState = true;
            PublishNetworkState();
            return true;
        }

        private bool ApplyExperience(int amount)
        {
            if ((!_isRunActive) || RunTimeLocator.Current.IsPaused || (!_hasState) ||
                (amount <= 0) || (!GameDB.IsInitialized) ||
                (GameDB.LevelCurve == null) ||
                (!GrowthRules.TryApplyExperience(GameDB.LevelCurve, _state, amount,
                    out GrowthState next, out GrowthApplyResult result)))
            {
                return false;
            }

            _state = next;
            PublishNetworkState();
            ApplyStateToParticipants();
            GrowthChanged?.Invoke(_state);

            if (result.LevelsGained > 0)
            {
                Debug.Log($"[GrowthManager] 파티 레벨업: {_state.Level}, 경험치 {_state.Experience}, 최대 HP +{result.MaxHealthIncrease:F0}", this);
            }

            return true;
        }

        private void PublishNetworkState()
        {
            if (!_hasState)
            {
                return;
            }

            _networkLevel        = _state.Level;
            _networkExperience   = _state.Experience;
            _networkMaxHealth    = _state.MaxHealth;
            _networkTalentPoints = _state.TalentPoints;
            _networkRevision     = _state.Revision;
        }

        private void OnNetworkGrowthRevisionChanged(uint _, uint __)
        {
            if (!isServer)
            {
                GrowthChanged?.Invoke(State);
            }
        }

        private void SyncRegisteredParticipants()
        {
            if (!_hasState)
            {
                return;
            }

            ApplyStateToParticipants();
        }

        private void ApplyStateToParticipants()
        {
            GrowthParticipantLocator.CopyTo(_participants);
            for (int i = 0; i < _participants.Count; ++i)
            {
                IGrowthParticipant participant = _participants[i];
                if (participant == null)
                {
                    continue;
                }

                participant.ServerApplyGrowth(_state);
            }
        }

        private void TrySubscribeToEventBus()
        {
            if (_hasEventBusSubscription)
            {
                return;
            }

            if (!GameEventBus.HasInstance)
            {
                if (_eventBusWaitRoutine == null)
                {
                    _eventBusWaitRoutine = StartCoroutine(CoWaitForEventBus());
                }

                return;
            }

            SubscribeToEventBus(GameEventBus.Instance);
        }

        private IEnumerator CoWaitForEventBus()
        {
            while ((!_hasEventBusSubscription) && (isActiveAndEnabled) && (!GameEventBus.HasInstance))
            {
                yield return null;
            }

            if ((!_hasEventBusSubscription) && (isActiveAndEnabled) && GameEventBus.HasInstance)
            {
                SubscribeToEventBus(GameEventBus.Instance);
            }

            _eventBusWaitRoutine = null;
        }

        private void SubscribeToEventBus(GameEventBus bus)
        {
            if (bus == null)
            {
                return;
            }

            bus.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
            bus.Subscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
            bus.Subscribe<RunResetEvent>(HandleRunReset);
            _hasEventBusSubscription = true;
        }

        private void UnsubscribeFromEventBus()
        {
            if (!_hasEventBusSubscription)
            {
                return;
            }

            if (GameEventBus.HasInstance)
            {
                GameEventBus bus = GameEventBus.Instance;
                bus.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
                bus.Unsubscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                bus.Unsubscribe<RunResetEvent>(HandleRunReset);
            }

            _hasEventBusSubscription = false;
        }
    }
}
