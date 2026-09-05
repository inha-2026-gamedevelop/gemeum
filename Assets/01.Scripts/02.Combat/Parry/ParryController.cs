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
    /// <summary> 패링 입력과 서버 창, 무기, 상태, 쿨다운, 자원 검증을 소유한다 </summary>
    public class ParryController : NetworkBehaviour, IParryWindowTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        // 대상당 활성 창은 항상 최대 1개다. 새 공격이 등록되면 소비 여부와 무관하게 이전 창을 밀어낸다
        private ParryWindow _activeWindow;

        // 활성 슬롯과 별개로 직전에 소비된 창의 tombstone을 하나 더 보관한다. 활성 슬롯 하나만 쓰면
        // tombstone이 될 창이 다른 공격의 등록으로 덮여 사라지고, 뒤늦게 온 중복 요청이
        // RejectedWindowConsumed가 아니라 RejectedWindowMismatch로 잘못 떨어진다
        private ParryWindowKey _consumedWindowKey;
        private double         _consumedWindowImpactTime;

        // 공격자마다 WindowId가 따로 증가하므로 재사용, 역전 요청을 막으려면 공격자별 마지막 값이 필요하다
        private readonly Dictionary<uint, uint> _lastIdByAttacker = new Dictionary<uint, uint>();

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryGauge           _gauge;
        private PlayerAttack         _attack;

        private uint             _localRequestId     = 0;
        private ParryWindowKey   _clientActiveWindow = default;
        private uint             _lastAppliedResultId = 0; // 소유 클라 전용, 늦게 온 옛 결과가 최신 상태를 되감지 않게 한다
        private RequestIdTracker _requestIds;

        // 서버 전용
        private double _actionLockEndServerTime = 0.0; // startup 또는 recovery가 끝나는 시각
        private double _lastRequestServerTime   = -1.0; // 거부 응답도 이 간격보다 자주 처리하지 않는다

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
        }

        private void Update()
        {
            if (isServer)
            {
                CleanupExpiredWindow(NetworkTime.time);
            }

            if ((!isOwned) || (!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_PARRY)))
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
                return; // 검증조차 안 되는 request는 결과도 보내지 않는다
            }

            // 2. duplicate/역순 검사
            if (!_requestIds.TryAccept(request.RequestId))
            {
                return;
            }

            double now = NetworkTime.time;

            // 거부 요청도 자원, 상태를 바꾸지 않으므로 비용 없이 연타될 수 있다. 최소 간격 미만이면
            // 결과도 보내지 않고 조용히 버린다. 정상 입력은 선딜, 후딜 때문에 이 간격에 걸리지 않는다
            if ((_lastRequestServerTime >= 0.0) && ((now - _lastRequestServerTime) < Constants.Network.PARRY_MIN_REQUEST_INTERVAL))
            {
                return;
            }

            _lastRequestServerTime = now;

            // 3. 필수 컴포넌트, GameDB 준비 확인
            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null) || (_roll == null) || (_gauge == null))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedDependency, now);
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            RunPhase       phase  = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool           paused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            // 4. RunPhase + PlayerLifeState
            if (!PlayerActionGate.CanAct(phase, _status.LifeState, paused))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedState, now);
                return;
            }

            // 5. 무기 확인
            if ((!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Sword))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedWeapon, now);
                return;
            }

            // 6. 현재 패링, 구르기, 공격 행동 잠금 확인
            if ((_roll.IsRolling) || (IsActionInProgress(now))
                || ((_attack != null) && (_attack.IsActionInProgress(now))))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedBusy, now);
                return;
            }

            CleanupExpiredWindow(now);

            // 7. 소비 tombstone과 관찰 키 비교
            bool matchesTombstone = ((_consumedWindowKey.IsValid)
                && (_consumedWindowKey == request.ObservedWindow)
                && (now < _consumedWindowImpactTime));

            if (matchesTombstone)
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedWindowConsumed, now);
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
                    TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedWindowMismatch, now);
                    return;
                }

                if (window.Consumed)
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedWindowConsumed, now);
                    return;
                }

                if (!window.HasValidSource())
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedDependency, now);
                    return;
                }

                // 9. 서버 수신 시각이 impact 이전인지 확인. 피해를 적용한 뒤 롤백하지 않으므로
                // 마감을 넘겨 도착한 입력은 실패가 아니라 거부다
                if (now >= window.ImpactTime)
                {
                    TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedResolutionClosed, now);
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

            // 12. 스태미나. 여기까지 통과한 시도만 자원과 행동 상태를 바꾼다
            if (!_status.TrySpendStamina(combat.ParryStaminaCost))
            {
                TargetParryResult(request.RequestId, request.ObservedWindow, ParryResultCode.RejectedStamina, now);
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
                // 16. 성공 보상. window를 먼저 비운 뒤 공격 측에 알린다
                _actionLockEndServerTime = startupEnd;

                _status.RestoreStaminaToMax();
                _gauge.ServerAddSuccess();
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

            // 18. 결과 전송
            TargetParryResult(request.RequestId, request.ObservedWindow, resultCode, now);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 공격 소스가 확정한 타격 시각에서 역산해 이 플레이어를 노리는 패링 창을 연다.
        /// 대상당 활성 창은 하나뿐이므로, 소비 여부와 무관하게 기존 창을 밀어낸다
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

            if ((!IsFinite(telegraphStartTime)) || (!IsFinite(impactTime)) || (telegraphStartTime >= impactTime))
            {
                return false;
            }

            // 같은 공격자의 이전 번호는 재사용, 순서 역전으로 보고 거부한다.
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

            TargetOpenParryWindow(key);
            return true;
        }

        /// <summary>
        /// 공격이 취소되거나 공격자가 사라졌을 때 창을 즉시 닫는다. 현재 활성 창과 정확히
        /// 일치할 때만 닫는다 - 늦게 끝난 이전 공격이 새 창을 닫아서는 안 된다
        /// </summary>
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
        private void TargetParryResult(uint requestId, ParryWindowKey windowKey, ParryResultCode resultCode, double serverResolvedAt)
        {
            // 더 오래된 결과가 늦게 도착해도 이미 반영된 최신 상태를 되감지 않는다
            if (requestId <= _lastAppliedResultId)
            {
                return;
            }

            _lastAppliedResultId = requestId;

            if (resultCode == ParryResultCode.Success)
            {
                CombatFeedbackLocator.Current.PlayParrySuccess();
            }

            // 이 TargetRpc는 패링한 본인에게만 오므로 항상 내 소리다.
            // 거부(Rejected*)에는 소리를 내지 않는다 - 창이 없었거나 요청이 반려된 것이지 타이밍을 놓친 것이 아니다
            if ((resultCode == ParryResultCode.Success) || (resultCode == ParryResultCode.MissTiming))
            {
                SfxPlayer.Play(
                    (resultCode == ParryResultCode.Success) ? SoundKey.ParrySuccess : SoundKey.ParryFail,
                    transform.position,
                    true,
                    netId);
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

        // ImpactTime까지는 서버 판정용 레코드를 살려 둔다. EndServerTime 직후 지우면 지연으로
        // 늦게 도착한 요청이 판정도 못 받고 버려진다
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
