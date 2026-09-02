// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 로비와 런 관리자가 플레이어에게 말을 거는 단일 창구.
    /// 무기, 외형, 상태, 이동 컴포넌트를 감춰 Network 파트가 Combat 타입을 직접 참조하지 않게 한다
    /// </summary>
    [RequireComponent(typeof(PlayerWeapon))]
    [RequireComponent(typeof(PlayerAppearance))]
    public class PlayerRunAgent : NetworkBehaviour, IPlayerRoleAssignment, IPlayerRunLifecycle
    {
        /****************************************
        *                Fields
        ****************************************/

        private PlayerWeapon     _weapon     = null;
        private PlayerAppearance _appearance = null;
        private PlayerStatus     _status     = null;
        private PlayerController _controller = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasRole => ((_weapon != null) && (_appearance != null) && (_weapon.HasWeapon) && (_appearance.HasAppearance));

        public Vector3 Position => transform.position;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weapon     = GetComponent<PlayerWeapon>();
            _appearance = GetComponent<PlayerAppearance>();
            _status     = GetComponent<PlayerStatus>();
            _controller = GetComponent<PlayerController>();
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 무기와 외형을 한 번에 확정한다. 하나만 적용된 채로 남으면 런이 열리지 않으므로
        /// 둘 다 확정 가능한지 먼저 확인하고, 그래도 실패하면 앞서 적용한 것을 되돌린다
        /// </summary>
        [Server]
        public bool TryCommitRole(WeaponKind weapon, AppearanceKind appearance)
        {
            if ((_weapon == null) || (_appearance == null))
            {
                Debug.LogError("[PlayerRunAgent] 무기나 외형 컴포넌트가 없어 역할을 확정할 수 없다", this);
                return false;
            }

            if ((_weapon.HasWeapon) || (_appearance.HasAppearance))
            {
                Debug.LogError("[PlayerRunAgent] 이미 역할이 확정되어 있다", this);
                return false;
            }

            if (!_weapon.ServerSetInitialWeapon(weapon))
            {
                return false;
            }

            if (!_appearance.ServerSetAppearance(appearance))
            {
                _weapon.ServerClearForNewRun();
                Debug.LogError("[PlayerRunAgent] 외형 적용에 실패해 무기 확정을 되돌렸다", this);
                return false;
            }

            return true;
        }

        [Server]
        public void ClearRole()
        {
            _weapon?.ServerClearForNewRun();
            _appearance?.ServerClearForNewRun();
        }

        [Server]
        public void ResetForNewRun()
        {
            if (_status == null)
            {
                return;
            }

            _status.ServerResetForNewRun();
        }

        [Server]
        public void Teleport(Vector3 position)
        {
            if (_controller == null)
            {
                transform.position = position;
                return;
            }

            _controller.ServerTeleport(position);
        }
    }
}
