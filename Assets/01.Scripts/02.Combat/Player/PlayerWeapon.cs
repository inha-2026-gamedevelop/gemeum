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

        /// <summary> 장착 무기가 바뀌었다. 배분 전에는 기본값이 온다 </summary>
        public event System.Action<WeaponKind> WeaponChanged;

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
            _animation    = GetComponent<PlayerAnimationController>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        // 캐릭터(PlayerCharacter)가 오른손 뼈를 켜는 시점과 무기 SyncVar 도착 순서가 보장되지 않아
        // 두 훅 모두 무기 시각화를 다시 시도한다. PlayerCharacter의 이중 훅과 같은 이유다
        private void OnWeaponChanged(WeaponKind oldWeapon, WeaponKind newWeapon)
        {
            RefreshWeaponVisual();

            WeaponChanged?.Invoke(newWeapon);
        }

        private void OnHasWeaponChanged(bool oldHasWeapon, bool newHasWeapon)
        {
            RefreshWeaponVisual();
        }

        /****************************************
        *           Interface Members
        ****************************************/

        // 캐릭터(10) 바로 뒤다. 무기 모델은 캐릭터 모델의 오른손 뼈에 붙으므로
        // 캐릭터가 먼저 켜져 있어야 붙을 자리가 있다
        public int CarrierOrder => 11;

        /// <summary> 확정된 무기를 담는다. 배분 전이면 담지 않는다 - 기본값이 직업으로 읽히면 안 된다 </summary>
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

        /// <summary>
        /// 담아 둔 무기를 되붓는다.
        ///
        /// <see cref="ServerSetInitialWeapon"/>을 쓰지 않는 이유는 그쪽이 "런 시작 배분"의 창구라
        /// 이미 확정된 대상을 거부하기 때문이다. 되붓기는 배분이 아니라 같은 값의 복원이다.
        /// 무기가 곧 직업이므로 이 값이 빠지면 도착한 사람은 직업 없는 몸이 된다
        /// </summary>
        public bool Restore(RunParticipantSnapshot snapshot)
        {
            _weapon    = snapshot.Weapon;
            _hasWeapon = true;

            // 호스트에서는 SyncVar 훅이 울지 않는다. 같은 값으로 되부으면 클라이언트에서도 안 운다
            RefreshWeaponVisual();
            return true;
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

        /// <summary>
        /// 런이 끝나 로비로 돌아갈 때 배분을 되돌린다. LobbyController만 호출한다.
        /// 확정 여부만 내리고 무기 값은 남긴다, 다음 배분이 값을 덮어쓴다
        /// </summary>
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

            // 손에 든 모델뿐 아니라 동작도 무기가 정한다
            if (_animation != null)
            {
                _animation.RefreshWeaponAnimation();
            }
        }
    }
}
