// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 이 플레이어가 쥔 무기 = 직업. 서버가 확정하고 클라이언트는 읽기만 한다 </summary>
    public class PlayerWeapon : NetworkBehaviour, IRunStateCarrier
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnWeaponChanged))]    private WeaponKind _weapon    = default;
        [SyncVar(hook = nameof(OnHasWeaponChanged))] private bool       _hasWeapon = false;

        private PlayerWeaponVisual        _weaponVisual;
        private PlayerAnimationController _animation;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 장착 무기가 바뀌었다 </summary>
        public event System.Action<WeaponKind> WeaponChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 무기가 없다 </summary>
        public bool HasWeapon => _hasWeapon;

        public WeaponKind Weapon => _weapon;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weaponVisual = GetComponent<PlayerWeaponVisual>();
            _animation    = GetComponent<PlayerAnimationController>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnWeaponChanged(WeaponKind oldWeapon, WeaponKind newWeapon)
        {
            RefreshWeaponVisual();

            if (isLocalPlayer)
            {
                GDebug.Log($"무기 {oldWeapon} -> {newWeapon}");
            }

            WeaponChanged?.Invoke(newWeapon);
        }

        private void OnHasWeaponChanged(bool oldHasWeapon, bool newHasWeapon)
        {
            RefreshWeaponVisual();
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 11;

        /// <summary> 확정된 무기를 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (!_hasWeapon)
            {
                return;
            }

            snapshot.Weapon = _weapon;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if (!System.Enum.IsDefined(typeof(WeaponKind), snapshot.Weapon))
            {
                failure = $"알 수 없는 무기다: {snapshot.Weapon}";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary> 담아 둔 무기를 되붓는다 </summary>
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            _weapon    = snapshot.Weapon;
            _hasWeapon = true;

            RefreshWeaponVisual();
            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런 시작 시 배분된 무기를 확정한다 </summary>
        [Server]
        public bool ServerSetInitialWeapon(WeaponKind weapon)
        {
            if (_hasWeapon)
            {
                Debug.LogWarning($"[PlayerWeapon] 이미 무기가 확정된 플레이어에 다시 배분하려 했다: {_weapon}", this);
                return false;
            }

            _weapon    = weapon;
            _hasWeapon = true;
            return true;
        }

        /// <summary> 런이 끝나 로비로 돌아갈 때 배분을 되돌린다 </summary>
        [Server]
        public void ServerClearForNewRun()
        {
            _hasWeapon = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshWeaponVisual()
        {
            if (_weaponVisual != null)
            {
                _weaponVisual.Refresh();
            }

            if (_animation != null)
            {
                _animation.RefreshWeaponAnimation();
            }
        }
    }
}
