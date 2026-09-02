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
    /// <summary>
    /// 플레이어 HP, 스태미나, 신력, 생명 상태의 원본. IDamageable 구현.
    /// 다른 컴포넌트는 이 값을 직접 대입하지 않고 여기의 서버 API를 호출한다
    /// </summary>
    public class PlayerStatus : NetworkBehaviour, IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthChanged))]    private float           _health    = 0f;
        [SyncVar(hook = nameof(OnMaxHealthChanged))] private float           _maxHealth = 0f;
        [SyncVar(hook = nameof(OnStaminaChanged))]   private float           _stamina   = 0f;
        [SyncVar(hook = nameof(OnSpiritChanged))]    private float           _spirit    = 0f;
        [SyncVar(hook = nameof(OnLifeStateChanged))] private PlayerLifeState _lifeState = PlayerLifeState.Alive;
        [SyncVar] private bool            _isInvincible = false; // 구르기, 패링 성공처럼 생명 상태는 그대로인 일시 무적

        private double _lastStaminaSpendServerTime = -1.0; // 서버 전용, 회복 시작을 늦추는 기준 시각

        // 구르기, 패링 등 여러 출처가 각자 bool을 껐다 켜면 먼저 끝난 출처가 더 긴 다른 출처의
        // 무적까지 해제한다. 만료 시각 하나만 소유하고 각 출처는 max로만 연장한다
        private double _invincibleUntilServerTime = 0.0; // 서버 전용

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

        // UI가 매 프레임 값을 훑지 않게 변화 시점에만 알린다. 클라이언트에서만 발생한다
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

            // 구독 시점이 값 도착보다 늦을 수 있다. UI가 처음부터 올바른 값을 그리도록 한 번 흘려 준다
            RaiseAll();
        }

        public override void OnStopLocalPlayer()
        {
            if (Local == this)
            {
                Local = null;
            }
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnHealthChanged(float oldValue, float newValue)
        {
            HealthChanged?.Invoke(newValue, _maxHealth);
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

        // 최대치는 동기화 대상이 아니라 데이터다. 세 클라이언트가 같은 GameDB를 읽는다
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
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerRegenTick);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 런이 끝나 로비로 돌아갈 때 HP, 스태미나, 신력, 생명 상태를 시작값으로 되돌린다.
        /// LobbyController만 호출한다. 이걸 안 하면 직전 런에서 쓰러진 사람이 다음 런에서도
        /// Downed로 남아 PlayerActionGate가 모든 행동을 막는다
        /// </summary>
        [Server]
        public void ServerResetForNewRun()
        {
            _isInvincible               = false;
            _invincibleUntilServerTime  = 0.0;
            _lastStaminaSpendServerTime = -1.0;

            TryApplyStartingState();
        }

        // 스폰 직후와 런 재시작이 같은 시작값을 쓰도록 한곳에 모은다
        private bool TryApplyStartingState()
        {
            // Awake가 아니라 여기서 읽어야 GameDBBootstrap 이후가 보장된다
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
            return true;
        }

        /****************************************
        *              Server Tick
        ****************************************/

        // 신력은 자연 회복하지 않는다, 물약 전용 희소 자원이라 여기서 건드리지 않는다
        private void ServerRegenTick(float fixedDeltaTime)
        {
            if (IsDead || !GameDB.IsInitialized)
            {
                return;
            }

            if ((_isInvincible) && (NetworkTime.time >= _invincibleUntilServerTime))
            {
                _isInvincible = false;
            }

            CombatConfigSO combat = GameDB.Combat;
            if (_stamina >= combat.StaminaMax)
            {
                return;
            }

            // 행동 직후 곧바로 차오르면 연속 구르기를 막는 자원 설계가 무의미해진다
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

            // 부활 직후 무적은 남은 잡귀에게 즉사하는 것을 막기 위한 것이다
            if (_lifeState == PlayerLifeState.ReviveInvulnerable)
            {
                return;
            }

            // 구르기, 패링 성공 무적은 생명 상태를 바꾸지 않는 일시 상태라 별도로 확인한다
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
                // TODO(민성): ReviveController가 붙으면 Spectating 전이와 관전 타이머를 잇는다
                _lifeState = PlayerLifeState.Downed;
            }
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

        /// <summary>
        /// 구르기, 패링 성공처럼 생명 상태와 무관한 일시 무적을 만료 시각까지 연장한다.
        /// 여러 출처가 동시에 걸려 있어도 가장 늦게 끝나는 시각만 남는다. 서버에서만 호출한다
        /// </summary>
        [Server]
        public void GrantTimedInvincibility(double untilServerTime)
        {
            _invincibleUntilServerTime = System.Math.Max(_invincibleUntilServerTime, untilServerTime);
            _isInvincible               = true;
        }
    }
}
