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

namespace Geumeum.Combat
{
    /// <summary> 플레이어 HP, 스태미나, 신력, 생명 상태의 원본. IDamageable 구현 </summary>
    public class PlayerStatus : NetworkBehaviour, IDamageable, IRecoveryTarget, IRunStateCarrier
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

        // 서버가 스폰한 플레이어 전부
        private static readonly List<PlayerStatus> G_ServerPlayers = new List<PlayerStatus>();

        private double _lastStaminaSpendServerTime = -1.0; // 서버 전용

        private double _invincibleUntilServerTime = 0.0; // 서버 전용

        private double _reviveInvulnerableUntilServerTime = 0.0; // 서버 전용

        private bool _isRunStateReady = false; // 서버 전용

        /****************************************
        *              Properties
        ****************************************/

        public float           Health       => _health;
        public float           MaxHealth    => _maxHealth;
        public float           Stamina      => _stamina;
        public float           Spirit       => _spirit;
        public PlayerLifeState LifeState    => _lifeState;
        public bool            IsInvincible => _isInvincible;

        /// <summary> 관전 중이거나 쓰러진 상태. 부활 무적은 살아 있는 것으로 센다 </summary>
        public bool IsDead => ((_lifeState == PlayerLifeState.Downed) || (_lifeState == PlayerLifeState.Spectating));

        /// <summary>
        /// 이 화면의 주인. UI가 체력 바를 누구 것으로 그릴지 여기서 정한다.
        /// 관전 중에도 자기 인스턴스는 그대로 남으므로 바뀌지 않는다
        /// </summary>
        public static PlayerStatus Local { get; private set; }

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 체력이 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> HealthChanged;

        /// <summary> 스태미나가 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> StaminaChanged;

        /// <summary> 정신력이 바뀌었다. (현재, 최대) </summary>
        public event System.Action<float, float> SpiritChanged;

        /// <summary> 생명 상태가 바뀌었다 </summary>
        public event System.Action<PlayerLifeState> LifeStateChanged;

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
            HealthChanged?.Invoke(newValue, _maxHealth);

            if (newValue < oldValue)
            {
                SfxPlayer.Play(SoundKey.PlayerHurt, transform.position, isLocalPlayer, netId);
            }
        }

        private void OnMaxHealthChanged(float oldValue, float newValue)
        {
            HealthChanged?.Invoke(_health, newValue);
        }

        private void OnStaminaChanged(float oldValue, float newValue)
        {
            StaminaChanged?.Invoke(newValue, GetStaminaMax());
        }

        private void OnSpiritChanged(float oldValue, float newValue)
        {
            SpiritChanged?.Invoke(newValue, GetSpiritMax());
        }

        private void OnLifeStateChanged(PlayerLifeState oldValue, PlayerLifeState newValue)
        {
            LifeStateChanged?.Invoke(newValue);
        }

        /// <summary> 지금 값을 그대로 한 번 더 알린다. UI가 뒤늦게 구독했을 때 쓴다 </summary>
        public void RaiseAll()
        {
            HealthChanged?.Invoke(_health, _maxHealth);
            StaminaChanged?.Invoke(_stamina, GetStaminaMax());
            SpiritChanged?.Invoke(_spirit, GetSpiritMax());
            LifeStateChanged?.Invoke(_lifeState);
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

            if (!TryApplyStartingState())
            {
                return;
            }

            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[PlayerStatus] AuthorityTickScheduler가 없어 스태미나 회복을 돌릴 수 없다", this);
                return;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_STATUS, ServerRegenTick);
        }

        public override void OnStopServer()
        {
            G_ServerPlayers.Remove(this);

            // TODO(민성): 보스맵 씬 전환을 붙이기 전에 이 호출을 막을 조건을 먼저 만든다
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

        /// <summary> 지금 자원과 생명 상태를 담는다. 최대 HP는 담지 않는다 - 레벨에서 다시 계산한다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            snapshot.Health    = _health;
            snapshot.Stamina   = _stamina;
            snapshot.Spirit    = _spirit;
            snapshot.LifeState = _lifeState.ToSnapshotState();
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot.Health < 0f) || (snapshot.Stamina < 0f) || (snapshot.Spirit < 0f))
            {
                failure = "자원 값이 음수다";
                return false;
            }

            if (!snapshot.LifeState.TryToLifeState(out PlayerLifeState _))
            {
                failure = $"되돌릴 수 없는 생명 상태다: {snapshot.LifeState}";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary> 담아 둔 자원과 생명 상태를 되붓는다 </summary>
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (!snapshot.LifeState.TryToLifeState(out PlayerLifeState lifeState))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.IsInitialized ? GameDB.Combat : null;

            _health    = Mathf.Clamp(snapshot.Health, 0f, _maxHealth);
            _stamina   = (combat == null) ? snapshot.Stamina : Mathf.Clamp(snapshot.Stamina, 0f, combat.StaminaMax);
            _spirit    = (combat == null) ? snapshot.Spirit  : Mathf.Clamp(snapshot.Spirit, 0f, combat.SpiritMax);
            _lifeState = lifeState;

            _isRunStateReady = true;

            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런이 끝나 로비로 돌아갈 때 HP, 스태미나, 신력, 생명 상태를 시작값으로 되돌린다 </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            _isInvincible                      = false;
            _invincibleUntilServerTime         = 0.0;
            _reviveInvulnerableUntilServerTime = 0.0;
            _lastStaminaSpendServerTime        = -1.0;

            TryApplyStartingState();
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

            _maxHealth = entry.MaxHealth;
            _health    = _maxHealth;
            _stamina   = combat.StaminaMax;
            _spirit    = combat.SpiritMax;
            _lifeState = PlayerLifeState.Alive;

            _isRunStateReady = (!RunSceneDirector.IsServerTransitioning);
            return true;
        }

        /****************************************
        *              Server Tick
        ****************************************/

        private void ServerRegenTick(float fixedDeltaTime)
        {
            if ((IsDead) || (!GameDB.IsInitialized))
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
            if (IsDead)
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

            if (damage.Amount <= Constants.Combat.DAMAGE_EPSILON)
            {
                return;
            }

            _health = Mathf.Max(0f, _health - damage.Amount);

            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                _lifeState = PlayerLifeState.Downed;
            }
        }

        /// <summary> 사망 직후 전이 상태(Downed)를 관전 대기(Spectating)로 넘긴다 </summary>
        [Server]
        public bool ServerBeginSpectate()
        {
            if (_lifeState != PlayerLifeState.Downed)
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
            if (!IsDead)
            {
                return;
            }

            _health  = Mathf.Clamp(_maxHealth * Mathf.Clamp01(healthRatio), 0f, _maxHealth);
            _stamina = GameDB.IsInitialized ? GameDB.Combat.StaminaMax : _stamina;

            _isInvincible              = false;
            _invincibleUntilServerTime = 0.0;

            _lifeState                         = PlayerLifeState.ReviveInvulnerable;
            _reviveInvulnerableUntilServerTime = NetworkTime.time + invulnerableDuration;
        }

        /// <summary> 회복을 적용한다, 서버에서만 호출한다 </summary>
        [Server]
        public bool TryRestoreHealth(float amount)
        {
            if (IsDead)
            {
                return false;
            }

            if (amount <= Constants.Combat.HEALTH_EPSILON)
            {
                return false;
            }

            if (_health >= _maxHealth)
            {
                return false;
            }

            float applied = amount * BarrierStateLocator.Current.GetHealingMultiplier(transform.position);

            if (applied <= Constants.Combat.HEALTH_EPSILON)
            {
                return false;
            }

            _health = Mathf.Min(_maxHealth, _health + applied);
            return true;
        }

        /// <summary> 스태미나가 충분할 때만 원자적으로 차감한다. 서버에서만 호출한다 </summary>
        [Server]
        public bool TrySpendStamina(float amount)
        {
            if ((amount < 0f) || (_stamina < amount))
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
            if (!GameDB.IsInitialized)
            {
                return;
            }

            _stamina = GameDB.Combat.StaminaMax;
        }

        /// <summary> 신력이 충분할 때만 원자적으로 차감한다. 자연 회복 시간에는 영향을 주지 않는다 </summary>
        [Server]
        public bool TrySpendSpirit(float amount)
        {
            if ((amount < 0f) || (_spirit < amount))
            {
                return false;
            }

            _spirit -= amount;
            return true;
        }

        /// <summary> 신력을 최대치까지만 채운다. 넘치는 분은 버린다 </summary>
        [Server]
        public void ServerAddSpirit(float amount)
        {
            if ((amount <= 0f) || (IsDead))
            {
                return;
            }

            _spirit = Mathf.Min(_spirit + amount, GetSpiritMax());
        }

        /// <summary> 일시 무적을 만료 시각까지 연장한다 </summary>
        [Server]
        public void GrantTimedInvincibility(double untilServerTime)
        {
            _invincibleUntilServerTime = System.Math.Max(_invincibleUntilServerTime, untilServerTime);
            _isInvincible               = true;
        }
    }
}
