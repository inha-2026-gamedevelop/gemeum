// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 플레이어 HP, 스태미나, 신력, 생명 상태의 원본. IDamageable 구현 </summary>
    public class PlayerStatus : NetworkBehaviour, IDamageable, IDamagePresentationSource, IRecoveryTarget,
        IResourceTransactionTarget, IRunStateCarrier, IGrowthParticipant, IBossSwallowTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthChanged))]    private float           _health    = 0f;
        [SyncVar(hook = nameof(OnMaxHealthChanged))] private float           _maxHealth = 0f;
        [SyncVar(hook = nameof(OnStaminaChanged))]   private float           _stamina   = 0f;
        [SyncVar(hook = nameof(OnSpiritChanged))]    private float           _spirit    = 0f;
        [SyncVar(hook = nameof(OnLifeStateChanged))] private PlayerLifeState _lifeState = PlayerLifeState.Alive;
        [SyncVar] private bool            _isInvincible = false; // 일시 무적

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
        // 개발용 무한 자원. SyncVar인 이유는 켠 사람 화면에 상태를 보여 주기 위해서다
        // 판정은 전부 서버에서만 본다
        [SyncVar] private bool            _devInfiniteResources = false;
#endif
        [SyncVar] private int             _level       = Constants.Combat.START_LEVEL;
        [SyncVar] private int             _experience  = 0;
        [SyncVar] private uint            _growthRevision = 0u;
        [SyncVar] private uint            _resourceRevision = 0u;
        [SyncVar] private uint            _swallowGeneration = 0u;
        [SyncVar] private double          _swallowedEndGameplayTime = 0.0;

        private float _serverHealth;
        private float _serverSpirit;
        private uint  _serverResourceRevision;
        private bool  _resourceStateInitialized;
        private bool  _resourceTransactionLocked;
        private uint  _serverDamageGeneration;
        [SyncVar] private PlayerHitReactionCue _hitReaction;
        private double _nextHitReactionGameplayTime;
        private PlayerAttack _hitAttack;
        private PrecisionShotController _hitPrecision;
        private HealController _hitHeal;
        private PlayerRollController _hitRoll;
        private ParryController _hitParry;

        private void Awake()
        {
            _hitAttack = GetComponent<PlayerAttack>();
            _hitPrecision = GetComponent<PrecisionShotController>();
            _hitHeal = GetComponent<HealController>();
            _hitRoll = GetComponent<PlayerRollController>();
            _hitParry = GetComponent<ParryController>();
        }

        public PlayerHitReactionCue HitReaction => _hitReaction;
        public bool IsHitReacting => !IsDead && !IsSwallowed &&
            _hitReaction.IsActiveAt(RunTimeLocator.Current.GameplayElapsedSeconds);

        private void ResetHitReaction()
        {
            _hitReaction = default;
            _nextHitReactionGameplayTime = 0d;
        }

        [Server]
        private void ServerReactToHit(in DamageInfo damage)
        {
            if (IsDead || IsSwallowed)
            {
                _hitAttack?.ResetForNewRun();
                ResetHitReaction();
                return;
            }

            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            if (damage.AttackerNetId == DamageInfo.NO_ATTACKER || !IsFinite(now) ||
                now < _nextHitReactionGameplayTime || !GameDB.IsInitialized)
            {
                return;
            }

            float duration = GameDB.Combat.PlayerHitReactionDuration;
            float resistance = GameDB.Combat.PlayerHitReactionResistanceDuration;
            if (!IsFinite(duration) || duration <= 0f || !IsFinite(resistance))
            {
                return;
            }

            _hitReaction = new PlayerHitReactionCue
            {
                Generation = _serverDamageGeneration,
                StartGameplayTime = now,
                EndGameplayTime = now + duration,
            };
            _nextHitReactionGameplayTime = _hitReaction.EndGameplayTime + Math.Max(0f, resistance);
            _hitAttack?.ServerInterruptForHit();
            _hitPrecision?.ServerInterruptForHit();
            _hitHeal?.ServerInterruptForHit();
            _hitRoll?.ServerInterruptForHit();
            _hitParry?.ServerInterruptForHit();
        }

        // 서버가 스폰한 플레이어 전부
        private static readonly List<PlayerStatus> G_ServerPlayers = new List<PlayerStatus>();
        public static IReadOnlyList<PlayerStatus> ServerPlayers => G_ServerPlayers;

        private double _lastStaminaSpendServerTime = -1.0; // 서버 전용

        private double _invincibleUntilServerTime = 0.0; // 서버 전용

        private double _reviveInvulnerableUntilServerTime = 0.0; // 서버 전용

        private bool _isRunStateReady = false; // 서버 전용

        private ParticipantId _participant = ParticipantId.None;
        [SyncVar] private ParticipantKind _replicatedParticipantKind;
        [SyncVar] private ulong _replicatedParticipantValue;

        /****************************************
        *              Properties
        ****************************************/

        public float           Health       => (_resourceStateInitialized && isServer) ? _serverHealth : _health;
        public float           MaxHealth    => _maxHealth;
        public float           Stamina      => _stamina;
        public float           Spirit       => (_resourceStateInitialized && isServer) ? _serverSpirit : _spirit;
        public PlayerLifeState LifeState    => _lifeState;
        public bool            IsInvincible => _isInvincible;
        public int             Level       => _level;
        public int             Experience  => _experience;
        public uint            GrowthRevision => _growthRevision;
        public uint            ResourceRevision => (_resourceStateInitialized && isServer)
            ? _serverResourceRevision : _resourceRevision;
        public bool            IsResourceTransactionLocked => _resourceTransactionLocked;
        public ParticipantId   Participant  => isServer ? _participant :
            new ParticipantId(_replicatedParticipantKind, _replicatedParticipantValue);
        public bool            IsSwallowed  => (_lifeState == PlayerLifeState.Swallowed);
        public uint            SwallowGeneration => _swallowGeneration;
        public uint            DamageGeneration => _serverDamageGeneration;

        public double SwallowedRemainingSeconds
        {
            get
            {
                if (!IsSwallowed)
                {
                    return 0.0;
                }

                double gameplayTime = RunTimeLocator.Current.GameplayElapsedSeconds;
                if (!IsFinite(gameplayTime) || !IsFinite(_swallowedEndGameplayTime))
                {
                    return 0.0;
                }

                return Math.Max(0.0, _swallowedEndGameplayTime - gameplayTime);
            }
        }

        public GrowthState Growth
        {
            get
            {
                int talentPoints = 0;
                if (GameDB.IsInitialized && (GameDB.LevelCurve != null))
                {
                    talentPoints = Mathf.Max(0,
                        (_level - Constants.Combat.START_LEVEL) * GameDB.LevelCurve.TalentPointPerLevel);
                }

                return new GrowthState(_level, _experience, _maxHealth, _growthRevision, talentPoints);
            }
        }

        /// <summary> 관전 중이거나 쓰러진 상태. 부활 무적은 살아 있는 것으로 센다 </summary>
        public bool IsDead => ((_lifeState == PlayerLifeState.Downed) || (_lifeState == PlayerLifeState.Spectating));

        /// <summary>
        /// 이 화면의 주인. UI가 체력 바를 누구 것으로 그릴지 여기서 정한다
        /// 관전 중에도 자기 인스턴스는 그대로 남으므로 바뀌지 않는다
        /// </summary>
        public static PlayerStatus Local { get; private set; }

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 체력이 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> HealthChanged;

        /// <summary> 동기화된 체력이 줄었다. 피격 플래시가 쓴다 </summary>
        public event System.Action HealthDecreased;
        public event System.Action<uint> ServerDamagedBy;

        /// <summary> 스태미나가 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> StaminaChanged;

        /// <summary> 정신력이 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> SpiritChanged;

        /// <summary> 생명 상태가 바뀌었다 </summary>
        public event System.Action<PlayerLifeState> LifeStateChanged;

        /// <summary> 공용 성장 상태가 바뀌었을 때 서버에서 발생한다 </summary>
        public event System.Action<GrowthState> GrowthChanged;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartLocalPlayer()
        {
            Local = this;

            LocalCombatState.Bind(gameObject);

            RaiseAll();
        }

        public override void OnStopLocalPlayer()
        {
            if (Local != this)
            {
                return;
            }

            Local = null;

            LocalCombatState.Unbind();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnHealthChanged(float oldValue, float newValue)
        {
            NotifyResourceObservers(HealthChanged, newValue, _maxHealth, "체력");

            // 서버가 확정한 SyncVar라 세 화면이 같은 값을 찍는다
            float delta = (newValue - oldValue);
            GDebug.Log(
                (delta < 0f) ? GLogKind.Fail : GLogKind.Success,
                $"{GDebug.Who(netId)} HP {oldValue:F0} -> {newValue:F0} ({delta:+0;-0}) / {_maxHealth:F0}");

            if (newValue < oldValue)
            {
                SfxPlayer.Play(SoundKey.PlayerHurt, transform.position, isLocalPlayer, netId);
                HealthDecreased?.Invoke();
            }
        }

        private void OnMaxHealthChanged(float oldValue, float newValue)
        {
            NotifyResourceObservers(HealthChanged, _health, newValue, "체력");
        }

        private void OnStaminaChanged(float oldValue, float newValue)
        {
            NotifyResourceObservers(StaminaChanged, newValue, GetStaminaMax(), "스태미나");
        }

        private void OnSpiritChanged(float oldValue, float newValue)
        {
            NotifyResourceObservers(SpiritChanged, newValue, GetSpiritMax(), "신력");
        }

        private void OnLifeStateChanged(PlayerLifeState oldValue, PlayerLifeState newValue)
        {
            NotifyLifeStateObservers(LifeStateChanged, newValue);
        }

        /// <summary> 지금 값을 그대로 한 번 더 알린다. UI가 뒤늦게 구독했을 때 쓴다 </summary>
        public void RaiseAll()
        {
            NotifyResourceObservers(HealthChanged, _health, _maxHealth, "체력");
            NotifyResourceObservers(StaminaChanged, _stamina, GetStaminaMax(), "스태미나");
            NotifyResourceObservers(SpiritChanged, _spirit, GetSpiritMax(), "신력");
            NotifyLifeStateObservers(LifeStateChanged, _lifeState);
        }

        private void NotifyResourceObservers(Action<float, float> handlers, float current,
            float maximum, string resourceName)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<float, float>) observers[i])(current, maximum);
                }
                catch (Exception e)
                {
                    // SyncVar hook 밖으로 예외가 나가면 Mirror hook guard도 복구되지 않는다
                    Debug.LogError($"[PlayerStatus] {resourceName} observer 실패: {e}", this);
                }
            }
        }

        private void NotifyLifeStateObservers(Action<PlayerLifeState> handlers,
            PlayerLifeState state)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<PlayerLifeState>) observers[i])(state);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PlayerStatus] 생명 상태 observer 실패: {e}", this);
                }
            }
        }

        private static float GetStaminaMax()
        {
            return GameDB.IsInitialized ? GameDB.Combat.StaminaMax : 0f;
        }

        private static float GetSpiritMax()
        {
            return GameDB.IsInitialized ? GameDB.Combat.SpiritMax : 0f;
        }

        public override void OnStartServer()
        {
            G_ServerPlayers.Add(this);
            BossSwallowTargetRegistry.Register(this);

            if (!TryApplyStartingState())
            {
                return;
            }

            RegisterGrowthParticipant();
            ServerRecordHealthState();

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[PlayerStatus] AuthorityTickScheduler가 없어 스태미나 회복을 돌릴 수 없다", this);
                return;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_STATUS, ServerRegenTick);
        }

        public override void OnStopServer()
        {
            CombatClock.Forget(netId);
            G_ServerPlayers.Remove(this);
            BossSwallowTargetRegistry.Unregister(this);
            GrowthParticipantLocator.Unregister(this);
            ParticipantDirectory.Unregister(_participant, netIdentity);
            _participant = ParticipantId.None;
            _resourceTransactionLocked = false;
            _resourceStateInitialized  = false;

            // 씬 전환 중에는 ServerCheckPartyWipe 안의 IsServerTransitioning 가드가 전멸 판정을 막는다
            ServerCheckPartyWipe();

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerRegenTick);
        }

        /// <summary> 살아 있는 사람이 하나도 남지 않았으면 런을 전멸로 끝낸다 </summary>
        public static void ServerCheckPartyWipe()
        {
            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                return;
            }

            if (G_ServerPlayers.Count == 0)
            {
                return;
            }

            if (RunSceneDirector.IsServerTransitioning)
            {
                return;
            }

            int readyCount = 0;
            for (int i = 0; i < G_ServerPlayers.Count; ++i)
            {
                if (!G_ServerPlayers[i]._isRunStateReady)
                {
                    continue;
                }

                ++readyCount;

                if (!G_ServerPlayers[i].IsDead)
                {
                    return;
                }
            }

            if (readyCount == 0)
            {
                return;
            }

            if (run.TryFinishRun(RunOutcome.PartyWipe))
            {
                Debug.Log($"[PlayerStatus] 전멸로 런을 끝냈다: 판정 대상 {readyCount} / 전체 {G_ServerPlayers.Count}");
            }
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 20;

        ParticipantId IGrowthParticipant.Participant => _participant;
        GrowthState IGrowthParticipant.Growth => Growth;

        /// <summary> 지금 자원과 생명 상태를 담는다. 최대 HP는 담지 않는다 - 레벨에서 다시 계산한다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            snapshot.Health    = Health;
            snapshot.Stamina   = _stamina;
            snapshot.Spirit    = Spirit;
            snapshot.LifeState = _lifeState.ToSnapshotState();
            snapshot.SwallowedRemainingSeconds = SwallowedRemainingSeconds;
            snapshot.SwallowGeneration = _swallowGeneration;
            snapshot.Level     = _level;
            snapshot.Experience = _experience;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) || (!IsFinite(snapshot.Health)) ||
                (!IsFinite(snapshot.Stamina)) || (!IsFinite(snapshot.Spirit)) ||
                (snapshot.Health < 0f) || (snapshot.Stamina < 0f) ||
                (snapshot.Spirit < 0f))
            {
                failure = "자원 값이 음수다";
                return false;
            }

            if (!snapshot.LifeState.TryToLifeState(out PlayerLifeState _))
            {
                failure = $"되돌릴 수 없는 생명 상태다: {snapshot.LifeState}";
                return false;
            }

            if (!TryValidateSwallowSnapshot(snapshot, out failure))
            {
                return false;
            }

            if ((snapshot.Level < Constants.Combat.START_LEVEL) ||
                (snapshot.Level > Constants.Combat.MAX_LEVEL) || (snapshot.Experience < 0))
            {
                failure = "성장 값이 유효하지 않다";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary> 담아 둔 자원과 생명 상태를 되붓는다 </summary>
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (_resourceTransactionLocked || (snapshot == null) ||
                (!snapshot.LifeState.TryToLifeState(out PlayerLifeState lifeState)) ||
                (!TryValidateSwallowSnapshot(snapshot, out _)))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.IsInitialized ? GameDB.Combat : null;

            if (!TryResolveRestoreGrowth(snapshot, out GrowthState growth, out float snapshotMaxHealth,
                out bool hasAuthoritativeGrowth))
            {
                return false;
            }

            if (snapshot.Participant.IsValid)
            {
                if (!ParticipantDirectory.Register(snapshot.Participant, netIdentity))
                {
                    return false;
                }

                SetParticipant(snapshot.Participant);
                GrowthParticipantLocator.Register(this);
            }

            float restoredHealth = snapshot.Health;
            if (hasAuthoritativeGrowth && !BlocksHealthGrowth(lifeState))
            {
                float maxHealthDelta = growth.MaxHealth - snapshotMaxHealth;
                if (maxHealthDelta > 0f)
                {
                    restoredHealth += maxHealthDelta;
                }
            }

            _level          = growth.Level;
            _experience     = growth.Experience;
            _maxHealth      = growth.MaxHealth;
            _growthRevision = growth.Revision;
            _serverHealth   = Mathf.Clamp(restoredHealth, 0f, _maxHealth);
            _stamina        = (combat == null) ? snapshot.Stamina :
                Mathf.Clamp(snapshot.Stamina, 0f, combat.StaminaMax);
            _serverSpirit   = (combat == null) ? snapshot.Spirit :
                Mathf.Clamp(snapshot.Spirit, 0f, combat.SpiritMax);
            _lifeState      = lifeState;
            ResetHitReaction();
            _swallowGeneration = snapshot.SwallowGeneration;
            _swallowedEndGameplayTime = (lifeState == PlayerLifeState.Swallowed)
                ? RunTimeLocator.Current.GameplayElapsedSeconds +
                    snapshot.SwallowedRemainingSeconds
                : 0.0;

            _serverResourceRevision    = 0u;
            _resourceRevision          = 0u;
            _resourceStateInitialized  = true;
            _resourceTransactionLocked = false;
            _health                    = _serverHealth;
            _spirit                    = _serverSpirit;

            _isRunStateReady = true;
            ServerRecordHealthState();

            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런이 끝나 로비로 돌아갈 때 HP, 스태미나, 신력, 생명 상태를 시작값으로 되돌린다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            if (_resourceTransactionLocked)
            {
                Debug.LogError("[PlayerStatus] 자원 거래 통지 중 런 초기화를 시도했다", this);
                return;
            }

            _isInvincible                      = false;
            _invincibleUntilServerTime         = 0.0;
            _reviveInvulnerableUntilServerTime = 0.0;
            _lastStaminaSpendServerTime        = -1.0;

            TryApplyStartingState();
        }

        /// <summary> 서버가 참가자 신원을 확정하고 객체, 성장 목록에 등록한다 </summary>
        [Server]
        public bool ServerSetParticipantId(ParticipantId participant)
        {
            if ((!participant.IsValid) ||
                (!ParticipantDirectory.Register(participant, netIdentity)))
            {
                return false;
            }

            SetParticipant(participant);
            RegisterGrowthParticipant();
            return true;
        }

        /// <summary> 공용 성장 상태를 적용한다. 최대 HP 증가분만 현재 HP에 더한다 </summary>
        [Server]
        public void ServerApplyGrowth(GrowthState growth)
        {
            if (!growth.IsValid)
            {
                return;
            }

            if (_resourceTransactionLocked)
            {
                Debug.LogError("[PlayerStatus] 자원 거래 통지 중 성장 상태를 바꾸려 했다", this);
                return;
            }

            ServerEnsureResourceState();

            bool canReceiveMaxHealthDelta = (!IsDead) && (!IsSwallowed);
            float previousMaxHealth = _maxHealth;
            float previousHealth = _serverHealth;
            bool changed = ((_level != growth.Level) || (_experience != growth.Experience) ||
                (!Mathf.Approximately(_maxHealth, growth.MaxHealth)) ||
                (_growthRevision != growth.Revision));

            _level          = growth.Level;
            _experience     = growth.Experience;
            _maxHealth      = growth.MaxHealth;
            _growthRevision = growth.Revision;

            float maxHealthDelta = growth.MaxHealth - previousMaxHealth;
            if (canReceiveMaxHealthDelta && (maxHealthDelta > 0f))
            {
                _serverHealth += maxHealthDelta;
            }

            _serverHealth = Mathf.Clamp(_serverHealth, 0f, _maxHealth);
            bool resourceChanged = ((!Mathf.Approximately(previousMaxHealth, _maxHealth)) ||
                (!Mathf.Approximately(previousHealth, _serverHealth)));
            if (resourceChanged)
            {
                ServerAdvanceResourceRevision();
            }

            _health = _serverHealth;

            if (changed)
            {
                GrowthChanged?.Invoke(growth);
                NotifyResourceObservers(HealthChanged, _serverHealth, _maxHealth, "체력");
                ServerRecordHealthState();
            }
        }

        private void RegisterGrowthParticipant()
        {
            if (!_participant.IsValid)
            {
                _participant = ResolveParticipant();
            }

            if (!_participant.IsValid)
            {
                return;
            }

            if (!ParticipantDirectory.Register(_participant, netIdentity))
            {
                Debug.LogError($"[PlayerStatus] 참가자 중복 등록: {_participant}", this);
                return;
            }

            GrowthParticipantLocator.Register(this);
            _replicatedParticipantKind = _participant.Kind;
            _replicatedParticipantValue = _participant.Value;

            if (GrowthProviderLocator.HasProvider &&
                GrowthProviderLocator.Current.TryGetState(_participant, out GrowthState growth))
            {
                ServerApplyGrowth(growth);
            }
        }

        private void SetParticipant(ParticipantId participant)
        {
            if (_participant == participant)
            {
                return;
            }

            GrowthParticipantLocator.Unregister(this);
            ParticipantDirectory.Unregister(_participant, netIdentity);
            _participant = participant;
        }

        [Server]
        private ParticipantId ResolveParticipant()
        {
            if ((connectionToClient == null) || (GeumeumAuthenticator.ServerInstance == null))
            {
                return ParticipantId.None;
            }

            if (!GeumeumAuthenticator.ServerInstance.TryGetPlatformId(
                    connectionToClient.connectionId, out PlayerPlatformId platformId))
            {
                return ParticipantId.None;
            }

            return ParticipantId.FromPlatformId(platformId);
        }

        private bool TryResolveRestoreGrowth(RunParticipantSnapshot snapshot, out GrowthState growth,
            out float snapshotMaxHealth, out bool hasAuthoritativeGrowth)
        {
            growth = default;
            snapshotMaxHealth = 0f;
            hasAuthoritativeGrowth = false;

            LevelCurveSO curve = GameDB.IsInitialized ? GameDB.LevelCurve : null;
            if (GrowthProviderLocator.HasProvider)
            {
                if ((!snapshot.Participant.IsValid) ||
                    (!GrowthProviderLocator.Current.TryGetState(snapshot.Participant, out growth)) ||
                    (!growth.IsValid))
                {
                    return false;
                }

                hasAuthoritativeGrowth = true;
                if ((curve != null) && curve.TryGetLevel(snapshot.Level, out LevelCurveSO.LevelEntry oldEntry) &&
                    (oldEntry.MaxHealth > 0f) && !float.IsNaN(oldEntry.MaxHealth) &&
                    !float.IsInfinity(oldEntry.MaxHealth))
                {
                    snapshotMaxHealth = oldEntry.MaxHealth;
                }
                else
                {
                    snapshotMaxHealth = growth.MaxHealth;
                }

                return true;
            }

            if ((curve == null) || (snapshot.Level < Constants.Combat.START_LEVEL) ||
                (snapshot.Level > Constants.Combat.MAX_LEVEL) || (snapshot.Experience < 0) ||
                (!curve.TryGetLevel(snapshot.Level, out LevelCurveSO.LevelEntry entry)) ||
                (entry.MaxHealth <= 0f) || float.IsNaN(entry.MaxHealth) ||
                float.IsInfinity(entry.MaxHealth))
            {
                return false;
            }

            int talentPoints = Mathf.Max(0,
                (snapshot.Level - Constants.Combat.START_LEVEL) * curve.TalentPointPerLevel);
            growth = new GrowthState(snapshot.Level, snapshot.Experience, entry.MaxHealth, 1u,
                talentPoints);
            snapshotMaxHealth = entry.MaxHealth;
            return true;
        }

        private static bool TryValidateSwallowSnapshot(RunParticipantSnapshot snapshot,
            out string failure)
        {
            if (!IsFinite(snapshot.SwallowedRemainingSeconds) ||
                (snapshot.SwallowedRemainingSeconds < 0.0))
            {
                failure = "삼켜짐 남은 시간이 유효하지 않다";
                return false;
            }

            bool isSwallowed = (snapshot.LifeState == PlayerLifeSnapshotState.Swallowed);
            if (isSwallowed && ((snapshot.SwallowGeneration == 0u) ||
                (snapshot.Health > Constants.Combat.HEALTH_EPSILON)))
            {
                failure = "삼켜짐 상태와 세대 또는 체력이 맞지 않는다";
                return false;
            }

            if ((!isSwallowed) && (snapshot.SwallowedRemainingSeconds > 0.0))
            {
                failure = "삼켜짐 상태가 아닌데 제한 시간이 남아 있다";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool BlocksHealthGrowth(PlayerLifeState lifeState)
        {
            return ((lifeState == PlayerLifeState.Downed) ||
                (lifeState == PlayerLifeState.Spectating) ||
                (lifeState == PlayerLifeState.Swallowed));
        }

        private bool TryApplyStartingState()
        {
            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[PlayerStatus] GameDB가 초기화되지 않아 초기 상태를 정할 수 없다", this);
                return false;
            }

            if (!GameDB.LevelCurve.TryGetLevel(Constants.Combat.START_LEVEL, out LevelCurveSO.LevelEntry entry))
            {
                Debug.LogError($"[PlayerStatus] 레벨 {Constants.Combat.START_LEVEL}의 곡선 항목이 없다", this);
                return false;
            }

            CombatConfigSO combat = GameDB.Combat;

            _maxHealth      = entry.MaxHealth;
            _serverHealth   = _maxHealth;
            _stamina        = combat.StaminaMax;
            _serverSpirit   = combat.SpiritMax;
            _lifeState      = PlayerLifeState.Alive;
            _level          = Constants.Combat.START_LEVEL;
            _experience     = 0;
            _growthRevision = 1u;
            _swallowGeneration = 0u;
            _swallowedEndGameplayTime = 0.0;
            _serverDamageGeneration = 0u;
            ResetHitReaction();

            _serverResourceRevision    = 0u;
            _resourceRevision          = 0u;
            _resourceStateInitialized  = true;
            _resourceTransactionLocked = false;
            _health                    = _serverHealth;
            _spirit                    = _serverSpirit;

            _isRunStateReady = (!RunSceneDirector.IsServerTransitioning);
            ServerRecordHealthState();
            return true;
        }

        /****************************************
        *              Server Tick
        ****************************************/

        private void ServerRegenTick(float fixedDeltaTime)
        {
            if ((IsDead) || IsSwallowed || (!GameDB.IsInitialized))
            {
                return;
            }

            if ((_isInvincible) && (NetworkTime.time >= _invincibleUntilServerTime))
            {
                _isInvincible = false;
            }

            if ((_lifeState == PlayerLifeState.ReviveInvulnerable)
                && (NetworkTime.time >= _reviveInvulnerableUntilServerTime))
            {
                _lifeState = PlayerLifeState.Alive;
            }

            CombatConfigSO combat = GameDB.Combat;
            if (_stamina >= combat.StaminaMax)
            {
                return;
            }

            bool isDelayPassed = ((_lastStaminaSpendServerTime < 0.0)
                || ((NetworkTime.time - _lastStaminaSpendServerTime) >= combat.StaminaRegenDelay));

            if (!isDelayPassed)
            {
                return;
            }

            _stamina = Mathf.Min(combat.StaminaMax, _stamina + (combat.StaminaRegenPerSecond * fixedDeltaTime));
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 피해를 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public void TakeDamage(in DamageInfo damage)
        {
            ServerApplyDamage(damage, false, 0.0, 0f, out _, out _);
        }

        [Server]
        public BossSwallowDamageResult ServerApplyBossDamage(in DamageInfo damage,
            in BossSwallowDamageContext context)
        {
            ServerApplyDamage(damage, context.ConvertLethalToSwallowed,
                context.GameplayTime, context.SwallowDuration,
                out float appliedDamage, out bool enteredSwallowed);

            return new BossSwallowDamageResult(appliedDamage, enteredSwallowed,
                _participant, _swallowGeneration);
        }

        [Server]
        private void ServerApplyDamage(in DamageInfo damage, bool convertLethalToSwallowed,
            double gameplayTime, float swallowDuration, out float appliedDamage,
            out bool enteredSwallowed)
        {
            appliedDamage = 0f;
            enteredSwallowed = false;

            if (IsDead || IsSwallowed || _resourceTransactionLocked)
            {
                return;
            }

            if (_lifeState == PlayerLifeState.ReviveInvulnerable)
            {
                return;
            }

            if (_isInvincible)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
            if (_devInfiniteResources)
            {
                return;
            }
#endif

            if (!IsFinite(damage.Amount) ||
                (damage.Amount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            ServerEnsureResourceState();

            // 무적과 0 피해를 거른 뒤에 기록한다. 실제로 피해가 오간 것만 전투로 친다
            CombatClock.ServerNotifyDamage(netId, damage.AttackerNetId,
                RunTimeLocator.Current.GameplayElapsedSeconds);

            float previousHealth = _serverHealth;
            _serverHealth = Mathf.Max(0f, _serverHealth - damage.Amount);
            appliedDamage = previousHealth - _serverHealth;
            if (appliedDamage <= Constants.Combat.DAMAGE_EPSILON)
            {
                return;
            }

            AdvanceDamageGeneration();
            ServerDamagedBy?.Invoke(damage.AttackerNetId);
            ServerAdvanceResourceRevision();
            if (_serverHealth <= Constants.Combat.HEALTH_EPSILON)
            {
                bool canSwallow = convertLethalToSwallowed && IsFinite(gameplayTime) &&
                    IsFinite(swallowDuration) && (swallowDuration > 0f);
                if (canSwallow)
                {
                    AdvanceSwallowGeneration();
                    _lifeState = PlayerLifeState.Swallowed;
                    _swallowedEndGameplayTime = gameplayTime + swallowDuration;
                    enteredSwallowed = true;
                }
                else
                {
                    _lifeState = PlayerLifeState.Downed;
                    _swallowedEndGameplayTime = 0.0;
                }
            }

            _health = _serverHealth;
            ServerRecordHealthState();
            _hitAttack?.ServerDiscardCombatRewardsOnDamage();
            ServerReactToHit(in damage);
        }

        [Server]
        public bool ServerTryRescueSwallowed(ParticipantId rescuer,
            uint expectedGeneration, float restoreHealthRatio)
        {
            if ((!rescuer.IsValid) || (!_participant.IsValid) ||
                (rescuer == _participant) ||
                (!CanRestoreSwallowed(expectedGeneration, restoreHealthRatio)))
            {
                return false;
            }

            ServerRestoreSwallowed(restoreHealthRatio);
            RecordSwallowedRescueFact(rescuer, expectedGeneration);
            return true;
        }

        [Server]
        public bool ServerExpireSwallowed(double gameplayTime)
        {
            if (!IsSwallowed || !IsFinite(gameplayTime) ||
                !IsFinite(_swallowedEndGameplayTime) ||
                (gameplayTime < _swallowedEndGameplayTime))
            {
                return false;
            }

            _lifeState = PlayerLifeState.Downed;
            _swallowedEndGameplayTime = 0.0;
            ServerRecordHealthState();
            return true;
        }

        [Server]
        public bool ServerReleaseSwallowed(float restoreHealthRatio)
        {
            if (!CanRestoreSwallowed(_swallowGeneration, restoreHealthRatio))
            {
                return false;
            }

            ServerRestoreSwallowed(restoreHealthRatio);
            return true;
        }

        private bool CanRestoreSwallowed(uint expectedGeneration, float restoreHealthRatio)
        {
            return IsSwallowed && (!_resourceTransactionLocked) &&
                (expectedGeneration != 0u) &&
                (expectedGeneration == _swallowGeneration) &&
                IsFinite(restoreHealthRatio) &&
                (restoreHealthRatio > 0f) && (restoreHealthRatio <= 1f);
        }

        private void ServerRestoreSwallowed(float restoreHealthRatio)
        {
            ServerEnsureResourceState();
            _serverHealth = Mathf.Clamp(_maxHealth * restoreHealthRatio, 0f, _maxHealth);
            ServerAdvanceResourceRevision();
            _health = _serverHealth;
            _lifeState = PlayerLifeState.Alive;
            _swallowedEndGameplayTime = 0.0;
            ServerRecordHealthState();
        }

        private void AdvanceSwallowGeneration()
        {
            unchecked
            {
                ++_swallowGeneration;
            }

            if (_swallowGeneration == 0u)
            {
                _swallowGeneration = 1u;
            }
        }

        private void AdvanceDamageGeneration()
        {
            unchecked
            {
                ++_serverDamageGeneration;
            }

            if (_serverDamageGeneration == 0u)
            {
                _serverDamageGeneration = 1u;
            }
        }

        /// <summary> 사망 직후 전이 상태(Downed)를 관전 대기(Spectating)로 넘긴다 </summary>
        [Server]
        public bool ServerBeginSpectate()
        {
            if ((_lifeState != PlayerLifeState.Downed) || _resourceTransactionLocked)
            {
                return false;
            }

            _lifeState = PlayerLifeState.Spectating;
            return true;
        }

        /// <summary> 부활을 확정한다 </summary>
        [Server]
        public void ServerRevive(float healthRatio, float invulnerableDuration)
        {
            if ((!IsDead) || _resourceTransactionLocked)
            {
                return;
            }

            ServerEnsureResourceState();
            _serverHealth = Mathf.Clamp(_maxHealth * Mathf.Clamp01(healthRatio), 0f, _maxHealth);
            _stamina = GameDB.IsInitialized ? GameDB.Combat.StaminaMax : _stamina;
            ServerAdvanceResourceRevision();
            _health = _serverHealth;

            _isInvincible              = false;
            _invincibleUntilServerTime = 0.0;

            _lifeState                         = PlayerLifeState.ReviveInvulnerable;
            _reviveInvulnerableUntilServerTime = NetworkTime.time + invulnerableDuration;
            ServerRecordHealthState();
        }

        /// <summary> 회복을 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public bool TryRestoreHealth(float amount)
        {
            return TryRestoreHealth(amount, _participant, out _);
        }

        /// <summary> 회복 출처와 실제 적용량을 함께 확정한다, 서버에서만 호출한다 </summary>
        [Server]
        public bool TryRestoreHealth(float amount, ParticipantId sourceParticipant,
            out float appliedAmount)
        {
            appliedAmount = 0f;
            ResourceTransactionResult prepareResult = ServerPrepareRecovery(
                RecoveryResourceKind.Health, sourceParticipant, amount,
                out ResourceCommitPlan plan);
            if (prepareResult != ResourceTransactionResult.Success)
            {
                return false;
            }

            if (!ServerTryAcquireResourceTransaction(plan))
            {
                return false;
            }

            try
            {
                ServerApplyResourceTransaction(plan);
                ServerPublishResourceTransaction(plan);
                appliedAmount = plan.AppliedAmount;
                return true;
            }
            finally
            {
                ServerReleaseResourceTransaction();
            }
        }

        /// <summary> 현재 원본과 회복 정책으로 실제 적용량이 고정된 계획을 만든다 </summary>
        [Server]
        public ResourceTransactionResult ServerPrepareRecovery(
            RecoveryResourceKind resourceKind, ParticipantId sourceParticipant, float amount,
            out ResourceCommitPlan plan)
        {
            plan = default;
            if (_resourceTransactionLocked || float.IsNaN(amount) ||
                float.IsInfinity(amount) || (amount <= Constants.Combat.HEALTH_EPSILON))
            {
                return ResourceTransactionResult.InvalidRequest;
            }

            if (IsDead || IsSwallowed)
            {
                return ResourceTransactionResult.Dead;
            }

            ServerEnsureResourceState();
            if (_serverResourceRevision == uint.MaxValue)
            {
                return ResourceTransactionResult.RevisionExhausted;
            }

            float previousValue;
            float nextValue;
            switch (resourceKind)
            {
                case RecoveryResourceKind.Health:
                    if (float.IsNaN(_maxHealth) || float.IsInfinity(_maxHealth) ||
                        (_maxHealth <= Constants.Combat.HEALTH_EPSILON) ||
                        float.IsNaN(_serverHealth) || float.IsInfinity(_serverHealth))
                    {
                        return ResourceTransactionResult.InvalidState;
                    }

                    if (_serverHealth >= (_maxHealth - Constants.Combat.HEALTH_EPSILON))
                    {
                        return ResourceTransactionResult.ResourceFull;
                    }

                    float multiplier = BarrierStateLocator.Current.GetHealingMultiplier(
                        transform.position);
                    if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                    {
                        return ResourceTransactionResult.InvalidState;
                    }

                    float effectiveAmount = amount * multiplier;
                    if (float.IsNaN(effectiveAmount) || float.IsInfinity(effectiveAmount))
                    {
                        return ResourceTransactionResult.InvalidRequest;
                    }

                    if (effectiveAmount <= Constants.Combat.HEALTH_EPSILON)
                    {
                        return ResourceTransactionResult.RecoveryBlocked;
                    }

                    previousValue = _serverHealth;
                    nextValue     = Mathf.Min(_maxHealth, previousValue + effectiveAmount);
                    break;

                case RecoveryResourceKind.Spirit:
                    if ((!GameDB.IsInitialized) || (GameDB.Combat == null))
                    {
                        return ResourceTransactionResult.InvalidState;
                    }

                    float spiritMax = GameDB.Combat.SpiritMax;
                    if (float.IsNaN(spiritMax) || float.IsInfinity(spiritMax) ||
                        (spiritMax <= Constants.Combat.HEALTH_EPSILON) ||
                        float.IsNaN(_serverSpirit) || float.IsInfinity(_serverSpirit))
                    {
                        return ResourceTransactionResult.InvalidState;
                    }

                    if (_serverSpirit >= (spiritMax - Constants.Combat.HEALTH_EPSILON))
                    {
                        return ResourceTransactionResult.ResourceFull;
                    }

                    previousValue = _serverSpirit;
                    nextValue     = Mathf.Min(spiritMax, previousValue + amount);
                    break;

                default:
                    return ResourceTransactionResult.InvalidRequest;
            }

            plan = new ResourceCommitPlan(resourceKind, sourceParticipant,
                _serverResourceRevision, previousValue, nextValue);
            return plan.IsValid
                ? ResourceTransactionResult.Success
                : ResourceTransactionResult.InvalidState;
        }

        /// <summary> 계획이 여전히 유효하면 통지가 끝날 때까지 자원을 잠근다 </summary>
        [Server]
        public bool ServerTryAcquireResourceTransaction(in ResourceCommitPlan plan)
        {
            if (!ServerCanApplyResourceTransaction(plan))
            {
                return false;
            }

            _resourceTransactionLocked = true;
            return true;
        }

        /// <summary> 잠근 계획을 서버 원본에만 적용한다 </summary>
        [Server]
        public void ServerApplyResourceTransaction(in ResourceCommitPlan plan)
        {
            if ((!_resourceTransactionLocked) || (!plan.IsValid) || IsDead || IsSwallowed ||
                (_serverResourceRevision != plan.ExpectedRevision) ||
                (!ServerCanApplyLockedResourceTransaction(plan)))
            {
                Debug.LogError("[PlayerStatus] 유효하지 않은 자원 거래 계획을 적용하려 했다", this);
                return;
            }

            if (plan.ResourceKind == RecoveryResourceKind.Health)
            {
                _serverHealth = plan.NextValue;
            }
            else
            {
                _serverSpirit = plan.NextValue;
            }

            _serverResourceRevision = plan.ExpectedRevision + 1u;
        }

        /// <summary> 결과 캐시와 모든 원본이 확정된 뒤 자원 표시를 공개한다 </summary>
        [Server]
        public void ServerPublishResourceTransaction(in ResourceCommitPlan plan)
        {
            if ((!_resourceTransactionLocked) || (!plan.IsValid) ||
                (_serverResourceRevision != (plan.ExpectedRevision + 1u)) ||
                (!ServerMatchesAppliedResourceTransaction(plan)))
            {
                Debug.LogError("[PlayerStatus] 확정되지 않은 자원 거래 계획을 공개하려 했다", this);
                return;
            }

            _resourceRevision = _serverResourceRevision;
            if (plan.ResourceKind == RecoveryResourceKind.Health)
            {
                _health = _serverHealth;
                ServerRecordHealing(plan.SourceParticipant, plan.AppliedAmount);
                ServerRecordHealthState();
            }
            else if (plan.ResourceKind == RecoveryResourceKind.Spirit)
            {
                _spirit = _serverSpirit;
            }
        }

        [Server]
        public void ServerReleaseResourceTransaction()
        {
            _resourceTransactionLocked = false;
        }

        /// <summary> 스태미나가 충분할 때만 원자적으로 차감한다. 서버에서만 호출한다 </summary>
        [Server]
        public bool TrySpendStamina(float amount)
        {
            if (IsDead || IsSwallowed || (amount < 0f))
            {
                return false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
            // 차감하지 않고 통과시킨다. 막대가 줄었다 차는 것이 보이면 무한인지 아닌지 헷갈린다
            if (_devInfiniteResources)
            {
                RestoreStaminaToMax();
                return true;
            }
#endif

            if (_stamina - (_hitAttack != null ? _hitAttack.ReservedSkillStamina : 0f) < amount)
            {
                return false;
            }

            _stamina                    -= amount;
            _lastStaminaSpendServerTime  = NetworkTime.time;
            return true;
        }

        /// <summary> 패링 성공 보상처럼 스태미나를 즉시 최대치로 채운다. 서버에서만 호출한다 </summary>
        [Server]
        public void RestoreStaminaToMax()
        {
            if (!GameDB.IsInitialized || IsDead || IsSwallowed)
            {
                return;
            }

            _stamina = GameDB.Combat.StaminaMax;
        }

        [Server]
        public void ServerRestoreStamina(float amount)
        {
            if (GameDB.IsInitialized && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount > 0f)
            {
                _stamina = Mathf.Min(GameDB.Combat.StaminaMax, _stamina + amount);
            }
        }

        /// <summary> 신력이 충분할 때만 원자적으로 차감한다. 자연 회복 시간에는 영향을 주지 않는다 </summary>
        [Server]
        public bool TrySpendSpirit(float amount)
        {
            ServerEnsureResourceState();
            if (_resourceTransactionLocked || IsDead || IsSwallowed || (amount < 0f))
            {
                return false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
            if (_devInfiniteResources)
            {
                DevFillSpirit();
                return true;
            }
#endif

            if (_serverSpirit - (_hitAttack != null ? _hitAttack.ReservedSkillSpirit : 0f) < amount)
            {
                return false;
            }

            if (amount <= 0f)
            {
                return true;
            }

            _serverSpirit -= amount;
            ServerAdvanceResourceRevision();
            _spirit = _serverSpirit;
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Dev Cheat
        ****************************************/

        /// <summary> 개발용 무한 자원이 켜져 있는지. 패널이 상태를 그리려고 읽는다 </summary>
        public bool DevInfiniteResources => _devInfiniteResources;

        /// <summary>
        /// 개발용 무한 자원을 켜고 끈다. 자기 캐릭터에만 걸리고 남의 판정은 건드리지 않는다
        /// 판정은 전부 서버가 하므로 클라이언트가 값을 직접 바꾸지 않고 서버에 부탁한다
        /// </summary>
        [Command]
        public void CmdDevSetInfiniteResources(bool isOn)
        {
            // 보낸 쪽의 잠금은 조작된 클라이언트가 건너뛸 수 있다. 호스트가 치트 모드를 켠 방에서만 받는다
            if (isOn && !DevAccess.IsUnlocked)
            {
                Debug.LogWarning($"[PlayerStatus] 호스트의 치트 모드가 꺼져 있어 무한 자원 요청을 거절했다: {NetworkPeerLabel.Describe(connectionToClient)}", this);
                return;
            }

            _devInfiniteResources = isOn;

            if (!isOn)
            {
                return;
            }

            // 켜는 순간 바닥나 있던 것도 채워 준다. 다음 공격을 기다릴 이유가 없다
            DevFillAll();
        }

        /// <summary> 켜져 있는 동안 세 자원을 최대치로 되돌린다 </summary>
        [Server]
        private void DevFillAll()
        {
            RestoreStaminaToMax();
            DevFillSpirit();
            DevFillHealth();
        }

        [Server]
        private void DevFillSpirit()
        {
            if (!GameDB.IsInitialized || IsDead || IsSwallowed || _resourceTransactionLocked)
            {
                return;
            }

            ServerEnsureResourceState();

            float max = GameDB.Combat.SpiritMax;
            if (_serverSpirit >= max)
            {
                return;
            }

            _serverSpirit = max;
            ServerAdvanceResourceRevision();
            _spirit = _serverSpirit;
        }

        [Server]
        private void DevFillHealth()
        {
            if (IsDead || IsSwallowed || (_maxHealth <= 0f))
            {
                return;
            }

            float missing = _maxHealth - Health;
            if (missing <= Constants.Combat.HEALTH_EPSILON)
            {
                return;
            }

            TryRestoreHealth(missing);
        }

#endif

        /// <summary> 신력을 최대치까지만 채운다. 넘치는 분은 버린다 </summary>
        [Server]
        public void ServerAddSpirit(float amount)
        {
            ResourceTransactionResult prepareResult = ServerPrepareRecovery(
                RecoveryResourceKind.Spirit, _participant, amount,
                out ResourceCommitPlan plan);
            if (prepareResult != ResourceTransactionResult.Success)
            {
                return;
            }

            if (!ServerTryAcquireResourceTransaction(plan))
            {
                return;
            }

            try
            {
                ServerApplyResourceTransaction(plan);
                ServerPublishResourceTransaction(plan);
            }
            finally
            {
                ServerReleaseResourceTransaction();
            }
        }

        private bool ServerCanApplyResourceTransaction(in ResourceCommitPlan plan)
        {
            ServerEnsureResourceState();
            return ((!_resourceTransactionLocked) && (!IsDead) && (!IsSwallowed) && plan.IsValid &&
                (_serverResourceRevision == plan.ExpectedRevision) &&
                (plan.ExpectedRevision != uint.MaxValue) &&
                ServerCanApplyLockedResourceTransaction(plan));
        }

        private bool ServerCanApplyLockedResourceTransaction(in ResourceCommitPlan plan)
        {
            switch (plan.ResourceKind)
            {
                case RecoveryResourceKind.Health:
                    return (Mathf.Approximately(_serverHealth, plan.PreviousValue) &&
                        (plan.NextValue <= _maxHealth));
                case RecoveryResourceKind.Spirit:
                    return (GameDB.IsInitialized && (GameDB.Combat != null) &&
                        Mathf.Approximately(_serverSpirit, plan.PreviousValue) &&
                        (plan.NextValue <= GameDB.Combat.SpiritMax));
                default:
                    return false;
            }
        }

        private bool ServerMatchesAppliedResourceTransaction(in ResourceCommitPlan plan)
        {
            switch (plan.ResourceKind)
            {
                case RecoveryResourceKind.Health:
                    return Mathf.Approximately(_serverHealth, plan.NextValue);
                case RecoveryResourceKind.Spirit:
                    return Mathf.Approximately(_serverSpirit, plan.NextValue);
                default:
                    return false;
            }
        }

        private void ServerEnsureResourceState()
        {
            if (_resourceStateInitialized)
            {
                return;
            }

            _serverHealth            = _health;
            _serverSpirit            = _spirit;
            _resourceStateInitialized = true;
        }

        private void ServerAdvanceResourceRevision()
        {
            if (_serverResourceRevision != uint.MaxValue)
            {
                ++_serverResourceRevision;
                _resourceRevision = _serverResourceRevision;
            }
        }

        private void ServerRecordHealing(ParticipantId sourceParticipant, float appliedAmount)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!sourceParticipant.IsValid) ||
                (!_participant.IsValid) || (appliedAmount <= 0f) || float.IsNaN(appliedAmount) ||
                float.IsInfinity(appliedAmount))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordHealing(new RunAppliedHealingFact(
                    run.RunId, sourceParticipant, _participant, appliedAmount));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerStatus] 회복 통계 기록 실패: {e}", this);
            }
        }

        private void RecordSwallowedRescueFact(ParticipantId rescuer, uint generation)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!rescuer.IsValid) ||
                (!_participant.IsValid) || (rescuer == _participant) || (generation == 0u))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordLife(
                    RunPlayerLifeFact.SwallowedRescue(run.RunId, rescuer,
                        _participant, generation));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerStatus] 삼켜짐 구조 통계 기록 실패: {e}", this);
            }
        }

        private void ServerRecordHealthState()
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!_participant.IsValid))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordParticipantState(
                    new RunParticipantStateFact(run.RunId, _participant, Health));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerStatus] 참가자 상태 기록 실패: {e}", this);
            }
        }

        /// <summary> 일시 무적을 만료 시각까지 연장한다 </summary>
        [Server]
        public void GrantTimedInvincibility(double untilServerTime)
        {
            _invincibleUntilServerTime = System.Math.Max(_invincibleUntilServerTime, untilServerTime);
            _isInvincible               = true;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
