// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 이 플레이어가 쥔 무기 = 직업. 서버가 확정하고 클라이언트는 읽기만 한다.
    /// 클라이언트가 보낸 무기 ID는 신뢰하지 않으며 판정은 항상 이 서버 상태를 본다
    /// </summary>
    public class PlayerWeapon : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnWeaponChanged))]    private WeaponKind _weapon    = default;
        [SyncVar(hook = nameof(OnHasWeaponChanged))] private bool       _hasWeapon = false;

        private PlayerWeaponVisual _weaponVisual;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 무기가 없다, 배분 전 값을 직업으로 읽지 않도록 이 값을 먼저 본다 </summary>
        public bool HasWeapon => _hasWeapon;

        public WeaponKind Weapon => _weapon;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weaponVisual = GetComponent<PlayerWeaponVisual>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        // 외형(PlayerAppearance)이 오른손 뼈를 켜는 시점과 무기 SyncVar 도착 순서가 보장되지 않아
        // 두 훅 모두 무기 시각화를 다시 시도한다. PlayerAppearance의 이중 훅과 같은 이유다
        private void OnWeaponChanged(WeaponKind oldWeapon, WeaponKind newWeapon)
        {
            RefreshWeaponVisual();
        }

        private void OnHasWeaponChanged(bool oldHasWeapon, bool newHasWeapon)
        {
            RefreshWeaponVisual();
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 런 시작 시 배분된 무기를 확정한다. LobbyController만 호출한다.
        /// 이미 확정된 뒤의 재호출은 거부한다, 런 도중 직업이 바뀌면 판정 결과가 달라진다
        /// </summary>
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

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshWeaponVisual()
        {
            if (_weaponVisual != null)
            {
                _weaponVisual.Refresh();
            }
        }
    }
}
