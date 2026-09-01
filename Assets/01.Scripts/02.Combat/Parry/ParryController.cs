// System
using System;
using System.Collections.Generic;

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
    /// <summary> 패링 입력과 서버 창·무기·상태·쿨다운·자원 검증을 소유한다 </summary>
    public class ParryController : NetworkBehaviour, IParryWindowTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        // 공격자별로 WindowId가 따로 증가하므로 창은 (공격자, 번호) 복합 키로만 식별한다
        private readonly Dictionary<ParryWindowKey, ParryWindow> _windows          = new Dictionary<ParryWindowKey, ParryWindow>();
        private readonly Dictionary<uint, ParryWindowKey>        _activeByAttacker = new Dictionary<uint, ParryWindowKey>();
        private readonly Dictionary<uint, uint>                  _lastIdByAttacker = new Dictionary<uint, uint>();
        private readonly List<ParryWindowKey>                    _expiredKeys      = new List<ParryWindowKey>();

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryGauge           _gauge;

        private uint             _localRequestId     = 0;
        private ParryWindowKey   _clientActiveWindow = default;
        private RequestIdTracker _requestIds;

        // 서버 전용
        private double _actionEndServerTime            = 0.0;
        private double _cooldownEndServerTime          = 0.0;
        private double _invincibleEndServerTime        = 0.0;
        private bool   _ownsTemporaryInvincibility     = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _gauge  = GetComponent<ParryGauge>();
        }

        private void Update()
        {
            if (isServer)
            {
                ServerUpdateTimers(NetworkTime.time);
            }

            if ((!isOwned) || (!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_PARRY)))
            {
                return;
            }

            ParryWindowKey key = _clientActiveWindow;
            if (!key.IsValid)
            {
                return;
            }

            _clientActiveWindow = default;
            CmdParry(NextRequestId(), key, NetworkTime.time);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if ((_status == null) || (_weapon == null) || (_roll == null) || (_gauge == null))
            {
                Debug.LogError("[ParryController] 패링에 필요한 PlayerStatus·PlayerWeapon·PlayerRollController·ParryGauge 중 하나가 없다", this);
            }
        }

        public override void OnStopServer()
        {
            if ((_ownsTemporaryInvincibility) && (_status != null) && (_status.IsInvincible))
            {
                _status.SetInvincible(false);
            }

            _windows.Clear();
            _activeByAttacker.Clear();
            _lastIdByAttacker.Clear();
            _expiredKeys.Clear();
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdParry(uint requestId, ParryWindowKey key, double clientEstimatedServerTime)
        {
            if (!_requestIds.TryAccept(requestId))
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null) || (_roll == null) || (_gauge == null))
            {
                return;
            }

            double now = NetworkTime.time;
            CleanupExpiredWindows(now);

            if (!_windows.TryGetValue(key, out ParryWindow window))
            {
                return;
            }

            if ((window.Consumed) || (window.IsExpired(now)) || (window.TargetNetId != netId) || (!window.HasValidSource()))
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            RunPhase       phase  = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;

            bool hasSword = ((_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Sword));
            PlayerActionContext context = new PlayerActionContext(
                phase,
                _status.LifeState,
                hasSword,
                (_roll.IsRolling) || (IsActionInProgress(now)),
                now,
                _cooldownEndServerTime,
                _status.Stamina,
                combat.ParryStaminaCost);

            if (!PlayerActionGate.CanStartAction(context))
            {
                return;
            }

            if (!_status.TrySpendStamina(combat.ParryStaminaCost))
            {
                return;
            }

            double actionDuration = combat.ParryStartupDuration
                + combat.ParryWindowDuration
                + combat.ParryRecoveryDuration;

            _actionEndServerTime   = now + actionDuration;
            _cooldownEndServerTime = _actionEndServerTime;

            double effectiveInputTime = Math.Max(
                now - Constants.Network.MAX_INPUT_TIME_BEHIND,
                Math.Min(clientEstimatedServerTime, now + Constants.Network.MAX_INPUT_TIME_AHEAD));

            bool success = ParryJudge.IsSuccess(
                effectiveInputTime,
                window.StartServerTime,
                window.EndServerTime);

            if ((success) && (window.TryConsume()))
            {
                _windows.Remove(key);
                _activeByAttacker.Remove(window.AttackerNetId);
                _gauge.ServerAddSuccess();
                BeginTemporaryInvincibility(now, combat.ParryInvincibleDuration);
                window.Source.OnParried(netId);
            }

            TargetParryResult(success);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 공격 소스가 확정한 타격 시각에서 역산해 이 플레이어를 노리는 패링 창을 연다.
        /// 같은 공격자의 이전 미소비 창은 자동 무효화한다
        /// </summary>
        [Server]
        public bool OpenParryWindow(IParryable source, ParryWindowKey key, double telegraphStartTime, double impactTime)
        {
            if ((!GameDB.IsInitialized) || (!key.IsValid) || (source == null))
            {
                return false;
            }

            if ((source is UnityEngine.Object unityObject) && (unityObject == null))
            {
                return false;
            }

            // 같은 공격자의 이전 번호는 재사용·순서 역전으로 보고 거부한다.
            // 공격자별 비교라 다른 공격자가 같은 번호를 써도 서로 방해하지 않는다
            if ((_lastIdByAttacker.TryGetValue(key.AttackerNetId, out uint lastWindowId)) && (key.WindowId <= lastWindowId))
            {
                return false;
            }

            float duration = Mathf.Clamp(source.ParryWindowDuration, 0f, GameDB.Combat.ParryWindowDurationMax);
            if (duration <= 0f)
            {
                return false; // 패링 불가 공격이다
            }

            // 판정이 타격보다 먼저 끝나야 지연 환경에서도 결과를 확정할 수 있다
            double windowEnd   = impactTime - Constants.Network.PARRY_RESOLUTION_LEAD;
            double windowStart = windowEnd - duration;

            double now = NetworkTime.time;
            if (windowEnd <= now)
            {
                Debug.LogWarning($"[ParryController] 이미 지난 창을 열려 했다 {key} (타격까지 {impactTime - now:F3}초)", this);
                return false;
            }

            // 텔레그래프보다 먼저 열리면 플레이어가 예고를 보기 전에 판정이 시작된다
            if (windowStart < telegraphStartTime)
            {
                Debug.LogWarning($"[ParryController] 텔레그래프가 짧아 창을 열지 못했다 {key} "
                    + $"(부족 {telegraphStartTime - windowStart:F3}초)", this);
                return false;
            }

            CleanupExpiredWindows(now);

            if (_activeByAttacker.TryGetValue(key.AttackerNetId, out ParryWindowKey previousKey))
            {
                _windows.Remove(previousKey);
            }

            ParryWindow window = new ParryWindow(
                key.WindowId,
                key.AttackerNetId,
                netId,
                windowStart,
                windowEnd,
                source);

            _windows[key]                      = window;
            _activeByAttacker[key.AttackerNetId] = key;
            _lastIdByAttacker[key.AttackerNetId] = key.WindowId;

            TargetOpenParryWindow(key);
            return true;
        }

        /// <summary>
        /// 공격이 취소되거나 공격자가 사라졌을 때 창을 즉시 닫는다.
        /// 만료를 기다리면 이미 없는 공격을 패링해 성공하는 상황이 생긴다
        /// </summary>
        [Server]
        public void CloseParryWindow(ParryWindowKey key)
        {
            if (!_windows.Remove(key))
            {
                return;
            }

            if ((_activeByAttacker.TryGetValue(key.AttackerNetId, out ParryWindowKey activeKey)) && (activeKey == key))
            {
                _activeByAttacker.Remove(key.AttackerNetId);
            }

            TargetCloseParryWindow(key);
        }

        /// <summary> 구르기 등 다른 행동 게이트가 확인하는 패링 진행 상태 </summary>
        [Server]
        public bool IsActionInProgress(double serverTime)
        {
            return serverTime < _actionEndServerTime;
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetOpenParryWindow(ParryWindowKey key)
        {
            _clientActiveWindow = key;
        }

        // 이미 다른 창으로 넘어간 뒤 늦게 도착한 닫기는 무시한다
        [TargetRpc]
        private void TargetCloseParryWindow(ParryWindowKey key)
        {
            if (_clientActiveWindow == key)
            {
                _clientActiveWindow = default;
            }
        }

        [TargetRpc]
        private void TargetParryResult(bool success)
        {
            if (success)
            {
                CombatFeedbackLocator.Current.PlayParrySuccess();
            }

            Debug.Log(success ? "[ParryController] 패링 성공" : "[ParryController] 패링 실패", this);
        }

        /****************************************
        *                Methods
        ****************************************/

        private uint NextRequestId()
        {
            ++_localRequestId;
            if (_localRequestId == 0)
            {
                ++_localRequestId;
            }

            return _localRequestId;
        }

        private void ServerUpdateTimers(double now)
        {
            CleanupExpiredWindows(now);

            if ((!_ownsTemporaryInvincibility) || (now < _invincibleEndServerTime))
            {
                return;
            }

            _ownsTemporaryInvincibility = false;
            if ((_status != null) && (_status.IsInvincible))
            {
                _status.SetInvincible(false);
            }
        }

        private void BeginTemporaryInvincibility(double now, float duration)
        {
            _invincibleEndServerTime    = now + duration;
            _ownsTemporaryInvincibility = true;
            _status.SetInvincible(true);
        }

        private void CleanupExpiredWindows(double now)
        {
            _expiredKeys.Clear();

            foreach (KeyValuePair<ParryWindowKey, ParryWindow> entry in _windows)
            {
                if (entry.Value.IsExpired(now))
                {
                    _expiredKeys.Add(entry.Key);
                }
            }

            for (int i = 0; i < _expiredKeys.Count; ++i)
            {
                ParryWindowKey key = _expiredKeys[i];
                if (!_windows.Remove(key))
                {
                    continue;
                }

                if ((_activeByAttacker.TryGetValue(key.AttackerNetId, out ParryWindowKey activeKey)) && (activeKey == key))
                {
                    _activeByAttacker.Remove(key.AttackerNetId);
                }
            }
        }
    }
}
