// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary>
    /// 사망 후 관전과 부활을 소유한다. 자동 부활(30초)과 협동 부활(팀원이 3초 유지) 둘 다 여기가 확정한다.
    ///
    /// 자동 부활만 있으면 코옵에서 긴장이 가장 높은 순간이 "30초 기다리기"로 처리된다 -
    /// 동료가 쓰러졌는데 살아있는 팀원이 할 수 있는 일이 없다. 구조가 손실을 반으로 줄이므로
    /// "구하러 갈 가치"가 수치로 생기고, 그 이득이 개인이 아니라 팀 공용 재화라 코옵 동기가 맞는다.
    /// 30초 자동 부활은 안전망으로 남는다
    /// </summary>
    // 컴포넌트 하나가 두 역할을 겸한다 - 내가 쓰러졌을 때의 타이머와
    // 내가 남을 구조할 때의 진행도. 사람마다 하나씩 붙으므로 둘을 나눌 이유가 없고,
    // 구조자 쪽 취소 조건(이동, 공격, 구르기)을 확인하려면 어차피 자기 컴포넌트들을 봐야 한다.
    //
    // 판정과 부활 확정은 전부 서버다. 클라이언트가 보내는 것은 "누구를 일으키겠다"는 요청뿐이고
    // 거리, 생존, 취소 조건은 서버가 매 틱 다시 본다
    public class ReviveController : NetworkBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint NO_TARGET = 0u;

        // 구조 후보를 찾을 때 한 번에 볼 콜라이더 수. 정원이 3인이라 넉넉하다
        private const int RESCUE_BUFFER_SIZE = 8;

        // 서버가 "움직이고 있다"고 볼 최소 평면 속도(m/s). 물리 잔여 속도로 구조가 끊기지 않게 한다
        private const float RESCUE_MOVE_EPSILON = 0.15f;

        /****************************************
        *                Fields
        ****************************************/

        // 관전이 끝나는 서버 시각. UI가 남은 시간을 그려야 해서 동기화한다
        [SyncVar] private double _spectateEndServerTime = 0.0;

        // 내가 일으키는 중인 대상과 진행도(0~1). 둘 다 남의 화면에도 보여야 한다 -
        // 쓰러진 사람은 자기를 누가 일으키는지 봐야 하고, 나머지 한 명은 갈지 말지 정해야 한다
        [SyncVar(hook = nameof(OnRescueTargetChanged))] private uint  _rescueTargetNetId = NO_TARGET;
        [SyncVar]                                      private float _rescueProgress    = 0f;

        private PlayerStatus         _status;
        private PlayerController     _movement;
        private PlayerRollController _roll;
        private PlayerAttack         _attack;
        private ParryController      _parry;
        private Rigidbody            _rigidbody;

        private int _playerLayerMask = 0;

        private readonly Collider[] _rescueBuffer = new Collider[RESCUE_BUFFER_SIZE];

        // 소유 클라이언트 전용. 키를 누른 순간에만 요청을 보내고 떼면 취소한다
        private bool _isHoldingRescue = false;

        /****************************************
        *                Events
        ****************************************/

        // 진행도와 남은 시간은 계속 변하므로 UI가 프로퍼티로 읽는다.
        // 이벤트는 구조를 시작하고 그만두는 이산 전이만 알린다
        /// <summary> 누군가를 일으키기 시작했거나 그만뒀다 </summary>
        public event Action<bool> RescuingChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 관전이 끝나는 서버 시각 </summary>
        public double SpectateEndServerTime => _spectateEndServerTime;

        /// <summary>
        /// 관전이 끝나기까지 남은 초. 쓰러진 상태가 아니면 0이다.
        /// 남은 시간을 여기서 계산하는 이유는 호출부마다 NetworkTime을 빼면
        /// 화면과 서버가 다른 기준으로 셀 수 있기 때문이다
        /// </summary>
        public double SpectateRemainingSeconds
        {
            get
            {
                if (_spectateEndServerTime <= 0.0)
                {
                    return 0.0;
                }

                return Math.Max(0.0, _spectateEndServerTime - NetworkTime.time);
            }
        }

        /// <summary> 내가 누군가를 일으키는 중이면 0~1, 아니면 0 </summary>
        public float RescueProgress => _rescueProgress;

        /// <summary> 지금 누군가를 일으키는 중인지 </summary>
        public bool IsRescuing => (_rescueTargetNetId != NO_TARGET);

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnRescueTargetChanged(uint oldNetId, uint newNetId)
        {
            bool wasRescuing = (oldNetId != NO_TARGET);
            bool isRescuing  = (newNetId != NO_TARGET);

            if (wasRescuing == isRescuing)
            {
                return;
            }

            RescuingChanged?.Invoke(isRescuing);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status    = GetComponent<PlayerStatus>();
            _movement  = GetComponent<PlayerController>();
            _roll      = GetComponent<PlayerRollController>();
            _attack    = GetComponent<PlayerAttack>();
            _parry     = GetComponent<ParryController>();
            _rigidbody = GetComponent<Rigidbody>();

            _playerLayerMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            HandleRescueInput();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if (!GameEventBus.HasInstance)
            {
                Debug.LogError("[ReviveController] GameEventBus가 없어 재개 통지를 받을 수 없다", this);
                return;
            }

            GameEventBus.Instance.Subscribe<RunResumedEvent>(OnRunResumed);
        }

        public override void OnStopServer()
        {
            if (!GameEventBus.HasInstance)
            {
                return;
            }

            GameEventBus.Instance.Unsubscribe<RunResumedEvent>(OnRunResumed);
        }

        /// <summary>
        /// 런이 얼어 있던 만큼 관전 종료 시각을 뒤로 민다.
        ///
        /// 종료 시각이 NetworkTime 절대 시각이라 멈춰 있는 동안에도 계속 다가온다.
        /// 밀지 않으면 이탈자를 3분 기다린 판에서 그 전에 쓰러진 사람이 관전을 한 번도 하지 않고
        /// 재개 첫 틱에 일어나며, 노잣돈은 그대로 빠진다.
        ///
        /// 무적이나 후딜처럼 짧은 시각은 밀지 않는다 - 몇 분 멈췄다 돌아왔는데 그때까지 굳어 있는 쪽이
        /// 오히려 이상하고, 저쪽은 만료되는 것이 맞다
        /// </summary>
        private void OnRunResumed(RunResumedEvent resumed)
        {
            if (_spectateEndServerTime <= 0.0)
            {
                return;
            }

            _spectateEndServerTime += resumed.PausedSeconds;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 관전 타이머와 구조 진행을 한 틱 진행한다.
        /// PlayerController의 단일 권위 틱에서 부른다 - 자기 Update를 따로 돌면
        /// 부활 시점이 물리 스텝과 어긋나 위치 보정이 한 프레임 흔들린다
        /// </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            ServerUpdateSpectate(now);
            ServerUpdateRescue(now);
        }

        // 쓰러진 직후 관전으로 넘기고, 아무도 오지 않으면 시간이 다 됐을 때 스스로 일어난다
        [Server]
        private void ServerUpdateSpectate(double now)
        {
            CombatConfigSO combat = GameDB.Combat;

            if (_status.LifeState == PlayerLifeState.Downed)
            {
                if (_status.ServerBeginSpectate())
                {
                    _spectateEndServerTime = now + combat.SpectateDuration;

                    // 관전으로 넘어간 뒤에 보는 것은 이 전이가 죽음마다 정확히 한 번이기 때문이다 -
                    // Downed는 피해를 받은 쪽에서 만들어지므로 같은 틱에 셋이 죽으면 순서가 정해지지 않는다
                    PlayerStatus.ServerCheckPartyWipe();
                }

                return; // 전이한 틱에는 타이머를 세지 않는다
            }

            if (_status.LifeState != PlayerLifeState.Spectating)
            {
                return;
            }

            if (now < _spectateEndServerTime)
            {
                return;
            }

            ServerRevive(combat.ReviveCurrencyLossRatio);
        }

        // 구조는 매 틱 조건을 다시 본다. 시작할 때 한 번만 보면 시전자가 걸어가면서도 일으킬 수 있다
        [Server]
        private void ServerUpdateRescue(double now)
        {
            if (_rescueTargetNetId == NO_TARGET)
            {
                return;
            }

            if (!TryGetRescueTarget(_rescueTargetNetId, out ReviveController target))
            {
                ServerCancelRescue();
                return;
            }

            if (!ServerCanContinueRescue(now, target))
            {
                ServerCancelRescue();
                return;
            }

            CombatConfigSO combat   = GameDB.Combat;
            float          duration = Mathf.Max(combat.RescueDuration, Constants.Combat.DAMAGE_EPSILON);

            _rescueProgress += (Time.fixedDeltaTime / duration);

            if (_rescueProgress < 1f)
            {
                return;
            }

            // 구조는 자동보다 손실이 절반이다. 그 차이가 "구하러 갈 가치"를 수치로 만든다
            target.ServerRevive(combat.RescueCurrencyLossRatio);
            ServerCancelRescue();
        }

        /// <summary>
        /// 부활을 확정한다. 상태는 PlayerStatus가, 위치 보정은 여기가 소유한다
        /// </summary>
        [Server]
        private void ServerRevive(float currencyLossRatio)
        {
            CombatConfigSO combat = GameDB.Combat;

            Vector3 position = ResolveRevivePosition(combat);

            // 상태보다 위치를 먼저 옮긴다. 순서를 바꾸면 무적이 시작된 자리와 실제로 선 자리가
            // 한 틱 다르고, 그 틱에 결계 밖 피해를 한 번 더 받는다
            if (_movement != null)
            {
                _movement.ServerTeleport(position);
            }

            _status.ServerRevive(combat.ReviveHealthRatio, combat.ReviveInvincibleDuration);

            _spectateEndServerTime = 0.0;

            // TODO(민성): 팀 공용 노잣돈이 아직 없다. 재화 시스템이 생기면 여기서 차감한다
            // (자동 20% / 구조 10%). 지금은 값만 받아 두고 쓰지 않는다
            _ = currencyLossRatio;
        }

        /// <summary>
        /// 부활 지점. 사망 지점이 결계 밖일 때만 중심 방향으로 당긴다.
        /// 보정이 없으면 3차 결계에서 무적 2초가 끝나자마자 다시 맞기 시작하고,
        /// 죽을 때마다 노잣돈이 또 빠져 억제 장치가 선형이 아니라 복리로 작동한다
        /// </summary>
        [Server]
        private Vector3 ResolveRevivePosition(CombatConfigSO combat)
        {
            Vector3      position = transform.position;
            IBarrierState barrier = BarrierStateLocator.Current;

            if ((barrier == null) || (!barrier.IsActive) || (!barrier.IsOutside(position)))
            {
                return position;
            }

            Vector3 center = barrier.Center;
            Vector3 delta  = position - center;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0f)
            {
                return position; // 중심과 XZ가 같은데 밖으로 판정되면 옮길 방향이 없다
            }

            Vector3 inset = center + (delta.normalized * (barrier.Radius * combat.ReviveBarrierInsetRatio));
            inset.y = position.y; // 높이는 그대로 둔다. 지형은 물리가 맞춘다

            return inset;
        }

        [Server]
        private void ServerCancelRescue()
        {
            _rescueTargetNetId = NO_TARGET;
            _rescueProgress    = 0f;
        }

        // 매 틱 다시 보는 조건들. 하나라도 어긋나면 진행도를 0으로 되돌린다
        [Server]
        private bool ServerCanContinueRescue(double now, ReviveController target)
        {
            if (target.GetComponent<PlayerStatus>().LifeState != PlayerLifeState.Spectating)
            {
                return false; // 이미 일어났거나 아직 Downed 전이 중이다
            }

            if ((_status == null) || (_status.IsDead))
            {
                return false; // 일으키던 사람이 죽었다
            }

            CombatConfigSO combat = GameDB.Combat;
            Vector3        delta  = target.transform.position - transform.position;

            if (delta.sqrMagnitude > (combat.RescueRadius * combat.RescueRadius))
            {
                return false;
            }

            // 취소 조건은 시전자의 이동, 공격, 구르기다. 피해를 받는 것으로는 중단되지 않는다 -
            // 전투 한복판에서 일으키는 것이 이 기믹의 전부라 맞는다고 끊기면 성립하지 않는다
            if ((_roll != null) && (_roll.IsRolling))
            {
                return false;
            }

            if ((_attack != null) && (_attack.IsActionInProgress(now)))
            {
                return false;
            }

            if ((_parry != null) && (_parry.IsActionInProgress(now)))
            {
                return false;
            }

            return !IsMovingOnServer();
        }

        // 서버가 속도를 직접 넣으므로 입력을 따로 받을 필요가 없다.
        // 클라이언트가 보내는 "안 움직였다"를 믿으면 걸어가면서 일으킬 수 있다
        [Server]
        private bool IsMovingOnServer()
        {
            if (_rigidbody == null)
            {
                return false;
            }

            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = 0f;

            return (velocity.sqrMagnitude > (RESCUE_MOVE_EPSILON * RESCUE_MOVE_EPSILON));
        }

        private static bool TryGetRescueTarget(uint netId, out ReviveController target)
        {
            target = null;

            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
            {
                return false;
            }

            target = identity.GetComponent<ReviveController>();
            return (target != null);
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdBeginRescue(uint targetNetId)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            if (targetNetId == netId)
            {
                return; // 스스로를 일으킬 수는 없다
            }

            if (!TryGetRescueTarget(targetNetId, out ReviveController target))
            {
                return;
            }

            // 시작 조건은 유지 조건과 같은 것을 쓴다. 시작만 느슨하면 사거리 밖에서 걸어놓고
            // 다가가는 동안 진행도가 쌓인다
            _rescueTargetNetId = targetNetId;
            _rescueProgress    = 0f;

            if (!ServerCanContinueRescue(NetworkTime.time, target))
            {
                ServerCancelRescue();
            }
        }

        [Command]
        private void CmdCancelRescue()
        {
            ServerCancelRescue();
        }

        /****************************************
        *                Input
        ****************************************/

        private void HandleRescueInput()
        {
            bool isHeld = Input.GetKey(Constants.Player.KEY_INTERACT);

            if ((isHeld) && (!_isHoldingRescue))
            {
                _isHoldingRescue = true;

                if (TryFindNearestDownedAlly(out uint targetNetId))
                {
                    CmdBeginRescue(targetNetId);
                }

                return;
            }

            if ((!isHeld) && (_isHoldingRescue))
            {
                _isHoldingRescue = false;

                if (_rescueTargetNetId != NO_TARGET)
                {
                    CmdCancelRescue();
                }
            }
        }

        /// <summary>
        /// 사거리 안에서 가장 가까운 쓰러진 팀원. 로컬 탐색이고 서버가 다시 검증한다.
        /// 여기서 고르는 것은 "누구를"이지 "되는가"가 아니다
        /// </summary>
        private bool TryFindNearestDownedAlly(out uint targetNetId)
        {
            targetNetId = NO_TARGET;

            if (!GameDB.IsInitialized)
            {
                return false;
            }

            float radius = GameDB.Combat.RescueRadius;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, _rescueBuffer, _playerLayerMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                Collider candidate = _rescueBuffer[i];
                if (candidate == null)
                {
                    continue;
                }

                Transform root = candidate.transform.root;
                if (root == transform)
                {
                    continue;
                }

                PlayerStatus status = root.GetComponent<PlayerStatus>();
                if ((status == null) || (status.LifeState != PlayerLifeState.Spectating))
                {
                    continue;
                }

                NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
                if (identity == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, root.position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                targetNetId  = identity.netId;
            }

            return (targetNetId != NO_TARGET);
        }
    }
}
