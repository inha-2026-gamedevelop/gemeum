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
    public class ParryController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<uint, ParryWindow> _windowsById          = new Dictionary<uint, ParryWindow>();
        private readonly Dictionary<uint, uint>        _activeByAttacker     = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint>        _lastIdByAttacker     = new Dictionary<uint, uint>();
        // TODO: 몬스터 공격이 OpenParryWindow를 호출하게 되면 _seenWindowIds를 제거한다.
        //       창을 열 때마다 커지는 영구 집합이라 장시간 전투에서 계속 자라고,
        //       공격자마다 windowId를 1부터 발급하면 두 번째 공격자의 창이 중복으로 판정돼 조용히 버려진다.
        //       재사용 방어는 _lastIdByAttacker 단조 증가 비교로 충분하고,
        //       번호 충돌은 _windowsById.ContainsKey로 열려 있는 창에 대해서만 막으면 된다.
        //       지금은 공격 소스가 없어 검증할 수 없으므로 바꾸지 않는다
        private readonly HashSet<uint>                 _seenWindowIds        = new HashSet<uint>();
        private readonly List<uint>                    _expiredWindowIds     = new List<uint>();

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryGauge           _gauge;

        private uint             _localRequestId       = 0;
        private uint             _clientActiveWindowId = 0;
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

            uint windowId = _clientActiveWindowId;
            if (windowId == 0)
            {
                return;
            }

            _clientActiveWindowId = 0;
            CmdParry(NextRequestId(), windowId, NetworkTime.time);
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

            _windowsById.Clear();
            _activeByAttacker.Clear();
            _lastIdByAttacker.Clear();
            _seenWindowIds.Clear();
            _expiredWindowIds.Clear();
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdParry(uint requestId, uint windowId, double clientEstimatedServerTime)
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

            if (!_windowsById.TryGetValue(windowId, out ParryWindow window))
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
        /// 공격 소스가 발급한 단조 증가 window ID로 이 플레이어를 노리는 패링 창을 연다.
        /// 같은 공격자의 이전 미소비 창은 자동 무효화한다
        /// </summary>
        [Server]
        public bool OpenParryWindow(uint windowId, uint attackerNetId, IParryable source)
        {
            if ((!GameDB.IsInitialized) || (windowId == 0) || (attackerNetId == 0) || (source == null))
            {
                return false;
            }

            if ((source is UnityEngine.Object unityObject) && (unityObject == null))
            {
                return false;
            }

            if (((_lastIdByAttacker.TryGetValue(attackerNetId, out uint lastWindowId)) && (windowId <= lastWindowId))
                || (!_seenWindowIds.Add(windowId)))
            {
                return false;
            }

            double now = NetworkTime.time;
            CleanupExpiredWindows(now);

            if (_activeByAttacker.TryGetValue(attackerNetId, out uint previousWindowId))
            {
                _windowsById.Remove(previousWindowId);
            }

            float duration = Mathf.Clamp(source.ParryWindowDuration, 0f, GameDB.Combat.ParryWindowDurationMax);
            if (duration <= 0f)
            {
                return false;
            }

            ParryWindow window = new ParryWindow(
                windowId,
                attackerNetId,
                netId,
                now,
                now + duration,
                source);

            _windowsById.Add(windowId, window);
            _activeByAttacker[attackerNetId] = windowId;
            _lastIdByAttacker[attackerNetId] = windowId;

            TargetOpenParryWindow(windowId);
            return true;
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
        private void TargetOpenParryWindow(uint windowId)
        {
            _clientActiveWindowId = windowId;
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
            _expiredWindowIds.Clear();

            foreach (KeyValuePair<uint, ParryWindow> entry in _windowsById)
            {
                if (entry.Value.IsExpired(now))
                {
                    _expiredWindowIds.Add(entry.Key);
                }
            }

            for (int i = 0; i < _expiredWindowIds.Count; ++i)
            {
                uint windowId = _expiredWindowIds[i];
                if (!_windowsById.TryGetValue(windowId, out ParryWindow window))
                {
                    continue;
                }

                _windowsById.Remove(windowId);

                if ((_activeByAttacker.TryGetValue(window.AttackerNetId, out uint activeWindowId))
                    && (activeWindowId == windowId))
                {
                    _activeByAttacker.Remove(window.AttackerNetId);
                }
            }
        }
    }
}
