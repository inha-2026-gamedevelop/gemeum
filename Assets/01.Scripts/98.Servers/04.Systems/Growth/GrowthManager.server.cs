// System
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    public partial class GrowthManager
    {
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

        /// <summary>
        /// 목표 레벨까지 필요한 경험치를 한 번에 넣는다. 보스 트라이가 30분을 건너뛰고 시작할 때 쓴다.
        /// 레벨을 직접 대입하지 않는 이유는 최대 HP와 특성 포인트가 레벨 곡선에서 함께 나오기 때문이다
        /// </summary>
        [Server]
        public bool ServerTrySetPartyLevel(int targetLevel)
        {
            if ((!_isRunActive) || (!_hasState) || (!GameDB.IsInitialized) || (GameDB.LevelCurve == null))
            {
                return false;
            }

            if (_state.Level >= targetLevel)
            {
                return true;
            }

            if (!GrowthRules.TryGetCumulativeExperience(GameDB.LevelCurve, targetLevel, out int required))
            {
                Debug.LogError($"[GrowthManager] 레벨 곡선에 {targetLevel}레벨이 없어 시작 레벨을 맞추지 못했다", this);
                return false;
            }

            int missing = (required - _state.Experience);
            return ((missing <= 0) || ApplyExperience(missing));
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
