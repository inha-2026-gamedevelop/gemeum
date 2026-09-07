// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> UI가 전투를 읽는 유일한 창구. 자기 캐릭터의 값만 다루고 판정에는 관여하지 않는다 </summary>
    public static class LocalCombatState
    {
        /****************************************
        *                Fields
        ****************************************/

        private static PlayerStatus      _status;
        private static ParryGauge        _gauge;
        private static PlayerWeapon      _weapon;
        private static LockOnController  _lockOn;
        private static ReviveController  _revive;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 자기 캐릭터를 잡았거나 놓았다. true면 아래 값들이 유효하다 </summary>
        public static event Action<bool> BoundChanged;

        /// <summary> 생명 상태가 바뀌었다 </summary>
        public static event Action<PlayerLifeState> LifeStateChanged;

        /// <summary> 장착 무기가 바뀌었다 </summary>
        public static event Action<WeaponKind> WeaponChanged;

        /// <summary> 락온을 걸거나 풀었다 </summary>
        public static event Action<bool> LockOnChanged;

        /// <summary> 패링 게이지가 최대치에 닿았거나 소비됐다. true면 다음 근접이 치명의 일격이다 </summary>
        public static event Action<bool> CriticalReadyChanged;

        /// <summary> 누군가를 일으키기 시작했거나 그만뒀다 </summary>
        public static event Action<bool> RescuingChanged;

        /// <summary> 단발 행동 요청이 거부됐다. 입력이 씹힌 것과 구분해서 보여줄 수 있다 </summary>
        public static event Action<ActionRejection> ActionRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 자기 캐릭터를 잡았는지. false면 아래 값은 전부 기본값이다 </summary>
        public static bool IsBound => (_status != null);

        public static float Health    => (_status != null) ? _status.Health : 0f;
        public static float MaxHealth => (_status != null) ? _status.MaxHealth : 0f;
        public static float Stamina   => (_status != null) ? _status.Stamina : 0f;
        public static float Spirit    => (_status != null) ? _status.Spirit : 0f;

        public static PlayerLifeState LifeState => (_status != null) ? _status.LifeState : PlayerLifeState.Alive;

        /// <summary> 패링 게이지 현재값. 최대치는 GameDB의 ParryGaugeMax다 </summary>
        public static float ParryGauge => (_gauge != null) ? _gauge.Gauge : 0f;

        /// <summary> 다음 근접 공격이 치명의 일격인지 </summary>
        public static bool IsCriticalReady => ((_gauge != null) && (_gauge.IsCriticalReady));

        public static bool       HasWeapon => ((_weapon != null) && (_weapon.HasWeapon));
        public static WeaponKind Weapon    => (_weapon != null) ? _weapon.Weapon : default;

        public static bool IsLockedOn => ((_lockOn != null) && (_lockOn.IsLockedOn));

        /// <summary> 락온 마커를 그릴 월드 좌표. 락온 중이 아니면 의미가 없다 </summary>
        public static Vector3 LockOnAimPoint => (_lockOn != null) ? _lockOn.TargetAimPoint : Vector3.zero;

        /// <summary> 관전이 끝나기까지 남은 초. 쓰러진 상태가 아니면 0이다 </summary>
        public static double SpectateRemainingSeconds => (_revive != null) ? _revive.SpectateRemainingSeconds : 0.0;

        /// <summary> 내가 누군가를 일으키는 진행도(0~1) </summary>
        public static float RescueProgress => (_revive != null) ? _revive.RescueProgress : 0f;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 자기 캐릭터를 창구에 건다 </summary>
        public static void Bind(GameObject localPlayer)
        {
            if (localPlayer == null)
            {
                return;
            }

            Unbind();

            _status = localPlayer.GetComponent<PlayerStatus>();
            _gauge  = localPlayer.GetComponent<ParryGauge>();
            _weapon = localPlayer.GetComponent<PlayerWeapon>();
            _lockOn = localPlayer.GetComponent<LockOnController>();
            _revive = localPlayer.GetComponent<ReviveController>();

            Attach();

            BoundChanged?.Invoke(true);
            RaiseAll();
        }

        /// <summary> 자기 캐릭터를 놓는다. 재접속으로 인스턴스가 갈릴 때도 여기를 지난다 </summary>
        public static void Unbind()
        {
            if (_status == null)
            {
                return;
            }

            Detach();

            _status = null;
            _gauge  = null;
            _weapon = null;
            _lockOn = null;
            _revive = null;

            BoundChanged?.Invoke(false);
        }

        /// <summary> 지금 값을 이벤트로 한 번 흘려 준다 </summary>
        public static void RaiseAll()
        {
            if (_status == null)
            {
                return;
            }

            _status.RaiseAll();

            WeaponChanged?.Invoke(Weapon);
            LockOnChanged?.Invoke(IsLockedOn);
            CriticalReadyChanged?.Invoke(IsCriticalReady);
            RescuingChanged?.Invoke((_revive != null) && (_revive.IsRescuing));
        }

        /// <summary> 거부 사유를 화면에 전달한다. 각 행동의 TargetRpc가 부른다 </summary>
        public static void RaiseActionRejected(ActionRejection rejection)
        {
            ActionRejected?.Invoke(rejection);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void Attach()
        {
            if (_status != null)
            {
                _status.LifeStateChanged += OnLifeStateChanged;
            }

            if (_weapon != null)
            {
                _weapon.WeaponChanged += OnWeaponChanged;
            }

            if (_lockOn != null)
            {
                _lockOn.LockOnChanged += OnLockOnChanged;
            }

            if (_gauge != null)
            {
                _gauge.CriticalReadyChanged += OnCriticalReadyChanged;
            }

            if (_revive != null)
            {
                _revive.RescuingChanged += OnRescuingChanged;
            }
        }

        private static void Detach()
        {
            if (_status != null)
            {
                _status.LifeStateChanged -= OnLifeStateChanged;
            }

            if (_weapon != null)
            {
                _weapon.WeaponChanged -= OnWeaponChanged;
            }

            if (_lockOn != null)
            {
                _lockOn.LockOnChanged -= OnLockOnChanged;
            }

            if (_gauge != null)
            {
                _gauge.CriticalReadyChanged -= OnCriticalReadyChanged;
            }

            if (_revive != null)
            {
                _revive.RescuingChanged -= OnRescuingChanged;
            }
        }

        private static void OnLifeStateChanged(PlayerLifeState lifeState)
        {
            LifeStateChanged?.Invoke(lifeState);
        }

        private static void OnWeaponChanged(WeaponKind weapon)
        {
            WeaponChanged?.Invoke(weapon);
        }

        private static void OnLockOnChanged(bool isLockedOn)
        {
            LockOnChanged?.Invoke(isLockedOn);
        }

        private static void OnCriticalReadyChanged(bool isReady)
        {
            CriticalReadyChanged?.Invoke(isReady);
        }

        private static void OnRescuingChanged(bool isRescuing)
        {
            RescuingChanged?.Invoke(isRescuing);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _status = null;
            _gauge  = null;
            _weapon = null;
            _lockOn = null;
            _revive = null;

            BoundChanged         = null;
            LifeStateChanged     = null;
            WeaponChanged        = null;
            LockOnChanged        = null;
            CriticalReadyChanged = null;
            RescuingChanged      = null;
            ActionRejected       = null;
        }
    }
}
