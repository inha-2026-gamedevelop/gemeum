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

        private PlayerAppearance _appearance;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 무기가 없다, 배분 전 값을 직업으로 읽지 않도록 이 값을 먼저 본다 </summary>
        public bool HasWeapon => _hasWeapon;

        public WeaponKind Weapon => _weapon;

        /****************************************
        *              Unity Callbacks
        ****************************************/

        private void Awake()
        {
            _appearance = GetComponent<PlayerAppearance>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        /// <summary> 서버·클라이언트 모두에서 값이 바뀔 때 호출된다, 판정에는 관여하지 않는다 </summary>
        private void OnWeaponChanged(WeaponKind oldWeapon, WeaponKind newWeapon)
        {
            RefreshAppearance();
        }

        // Sword가 enum 기본값이라 검을 배분받으면 _weapon 값이 바뀌지 않아 위 훅이 호출되지 않는다.
        // 배분 여부는 반드시 false에서 true로 바뀌므로 외형 적용을 여기에도 건다.
        // _weapon이 먼저 선언되어 있어 스폰 payload를 읽을 때 이 시점의 _weapon은 이미 확정된 값이다
        private void OnHasWeaponChanged(bool oldHasWeapon, bool newHasWeapon)
        {
            RefreshAppearance();
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

        private void RefreshAppearance()
        {
            if ((!_hasWeapon) || (_appearance == null))
            {
                return;
            }

            _appearance.ApplyAppearance(_weapon);
        }
    }
}
