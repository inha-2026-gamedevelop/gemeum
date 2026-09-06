// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// UI가 전투를 읽는 유일한 창구. 자기 캐릭터의 값만 다루고 판정에는 관여하지 않는다
    /// </summary>
    // 값을 그대로 넘기기만 하는 자리가 아니다. 이것이 없으면 HUD 패널마다 아래 셋을 다시 쓰게 된다.
    //
    // 1. 자기 캐릭터를 언제 잡을 수 있는지. UI는 스폰보다 먼저 켜져 있을 수 있어
    //    패널마다 "아직 null이면" 분기를 다시 쓰게 된다.
    // 2. 늦게 구독한 화면에 현재 값을 흘려 주는 것. PlayerStatus는 RaiseAll로 이미 하고 있는데
    //    락온과 부활에는 그런 것이 없어, 나중에 열린 패널은 잠긴 대상을 모른 채 시작한다.
    // 3. 사망이나 재접속으로 인스턴스가 갈릴 때 구독을 옮기는 것. 패널마다 하면 언젠가 하나를 빼먹는다.
    //
    // 이벤트와 프로퍼티를 가르는 기준은 값의 종류다. 생명 상태나 락온 여부처럼 드물게 갈리는 것은
    // 이벤트로 알리고, 게이지나 남은 시간처럼 계속 변하는 것은 프로퍼티로 읽게 한다 -
    // 매 서버 틱 바뀌는 값에 이벤트를 달면 초당 수십 번 호출이 나가고 UI가 하는 일은 같다.
    //
    // 이벤트 버스를 쓰지 않는 이유는 방향이 없어서다. 버스는 주인이 없는 사실(런 단계 전환,
    // 잡귀 사망)을 위한 것이고, 여기 값은 전부 "내 캐릭터의 것"이라 버스에 실으면
    // 누구의 값인지 페이로드에 넣고 UI가 다시 걸러야 한다
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

        // 드물게 갈리는 값만 이벤트로 알린다. 계속 변하는 값은 아래 프로퍼티로 읽는다

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

        /// <summary>
        /// 자기 캐릭터를 창구에 건다. PlayerStatus의 OnStartLocalPlayer가 부른다 -
        /// 그 시점이면 형제 컴포넌트의 Awake가 전부 끝나 있다
        /// </summary>
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

        /// <summary>
        /// 지금 값을 이벤트로 한 번 흘려 준다. 구독 직후에 부르면 늦게 열린 화면도 처음부터 맞는다.
        /// 이것이 없으면 패널마다 "구독 전에 이미 바뀐 값"을 각자 따라잡아야 한다
        /// </summary>
        public static void RaiseAll()
        {
            if (_status == null)
            {
                return;
            }

            // PlayerStatus가 생명 상태까지 흘려 주고 그것이 OnLifeStateChanged로 되돌아온다.
            // 여기서 또 쏘면 늦게 구독한 화면이 같은 전이를 두 번 받는다
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

        /// <summary>
        /// 도메인 리로드를 끈 상태에서는 정적 구독이 플레이 세션 사이에 남는다.
        /// 지난 판의 UI 핸들러가 남으면 파괴된 오브젝트를 건드려 첫 프레임에 예외가 난다
        /// </summary>
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
