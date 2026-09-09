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
    /// <summary> 런 동안 서버 적용 사실을 모으고 결과를 한 번 동결한다 </summary>
    public sealed class RunStatistics : NetworkBehaviour, IRunStatisticsSink,
        IRunResultFactsProvider, IRunResultProvider
    {
        private enum CollectionState
        {
            Idle       = 0,
            Collecting = 1,
            Frozen     = 2,
        }

        private sealed class ParticipantTotals
        {
            public readonly ulong ParticipantIdValue;

            public bool          HasCharacter;
            public CharacterKind Character;
            public bool          HasHealth;
            public float         RemainingHealth;

            public float DamageDealt;
            public float HealingDone;

            public int RescueCount;
            public int SwallowedRescueCount;
            public int DeathCount;

            public int   ParryCounterCount;
            public int   PrecisionCounterCount;
            public int   ResonanceCounterCount;
            public float CounterReductionTotal;

            public ParticipantTotals(ulong participantIdValue)
            {
                ParticipantIdValue = participantIdValue;
            }

            public RunParticipantResultSnapshot ToSnapshot()
            {
                return new RunParticipantResultSnapshot
                {
                    ParticipantIdValue    = ParticipantIdValue,
                    HasCharacter          = HasCharacter,
                    Character             = Character,
                    DamageDealt           = DamageDealt,
                    HealingDone           = HealingDone,
                    RescueCount           = RescueCount,
                    SwallowedRescueCount  = SwallowedRescueCount,
                    DeathCount            = DeathCount,
                    ParryCounterCount      = ParryCounterCount,
                    PrecisionCounterCount  = PrecisionCounterCount,
                    ResonanceCounterCount  = ResonanceCounterCount,
                    CounterReductionTotal = CounterReductionTotal,
                    RemainingHealth       = RemainingHealth,
                };
            }
        }

        private readonly struct LifeKey : IEquatable<LifeKey>
        {
            private readonly RunPlayerLifeFactKind _kind;
            private readonly ulong                 _actor;
            private readonly ulong                 _target;
            private readonly uint                  _generation;

            public LifeKey(in RunPlayerLifeFact fact)
            {
                _kind       = fact.Kind;
                _actor      = fact.Actor.Value;
                _target     = fact.Target.Value;
                _generation = fact.DeathGeneration;
            }

            public bool Equals(LifeKey other)
            {
                return ((_kind == other._kind) && (_actor == other._actor) &&
                    (_target == other._target) && (_generation == other._generation));
            }

            public override bool Equals(object obj) => ((obj is LifeKey other) && Equals(other));

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int) _kind;
                    hash = (hash * 397) ^ _actor.GetHashCode();
                    hash = (hash * 397) ^ _target.GetHashCode();
                    return (hash * 397) ^ _generation.GetHashCode();
                }
            }
        }

        private readonly struct CounterKey : IEquatable<CounterKey>
        {
            private readonly RunCounterKind _kind;
            private readonly ulong          _actor;
            private readonly uint           _sceneRevision;
            private readonly uint           _entity;
            private readonly byte           _part;
            private readonly uint           _attack;

            public CounterKey(in RunCounterFact fact)
            {
                _kind          = fact.Kind;
                _actor         = fact.Actor.Value;
                _sceneRevision = fact.Target.SceneRevisionValue;
                _entity        = fact.Target.EntityNetId;
                _part          = fact.Target.PartId;
                _attack        = fact.AttackInstanceId;
            }

            public bool Equals(CounterKey other)
            {
                return ((_kind == other._kind) && (_actor == other._actor) &&
                    (_sceneRevision == other._sceneRevision) && (_entity == other._entity) &&
                    (_part == other._part) && (_attack == other._attack));
            }

            public override bool Equals(object obj) => ((obj is CounterKey other) && Equals(other));

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int) _kind;
                    hash = (hash * 397) ^ _actor.GetHashCode();
                    hash = (hash * 397) ^ _sceneRevision.GetHashCode();
                    hash = (hash * 397) ^ _entity.GetHashCode();
                    hash = (hash * 397) ^ _part.GetHashCode();
                    return (hash * 397) ^ _attack.GetHashCode();
                }
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private ScoreRuleSO _scoreRule = null;

        private readonly Dictionary<ulong, ParticipantTotals> _participants =
            new Dictionary<ulong, ParticipantTotals>();
        private readonly HashSet<uint> _enemyDeaths = new HashSet<uint>();
        private readonly HashSet<LifeKey> _lifeFacts = new HashSet<LifeKey>();
        private readonly HashSet<CounterKey> _counterFacts = new HashSet<CounterKey>();
        private readonly List<RunParticipantStateFact> _participantStateBuffer =
            new List<RunParticipantStateFact>(Constants.Network.MAX_PLAYERS);
        private readonly List<ulong> _participantIdBuffer =
            new List<ulong>(Constants.Network.MAX_PLAYERS);

        private CollectionState _state = CollectionState.Idle;
        private RunId _activeRun = default;
        private RunResultSnapshot _frozenFacts = default;
        private uint _lastResultRevision = 0u;

        private bool _hasEnemyDiedSubscription = false;
        private Coroutine _eventBusWaitRoutine = null;

        /****************************************
        *              Properties
        ****************************************/

        public int ScoreRuleVersion
        {
            get
            {
                ScoreRuleSO scoreRule = ResolveScoreRule();
                return (scoreRule == null) ? 0 : scoreRule.ScoreRuleVersion;
            }
        }

        /****************************************
        *           Network Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ResolveScoreRule();
            RunStatisticsSinkLocator.Register(this);
            RunResultFactsProviderLocator.Register(this);
            RunResultProviderLocator.Register(this);
            SubscribeEnemyDied();
        }

        public override void OnStopServer()
        {
            UnsubscribeEnemyDied();
            RunStatisticsSinkLocator.Unregister(this);
            RunResultFactsProviderLocator.Unregister(this);
            RunResultProviderLocator.Unregister(this);
            ClearRunState();
        }

        /****************************************
        *        Result Provider Members
        ****************************************/

        public int CalculateScore(in RunResultSnapshot facts)
        {
            ScoreRuleSO scoreRule = ResolveScoreRule();
            if ((scoreRule == null) || (!facts.HasAuthoritativeFacts))
            {
                return 0;
            }

            float remainingSeconds = Mathf.Max(0f,
                facts.ScoreTimeLimitSeconds - facts.ScoreElapsedSeconds);
            return scoreRule.CalculateFinalScore(facts.BossKilled, facts.EnemyKills,
                remainingSeconds, facts.RemainingCurrency, facts.RemainingHealthSum,
                facts.DeathCount);
        }

        public RunEvaluationGrade CalculateGrade(int score)
        {
            ScoreRuleSO scoreRule = ResolveScoreRule();
            return (scoreRule == null)
                ? RunEvaluationGrade.None
                : scoreRule.CalculateEvaluationGrade(score);
        }

        public string GetEvaluationMessage(RunEvaluationGrade grade)
        {
            ScoreRuleSO scoreRule = ResolveScoreRule();
            return (scoreRule == null) ? string.Empty : scoreRule.GetEvaluationMessage(grade);
        }

        /****************************************
        *         Facts Provider Members
        ****************************************/

        public bool BeginRun(RunId run)
        {
            if (!run.IsValid)
            {
                return false;
            }

            if ((_state == CollectionState.Collecting) && (_activeRun == run))
            {
                return true;
            }

            if ((_state == CollectionState.Frozen) && (_activeRun == run))
            {
                return false;
            }

            ClearRunState();
            _activeRun = run;
            _state     = CollectionState.Collecting;
            CaptureLiveParticipantStates();
            return true;
        }

        public bool TryFreeze(ref RunResultSnapshot snapshot)
        {
            RunId requestedRun = new RunId(snapshot.RunIdValue);
            if ((!requestedRun.IsValid) || (_activeRun != requestedRun) ||
                (_state == CollectionState.Idle))
            {
                return false;
            }

            if (_state == CollectionState.Frozen)
            {
                snapshot = _frozenFacts;
                return true;
            }

            CaptureLiveParticipantStates();
            if (!TryBuildParticipantSnapshots(out RunParticipantResultSnapshot[] participants,
                out int deaths, out float remainingHealth))
            {
                return false;
            }

            if (!PartyWalletLocator.HasProvider)
            {
                return false;
            }

            if (_lastResultRevision == uint.MaxValue)
            {
                return false;
            }

            ++_lastResultRevision;
            snapshot.EnemyKills         = _enemyDeaths.Count;
            snapshot.DeathCount         = deaths;
            snapshot.RemainingCurrency  = PartyWalletLocator.Current.Balance;
            snapshot.RemainingHealthSum = remainingHealth;
            snapshot.Participants       = participants;
            snapshot.ResultRevision     = _lastResultRevision;
            snapshot.FactsState         = RunResultFactsState.Authoritative;

            _frozenFacts = snapshot;
            _state       = CollectionState.Frozen;
            return true;
        }

        public void ResetRun(RunId run)
        {
            if (run.IsValid && (_activeRun.IsValid) && (_activeRun != run))
            {
                return;
            }

            ClearRunState();
        }

        /****************************************
        *          Statistics Sink Members
        ****************************************/

        public bool RecordDamage(in RunAppliedDamageFact fact)
        {
            if ((!CanRecord(fact.Run)) || (!fact.IsValid) ||
                (fact.Target.RunIdValue != fact.Run.Value))
            {
                return false;
            }

            ParticipantTotals totals = GetOrCreate(fact.Attacker);
            totals.DamageDealt = SaturatingAdd(totals.DamageDealt, fact.AppliedAmount);
            return true;
        }

        public bool RecordHealing(in RunAppliedHealingFact fact)
        {
            if ((!CanRecord(fact.Run)) || (!fact.IsValid))
            {
                return false;
            }

            GetOrCreate(fact.Target);
            ParticipantTotals healer = GetOrCreate(fact.Healer);
            if (fact.Healer != fact.Target)
            {
                healer.HealingDone = SaturatingAdd(healer.HealingDone, fact.AppliedAmount);
            }

            return true;
        }

        public bool RecordLife(in RunPlayerLifeFact fact)
        {
            if ((!CanRecord(fact.Run)) || (!fact.IsValid) ||
                (!_lifeFacts.Add(new LifeKey(fact))))
            {
                return false;
            }

            if (fact.Kind == RunPlayerLifeFactKind.Death)
            {
                ParticipantTotals target = GetOrCreate(fact.Target);
                target.DeathCount = SaturatingIncrement(target.DeathCount);
                return true;
            }

            ParticipantTotals actor = GetOrCreate(fact.Actor);
            if (fact.Kind == RunPlayerLifeFactKind.Rescue)
            {
                actor.RescueCount = SaturatingIncrement(actor.RescueCount);
            }
            else
            {
                actor.SwallowedRescueCount = SaturatingIncrement(actor.SwallowedRescueCount);
            }

            return true;
        }

        public bool RecordCounter(in RunCounterFact fact)
        {
            if ((!CanRecord(fact.Run)) || (!fact.IsValid) ||
                (fact.Target.RunIdValue != fact.Run.Value) ||
                (!_counterFacts.Add(new CounterKey(fact))))
            {
                return false;
            }

            ParticipantTotals actor = GetOrCreate(fact.Actor);
            actor.CounterReductionTotal = SaturatingAdd(
                actor.CounterReductionTotal, fact.AppliedAmount);

            if (fact.Kind == RunCounterKind.Parry)
            {
                actor.ParryCounterCount = SaturatingIncrement(actor.ParryCounterCount);
            }
            else if (fact.Kind == RunCounterKind.Precision)
            {
                actor.PrecisionCounterCount = SaturatingIncrement(actor.PrecisionCounterCount);
            }
            else
            {
                actor.ResonanceCounterCount = SaturatingIncrement(actor.ResonanceCounterCount);
            }

            return true;
        }

        public bool RecordParticipantState(in RunParticipantStateFact fact)
        {
            if ((!CanRecord(fact.Run)) || (!fact.IsValid))
            {
                return false;
            }

            ParticipantTotals totals = GetOrCreate(fact.Participant);
            totals.HasHealth       = true;
            totals.RemainingHealth = fact.RemainingHealth;

            if (fact.HasCharacter)
            {
                totals.HasCharacter = true;
                totals.Character    = fact.Character;
            }

            return true;
        }

        /// <summary> 기존 적 사망 이벤트를 런당 netId 한 번만 처치 수로 인정한다 </summary>
        public bool RecordEnemyDeath(in EnemyDiedEvent fact)
        {
            if ((_state != CollectionState.Collecting) || (!fact.IsValid) || fact.IsBoss)
            {
                return false;
            }

            return _enemyDeaths.Add(fact.EnemyNetId);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleEnemyDied(EnemyDiedEvent fact)
        {
            RecordEnemyDeath(fact);
        }

        private ParticipantTotals GetOrCreate(ParticipantId participant)
        {
            if (!_participants.TryGetValue(participant.Value, out ParticipantTotals totals))
            {
                totals = new ParticipantTotals(participant.Value);
                _participants.Add(participant.Value, totals);
            }

            return totals;
        }

        private bool CanRecord(RunId run)
        {
            return ((_state == CollectionState.Collecting) && (_activeRun == run));
        }

        private void CaptureLiveParticipantStates()
        {
            RunResultParticipantSourceLocator.Capture(_activeRun, _participantStateBuffer);
            for (int i = 0; i < _participantStateBuffer.Count; ++i)
            {
                RunParticipantStateFact state = _participantStateBuffer[i];
                RecordParticipantState(state);
            }
        }

        private bool TryBuildParticipantSnapshots(out RunParticipantResultSnapshot[] snapshots,
            out int deathCount, out float remainingHealth)
        {
            snapshots       = null;
            deathCount      = 0;
            remainingHealth = 0f;

            if (_participants.Count == 0)
            {
                return false;
            }

            _participantIdBuffer.Clear();
            foreach (KeyValuePair<ulong, ParticipantTotals> pair in _participants)
            {
                ParticipantTotals totals = pair.Value;
                if ((!totals.HasCharacter) || (!totals.HasHealth))
                {
                    return false;
                }

                _participantIdBuffer.Add(pair.Key);
            }

            _participantIdBuffer.Sort();
            snapshots = new RunParticipantResultSnapshot[_participantIdBuffer.Count];
            for (int i = 0; i < _participantIdBuffer.Count; ++i)
            {
                ParticipantTotals totals = _participants[_participantIdBuffer[i]];
                snapshots[i]     = totals.ToSnapshot();
                deathCount      = SaturatingAdd(deathCount, totals.DeathCount);
                remainingHealth = SaturatingAdd(remainingHealth, totals.RemainingHealth);
            }

            return true;
        }

        private static int SaturatingIncrement(int value)
        {
            return (value == int.MaxValue) ? int.MaxValue : value + 1;
        }

        private static int SaturatingAdd(int left, int right)
        {
            return (left > int.MaxValue - right) ? int.MaxValue : left + right;
        }

        private static float SaturatingAdd(float left, float right)
        {
            double total = (double) left + right;
            return (total >= float.MaxValue) ? float.MaxValue : (float) total;
        }

        private ScoreRuleSO ResolveScoreRule()
        {
            if ((_scoreRule == null) && GameDB.IsInitialized)
            {
                _scoreRule = GameDB.Score;
            }

            return _scoreRule;
        }

        private void ClearRunState()
        {
            _participants.Clear();
            _enemyDeaths.Clear();
            _lifeFacts.Clear();
            _counterFacts.Clear();
            _participantStateBuffer.Clear();
            _participantIdBuffer.Clear();
            _activeRun  = default;
            _frozenFacts = default;
            _state      = CollectionState.Idle;
        }

        private void SubscribeEnemyDied()
        {
            if (GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
                _hasEnemyDiedSubscription = true;
                return;
            }

            _eventBusWaitRoutine = StartCoroutine(CoWaitForEventBus());
        }

        private IEnumerator CoWaitForEventBus()
        {
            while (!GameEventBus.HasInstance)
            {
                yield return null;
            }

            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
            _hasEnemyDiedSubscription = true;
            _eventBusWaitRoutine      = null;
        }

        private void UnsubscribeEnemyDied()
        {
            if (_eventBusWaitRoutine != null)
            {
                StopCoroutine(_eventBusWaitRoutine);
                _eventBusWaitRoutine = null;
            }

            if ((!_hasEnemyDiedSubscription) || (!GameEventBus.HasInstance))
            {
                _hasEnemyDiedSubscription = false;
                return;
            }

            GameEventBus.Instance.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
            _hasEnemyDiedSubscription = false;
        }
    }
}
