// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary>
    /// 플레이어 HP · 스태미나 · 신력 · 생명 상태의 원본. IDamageable 구현.
    /// 다른 컴포넌트는 이 값을 직접 대입하지 않고 여기의 서버 API를 호출한다
    /// </summary>
    public class PlayerStatus : NetworkBehaviour, IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private float           _health    = 0f;
        [SyncVar] private float           _maxHealth = 0f;
        [SyncVar] private float           _stamina   = 0f;
        [SyncVar] private float           _spirit    = 0f;
        [SyncVar] private PlayerLifeState _lifeState = PlayerLifeState.Alive;

        /****************************************
        *              Properties
        ****************************************/

        public float           Health    => _health;
        public float           MaxHealth => _maxHealth;
        public float           Stamina   => _stamina;
        public float           Spirit    => _spirit;
        public PlayerLifeState LifeState => _lifeState;

        /// <summary> 관전 중이거나 쓰러진 상태. 부활 무적은 살아 있는 것으로 센다 </summary>
        public bool IsDead => ((_lifeState == PlayerLifeState.Downed) || (_lifeState == PlayerLifeState.Spectating));

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            // Awake가 아니라 여기서 읽어야 GameDBBootstrap 이후가 보장된다
            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[PlayerStatus] GameDB가 초기화되지 않아 초기 상태를 정할 수 없다", this);
                return;
            }

            if (!GameDB.LevelCurve.TryGetLevel(Constants.Combat.START_LEVEL, out LevelCurveSO.LevelEntry entry))
            {
                Debug.LogError($"[PlayerStatus] 레벨 {Constants.Combat.START_LEVEL}의 곡선 항목이 없다", this);
                return;
            }

            CombatConfigSO combat = GameDB.Combat;

            _maxHealth = entry.MaxHealth;
            _health    = _maxHealth;
            _stamina   = combat.StaminaMax;
            _spirit    = combat.SpiritMax;
            _lifeState = PlayerLifeState.Alive;
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
    }
}
