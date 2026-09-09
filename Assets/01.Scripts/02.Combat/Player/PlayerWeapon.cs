// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 이 플레이어가 쥔 무기 = 직업. 서버가 확정하고 클라이언트는 읽기만 한다 </summary>
    public class PlayerWeapon : NetworkBehaviour, IRunStateCarrier, IEquipmentWeaponTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnWeaponChanged))]    private WeaponKind _weapon    = default;
        [SyncVar(hook = nameof(OnHasWeaponChanged))] private bool       _hasWeapon = false;

        // 서버 판정 원본. SyncVar는 모든 관련 원본이 확정된 뒤 공개한다.
        private WeaponKind _serverWeapon;
        private bool       _serverHasWeapon;
        private bool       _serverStateInitialized;
        private bool       _equipmentWriteLocked;

        private PlayerWeaponVisual        _weaponVisual;
        private PlayerAnimationController _animation;
        private ParryGauge                _parryGauge;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 장착 무기가 바뀌었다 </summary>
        public event System.Action<WeaponKind> WeaponChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 무기가 없다 </summary>
        public bool HasWeapon => (isServer && _serverStateInitialized)
            ? _serverHasWeapon : _hasWeapon;

        public WeaponKind Weapon => (isServer && _serverStateInitialized)
            ? _serverWeapon : _weapon;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weaponVisual = GetComponent<PlayerWeaponVisual>();
            _animation    = GetComponent<PlayerAnimationController>();
            _parryGauge   = GetComponent<ParryGauge>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnWeaponChanged(WeaponKind oldWeapon, WeaponKind newWeapon)
        {
            SafeRefreshWeaponVisual();

            if (isLocalPlayer)
            {
                GDebug.Log($"무기 {oldWeapon} -> {newWeapon}");
            }

            // 서버 소비자는 모든 원본을 확정한 ServerPublish에서 한 번 알린다.
            if (!isServer)
            {
                NotifyWeaponObservers(newWeapon);
            }
        }

        private void OnHasWeaponChanged(bool oldHasWeapon, bool newHasWeapon)
        {
            SafeRefreshWeaponVisual();
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 11;

        bool IEquipmentWeaponTarget.HasWeapon => HasWeapon;
        WeaponKind IEquipmentWeaponTarget.Weapon => Weapon;

        /// <summary> 확정된 무기를 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            if (!HasWeapon)
            {
                return;
            }

            snapshot.Weapon = Weapon;
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
            _serverWeapon    = snapshot.Weapon;
            _serverHasWeapon = true;
            _serverStateInitialized = true;
            _weapon          = snapshot.Weapon;
            _hasWeapon       = true;

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
            ServerEnsureSourceInitialized();
            if (_serverHasWeapon)
            {
                Debug.LogWarning($"[PlayerWeapon] 이미 무기가 확정된 플레이어에 다시 배분하려 했다: {_serverWeapon}", this);
                return false;
            }

            if (!System.Enum.IsDefined(typeof(WeaponKind), weapon))
            {
                Debug.LogError($"[PlayerWeapon] 알 수 없는 무기를 배분하려 했다: {weapon}", this);
                return false;
            }

            _serverWeapon    = weapon;
            _serverHasWeapon = true;
            ServerPublishCurrentWeapon(false);
            return true;
        }

        /// <summary> 서버가 인벤토리의 장비 snapshot을 현재 무기로 적용한다 </summary>
        [Server]
        public bool ServerSetEquippedWeapon(in WeaponCombatSnapshot snapshot)
        {
            if (!ServerTryAcquireEquippedWeapon(snapshot, out EquipmentWeaponWrite write))
            {
                return false;
            }

            try
            {
                ServerApplyEquippedWeapon(write);
                ServerPublishEquippedWeapon(write);
                return true;
            }
            finally
            {
                ServerReleaseEquippedWeapon();
            }
        }

        [Server]
        public bool ServerTryAcquireEquippedWeapon(in WeaponCombatSnapshot snapshot,
            out EquipmentWeaponWrite write)
        {
            write = default;
            ServerEnsureSourceInitialized();
            if (_equipmentWriteLocked ||
                (!System.Enum.IsDefined(typeof(WeaponKind), snapshot.WeaponKind)) ||
                (!IsFinite(snapshot.AttackPower)) || (snapshot.AttackPower <= 0f))
            {
                return false;
            }

            write = new EquipmentWeaponWrite(_serverHasWeapon, _serverWeapon,
                snapshot.WeaponKind);
            _equipmentWriteLocked = true;
            return true;
        }

        [Server]
        public void ServerApplyEquippedWeapon(in EquipmentWeaponWrite write)
        {
            if ((!_equipmentWriteLocked) || (!write.IsValid) ||
                (_serverHasWeapon != write.ExpectedHasWeapon) ||
                (write.ExpectedHasWeapon && (_serverWeapon != write.ExpectedWeapon)))
            {
                throw new InvalidOperationException("역할 무기 계획이 현재 서버 원본과 다릅니다.");
            }

            _serverWeapon    = write.NextWeapon;
            _serverHasWeapon = true;
        }

        [Server]
        public void ServerPublishEquippedWeapon(in EquipmentWeaponWrite write)
        {
            if ((!_equipmentWriteLocked) || (_serverWeapon != write.NextWeapon) ||
                (!_serverHasWeapon))
            {
                throw new InvalidOperationException("적용되지 않은 역할 무기 계획은 공개할 수 없습니다.");
            }

            ServerPublishCurrentWeapon(write.ExpectedHasWeapon);
        }

        [Server]
        public void ServerReleaseEquippedWeapon()
        {
            _equipmentWriteLocked = false;
        }

        /// <summary> 런이 끝나 로비로 돌아갈 때 배분을 되돌린다 </summary>
        [Server]
        public void ServerClearForNewRun()
        {
            _serverHasWeapon = false;
            _serverStateInitialized = true;
            _hasWeapon       = false;
            _equipmentWriteLocked = false;
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

        [Server]
        private void ServerPublishCurrentWeapon(bool resetCombatChain)
        {
            try
            {
                _weapon = _serverWeapon;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerWeapon] 무기 종류 공개 실패: {e}", this);
            }

            try
            {
                _hasWeapon = _serverHasWeapon;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerWeapon] 무기 보유 공개 실패: {e}", this);
            }

            if (resetCombatChain && (_parryGauge != null))
            {
                try
                {
                    _parryGauge.ServerReset();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PlayerWeapon] 장비 교체 전투 상태 초기화 실패: {e}", this);
                }
            }

            NotifyWeaponObservers(_serverWeapon);
        }

        private void SafeRefreshWeaponVisual()
        {
            try
            {
                RefreshWeaponVisual();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerWeapon] 무기 표현 갱신 실패: {e}", this);
            }
        }

        private void NotifyWeaponObservers(WeaponKind weapon)
        {
            System.Action<WeaponKind> handlers = WeaponChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((System.Action<WeaponKind>) observers[i])(weapon);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PlayerWeapon] 무기 observer 실패: {e}", this);
                }
            }
        }

        [Server]
        private void ServerEnsureSourceInitialized()
        {
            if (_serverStateInitialized)
            {
                return;
            }

            _serverWeapon           = _weapon;
            _serverHasWeapon        = _hasWeapon;
            _serverStateInitialized = true;
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
