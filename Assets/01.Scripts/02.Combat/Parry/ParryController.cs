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
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 패링 입력과 서버 창, 무기, 상태, 쿨다운, 자원 검증을 소유한다 </summary>
    public class ParryController : NetworkBehaviour, IParryWindowTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        // 대상당 활성 창은 항상 최대 1개다
        private ParryWindow _activeWindow;

        // 직전에 소비된 창의 tombstone
        private ParryWindowKey _consumedWindowKey;
        private double         _consumedWindowImpactTime;

        private readonly Dictionary<uint, uint> _lastIdByAttacker = new Dictionary<uint, uint>();

        private PlayerStatus              _status;
        private PlayerWeapon              _weapon;
        private PlayerRollController      _roll;
        private ParryGauge                _gauge;
        private PlayerAttack              _attack;
        private PlayerAnimationController _animation;
        private ICombatLoadoutProvider     _loadoutProvider;

        private uint             _localRequestId     = 0;
        private ParryWindowKey   _clientActiveWindow = default;
        private uint             _lastAppliedResultId = 0; // 소유 클라 전용
        private uint             _clientPresentationActionId;
        private RequestIdTracker _requestIds;

        // 서버 전용
        private double _actionLockEndServerTime = 0.0; // startup 또는 recovery가 끝나는 시각
        private double _lastRequestServerTime   = -1.0;
        private uint   _nextPresentationActionId = 1u;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _gauge  = GetComponent<ParryGauge>();
            _attack = GetComponent<PlayerAttack>();

            _animation = GetComponent<PlayerAnimationController>();
            _loadoutProvider = GetComponent<ICombatLoadoutProvider>();
        }

        private void Update()
        {
            if (isServer)
            {
                CleanupExpiredWindow(NetworkTime.time);
            }

            if ((!isOwned) || (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_PARRY)))
            {
                return;
            }

            ParryRequest request = new ParryRequest
            {
                RequestId                 = NextRequestId(),
                ObservedWindow            = _clientActiveWindow,
                ClientEstimatedServerTime = NetworkTime.time,
            };

            CmdRequestParry(request);
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if ((_status == null) || (_weapon == null) || (_roll == null) || (_gauge == null))
            {
                Debug.LogError("[ParryController] 패링에 필요한 PlayerStatus, PlayerWeapon, PlayerRollController, ParryGauge 중 하나가 없다", this);
            }
        }

        public override void OnStopServer()
        {
            _activeWindow      = null;
            _consumedWindowKey = default;
            _lastIdByAttacker.Clear();
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestParry(ParryRequest request)
        {
            // 1. request ID, 시각 유한성 검사
            if ((request.RequestId == 0) || (!IsFinite(request.ClientEstimatedServerTime)))
            {
                return;
            }

            // 2. duplicate/역순 검사
            if (!_requestIds.TryAccept(request.RequestId))
            {
                return;
            }

            double now = NetworkTime.time;

            if ((_lastRequestServerTime >= 0.0) && ((now - _lastRequestServerTime) < Constants.Network.PARRY_MIN_REQUEST_INTERVAL))
            {
                return;
            }

            _lastRequestServerTime = now;

            // 3. 필수 컴포넌트, GameDB 준비 확인
            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null) || (_roll == null) || (_gauge == null))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedDependency, now, 0d);
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            RunPhase       phase  = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool           paused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            // 4. RunPhase + PlayerLifeState
            if (!PlayerActionGate.CanAct(phase, _status.LifeState, paused))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedState, now, 0d);
                return;
            }

            // 5. 무기 확인
            if ((!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Sword))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedWeapon, now, 0d);
                return;
            }

            ParticipantId participant = _status.Participant;
            if ((_loadoutProvider == null) || (!participant.IsValid) ||
                (!_loadoutProvider.TryBuildLoadout(participant,
                    out CombatStatsSnapshot loadout)) || (!loadout.IsValid))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedDependency, now, 0d);
                return;
            }

            // 6. 현재 패링, 구르기, 공격 행동 잠금 확인
            if ((_roll.IsRolling) || (IsActionInProgress(now))
                || ((_attack != null) && (_attack.IsActionInProgress(now))))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedBusy, now, 0d);
                return;
            }

            CleanupExpiredWindow(now);

            // 7. 소비 tombstone과 관찰 키 비교
            bool matchesTombstone = ((_consumedWindowKey.IsValid)
                && (_consumedWindowKey == request.ObservedWindow)
                && (now < _consumedWindowImpactTime));

            if (matchesTombstone)
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedWindowConsumed, now, 0d);
                return;
            }

            // 8. 서버 활성 window와 관찰 키 일치 여부 확인
            ParryWindow window             = _activeWindow;
            bool        hasObservedWindow  = request.ObservedWindow.IsValid;
            bool        hasActiveWindow    = (window != null);

            if ((hasObservedWindow) || (hasActiveWindow))
            {
                bool keysMatch = ((hasObservedWindow) && (hasActiveWindow)
                    && (window.AttackerNetId == request.ObservedWindow.AttackerNetId)
                    && (window.WindowId == request.ObservedWindow.WindowId));

                if (!keysMatch)
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow,
                        ParryResultCode.RejectedWindowMismatch, now, 0d);
                    return;
                }

                if (window.Consumed)
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow,
                        ParryResultCode.RejectedWindowConsumed, now, 0d);
                    return;
                }

                if (!window.HasValidSource())
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow,
                        ParryResultCode.RejectedDependency, now, 0d);
                    return;
                }

                // 9. 서버 수신 시각이 impact 이전인지 확인
                if (now >= window.ImpactTime)
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow,
                        ParryResultCode.RejectedResolutionClosed, now, 0d);
                    return;
                }
            }

            // 10~11. 입력 시각을 연결 rtt 기반 하한으로 보정하고 선딜을 반영한 판정 시각을 계산한다
            double allowedBehind = Constants.Network.PARRY_MAX_INPUT_TIME_BEHIND;
            if ((connectionToClient != null) && (connectionToClient.rtt > 0.0))
            {
                allowedBehind = Math.Min(allowedBehind, (connectionToClient.rtt * 0.5) + Constants.Network.PARRY_INPUT_TIME_TOLERANCE);
            }

            double effectiveInputTime = Math.Max(
                now - allowedBehind,
                Math.Min(request.ClientEstimatedServerTime, now + Constants.Network.MAX_INPUT_TIME_AHEAD));

            double parryActivationTime = effectiveInputTime + combat.ParryStartupDuration;

            // 12. 스태미나 확인
            if (!_status.TrySpendStamina(
                    loadout.ResolveStaminaCost(combat.ParryStaminaCost)))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow,
                    ParryResultCode.RejectedStamina, now, 0d);
                return;
            }

            // 13~14. window 없으면 허공 패링, 있으면 결과와 무관하게 여기서 한 번 소비한다
            bool             success;
            ParryResultCode  resultCode;

            if (!hasActiveWindow)
            {
                success    = false;
                resultCode = ParryResultCode.MissNoWindow;
            }
            else
            {
                window.TryConsume();
                _consumedWindowKey        = request.ObservedWindow;
                _consumedWindowImpactTime = window.ImpactTime;

                // 15. 순수 시간 판정
                success    = ParryJudge.IsSuccess(parryActivationTime, window.StartServerTime, window.EndServerTime);
                resultCode = success ? ParryResultCode.Success : ParryResultCode.MissTiming;
            }

            double startupEnd = Math.Max(parryActivationTime, now);

            if (success)
            {
                // 16. 성공 보상
                _actionLockEndServerTime = startupEnd;

                _status.RestoreStaminaToMax();
                _gauge.ServerAddSuccess(loadout.ParryGaugeGain);
                _status.GrantTimedInvincibility(now + combat.ParryInvincibleDuration);

                _activeWindow = null;
                TargetCloseParryWindow(request.ObservedWindow);

                window.Source.OnParried(netId);
            }
            else
            {
                // 17. 실패 후딜
                _actionLockEndServerTime = startupEnd + combat.ParryRecoveryDuration;

                if (hasActiveWindow)
                {
                    _activeWindow = null;
                    TargetCloseParryWindow(request.ObservedWindow);
                }
            }

            uint presentationActionId = TakeNextPresentationActionId();
            double gameplayStart = RunTimeLocator.Current.GameplayElapsedSeconds;
            double presentationImpact = gameplayStart + combat.ParryStartupDuration;
            PlayerActionCue cue = new PlayerActionCue
            {
                ExecutionId = presentationActionId,
                Kind = PlayerActionCueKind.Parry,
                Weapon = WeaponKind.Sword,
                ComboStage = 0,
                StartGameplayTime = gameplayStart,
                ImpactGameplayTime = presentationImpact,
                EndGameplayTime = presentationImpact +
                    (success ? 0.01f : Mathf.Max(0.01f,
                        combat.ParryRecoveryDuration)),
            };
            RpcParryStarted(cue);

            // 18. 결과 전송
            TargetParryResult(request.RequestId, request.ObservedWindow,
                resultCode, now, gameplayStart);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 공격 소스가 확정한 타격 시각에서 역산해 이 플레이어를 노리는 패링 창을 연다 </summary>
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

            if ((!IsFinite(telegraphStartTime)) || (!IsFinite(impactTime)) || (telegraphStartTime >= impactTime))
            {
                return false;
            }

            if ((_lastIdByAttacker.TryGetValue(key.AttackerNetId, out uint lastWindowId)) && (key.WindowId <= lastWindowId))
            {
                return false;
            }

            if ((_loadoutProvider == null) || (_status == null) ||
                (!_status.Participant.IsValid) ||
                (!_loadoutProvider.TryBuildLoadout(_status.Participant,
                    out CombatStatsSnapshot loadout)) || (!loadout.IsValid))
            {
                return false;
            }

            float duration = Mathf.Clamp(
                source.ParryWindowDuration + loadout.ParryWindowBonus,
                0f,
                GameDB.Combat.ParryWindowDurationMax);
            if (duration <= 0f)
            {
                return false;
            }

            double windowEnd   = impactTime - Constants.Network.PARRY_RESOLUTION_LEAD;
            double windowStart = windowEnd - duration;

            double now = NetworkTime.time;
            if (windowEnd <= now)
            {
                Debug.LogWarning($"[ParryController] 이미 지난 창을 열려 했다 {key} (타격까지 {impactTime - now:F3}초)", this);
                return false;
            }

            if (windowStart < telegraphStartTime)
            {
                Debug.LogWarning($"[ParryController] 텔레그래프가 짧아 창을 열지 못했다 {key} "
                    + $"(부족 {telegraphStartTime - windowStart:F3}초)", this);
                return false;
            }

            CleanupExpiredWindow(now);

            bool           hadPrevious = (_activeWindow != null);
            ParryWindowKey previousKey = hadPrevious
                ? new ParryWindowKey(_activeWindow.AttackerNetId, _activeWindow.WindowId)
                : default;

            ParryWindow window = new ParryWindow(
                key.WindowId,
                key.AttackerNetId,
                netId,
                windowStart,
                windowEnd,
                impactTime,
                source);

            _activeWindow                        = window;
            _lastIdByAttacker[key.AttackerNetId] = key.WindowId;

            if (hadPrevious)
            {
                Debug.Log($"[ParryController] 활성 창 교체 {previousKey} -> {key}", this);
                TargetCloseParryWindow(previousKey);
            }

            TargetOpenParryWindow(key, windowStart, windowEnd);
            return true;
        }

        /// <summary> 공격이 취소되거나 공격자가 사라졌을 때 창을 즉시 닫는다 </summary>
        [Server]
        public void CloseParryWindow(ParryWindowKey key)
        {
            if ((_activeWindow == null) || (_activeWindow.AttackerNetId != key.AttackerNetId) || (_activeWindow.WindowId != key.WindowId))
            {
                return;
            }

            _activeWindow = null;
            TargetCloseParryWindow(key);
        }

        /// <summary> 구르기 등 다른 행동 게이트가 확인하는 패링 진행 상태 </summary>
        [Server]
        public bool IsActionInProgress(double serverTime)
        {
            return serverTime < _actionLockEndServerTime;
        }

        /****************************************
        *              ClientRpc
        ****************************************/

        /// <summary> 패링 자세를 모든 화면에서 재생한다 </summary>
        [ClientRpc]
        private void RpcParryStarted(PlayerActionCue cue)
        {
            _clientPresentationActionId = cue.ExecutionId;
            if (_animation == null)
            {
                return;
            }

            _animation.PlayActionCue(in cue);
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetOpenParryWindow(ParryWindowKey key, double windowStart, double windowEnd)
        {
            _clientActiveWindow = key;

            // 이 신호는 공격 모션 시작에 온다. 실제 창은 타격 시각에서 역산한 뒤쪽 구간이다.
            double now = NetworkTime.time;
            GDebug.Log($"패링 창 예고 +{windowStart - now:F2}~+{windowEnd - now:F2}초");
        }

        [TargetRpc]
        private void TargetCloseParryWindow(ParryWindowKey key)
        {
            if (_clientActiveWindow == key)
            {
                _clientActiveWindow = default;
            }
        }

        [TargetRpc]
        private void TargetParryResult(uint requestId, ParryWindowKey windowKey,
            ParryResultCode resultCode, double serverResolvedAt,
            double resultGameplayTime)
        {
            if (requestId <= _lastAppliedResultId)
            {
                return;
            }

            _lastAppliedResultId = requestId;

            // 거부 8종과 성공, 타이밍 실패가 전부 여기를 지난다. 화면에 사유까지 그대로 띄운다
            if (resultCode == ParryResultCode.Success)
            {
                GDebug.Success("패링 성공");
            }
            else if (resultCode == ParryResultCode.MissTiming)
            {
                GDebug.Fail("패링 실패, 타이밍");
            }
            else if (resultCode == ParryResultCode.MissNoWindow)
            {
                GDebug.Warn("패링 헛침, 열린 창 없음");
            }
            else
            {
                GDebug.Warn($"패링 거부: {resultCode}");
            }

            if (resultCode == ParryResultCode.Success)
            {
                CombatFeedbackLocator.Current.PlayParrySuccess();
            }

            if ((resultCode == ParryResultCode.Success) ||
                (resultCode == ParryResultCode.MissTiming) ||
                (resultCode == ParryResultCode.MissNoWindow))
            {
                _animation?.PlayActionImpact(PlayerActionCueKind.Parry,
                    _clientPresentationActionId,
                    resultCode == ParryResultCode.Success, false,
                    resultGameplayTime);
            }

            Debug.Log($"[ParryController] 패링 결과 {resultCode}", this);
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

        private uint TakeNextPresentationActionId()
        {
            uint result = _nextPresentationActionId;
            _nextPresentationActionId = (_nextPresentationActionId == uint.MaxValue)
                ? 1u
                : _nextPresentationActionId + 1u;
            return result;
        }

        private void CleanupExpiredWindow(double now)
        {
            if ((_activeWindow != null) && (_activeWindow.IsExpired(now)))
            {
                _activeWindow = null;
            }

            if ((_consumedWindowKey.IsValid) && (now >= _consumedWindowImpactTime))
            {
                _consumedWindowKey = default;
            }
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
