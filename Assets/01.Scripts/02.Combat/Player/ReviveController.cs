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
    /// <summary> 사망 후 관전과 부활을 소유한다. 자동 부활과 협동 부활 둘 다 여기가 확정한다 </summary>
    public class ReviveController : NetworkBehaviour, IRunStateCarrier
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint NO_TARGET = 0u;

        private const int RESCUE_BUFFER_SIZE = 8;

        private const float RESCUE_MOVE_EPSILON = 0.15f; // m/s

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private double _spectateEndServerTime = 0.0;

        [SyncVar(hook = nameof(OnRescueTargetChanged))] private uint  _rescueTargetNetId = NO_TARGET;
        [SyncVar]                                      private float _rescueProgress    = 0f;

        private PlayerStatus         _status;
        private PlayerController     _movement;
        private PlayerRollController _roll;
        private PlayerAttack         _attack;
        private ParryController      _parry;
        private Rigidbody            _rigidbody;
        private CapsuleCollider      _capsule;

        private int _playerLayerMask = 0;

        private readonly Collider[] _rescueBuffer = new Collider[RESCUE_BUFFER_SIZE];

        private bool _isHoldingRescue = false;

        private uint _holdGeneration = 0u; // 소유 클라이언트 전용
        private uint _serverHoldGeneration = 0u; // 서버 전용

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 누군가를 일으키기 시작했거나 그만뒀다 </summary>
        public event Action<bool> RescuingChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 관전이 끝나는 서버 시각 </summary>
        public double SpectateEndServerTime => _spectateEndServerTime;

        /// <summary> 관전이 끝나기까지 남은 초. 쓰러진 상태가 아니면 0이다 </summary>
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
            _capsule   = GetComponent<CapsuleCollider>();

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

        /// <summary> 런이 얼어 있던 만큼 관전 종료 시각을 뒤로 민다 </summary>
        private void OnRunResumed(RunResumedEvent resumed)
        {
            if (_spectateEndServerTime <= 0.0)
            {
                return;
            }

            _spectateEndServerTime += resumed.PausedSeconds;
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public int CarrierOrder => 40;

        /// <summary> 관전 남은 시간을 담는다 </summary>
        public void Capture(RunParticipantSnapshot snapshot)
        {
            snapshot.SpectateRemainingSeconds = SpectateRemainingSeconds;
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if (snapshot.SpectateRemainingSeconds < 0.0)
            {
                failure = "관전 남은 시간이 음수다";
                return false;
            }

            failure = null;
            return true;
        }

        public bool Restore(RunParticipantSnapshot snapshot)
        {
            if (snapshot.SpectateRemainingSeconds <= 0.0)
            {
                _spectateEndServerTime = 0.0;
                return true;
            }

            _spectateEndServerTime = (NetworkTime.time + snapshot.SpectateRemainingSeconds);
            return true;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 관전 타이머와 구조 진행을 한 틱 진행한다 </summary>
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

        [Server]
        private void ServerUpdateSpectate(double now)
        {
            CombatConfigSO combat = GameDB.Combat;

            if (_status.LifeState == PlayerLifeState.Downed)
            {
                if (_status.ServerBeginSpectate())
                {
                    _spectateEndServerTime = now + combat.SpectateDuration;

                    PlayerStatus.ServerCheckPartyWipe();
                }

                return;
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

            target.ServerRevive(combat.RescueCurrencyLossRatio);
            ServerCancelRescue();
        }

        /// <summary> 부활을 확정한다 </summary>
        [Server]
        private void ServerRevive(float currencyLossRatio)
        {
            CombatConfigSO combat = GameDB.Combat;

            Vector3 position = ResolveRevivePosition(combat);

            if (_movement != null)
            {
                _movement.ServerTeleport(position);
            }

            _status.ServerRevive(combat.ReviveHealthRatio, combat.ReviveInvincibleDuration);

            _spectateEndServerTime = 0.0;

            // TODO(민성): 재화 시스템이 생기면 여기서 차감한다
            _ = currencyLossRatio;
        }

        /// <summary> 부활 지점. 사망 지점이 결계 밖일 때만 중심 방향으로 당긴다 </summary>
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
                return position;
            }

            Vector3 inset = center + (delta.normalized * (barrier.Radius * combat.ReviveBarrierInsetRatio));
            inset.y = position.y;

            return ResolveGroundedPosition(inset, position);
        }

        /// <summary> 후보 지점을 지면에 앉힌다. 찾지 못하면 원래 자리를 쓴다 </summary>
        [Server]
        private Vector3 ResolveGroundedPosition(Vector3 candidate, Vector3 fallback)
        {
            Vector3 probeOrigin = candidate + (Vector3.up * Constants.Player.REVIVE_PROBE_UP);
            float   probeLength = (Constants.Player.REVIVE_PROBE_UP + Constants.Player.REVIVE_PROBE_DISTANCE);
            int     groundMask  = LayerMask.GetMask(Constants.Layer.GROUND, Constants.Layer.DEFAULT);

            if (!Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, probeLength, groundMask, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"[ReviveController] 부활 지점 아래에서 지면을 찾지 못해 사망 지점을 쓴다: {candidate}", this);
                return fallback;
            }

            Vector3 grounded = hit.point + (Vector3.up * Constants.Player.REVIVE_GROUND_OFFSET);

            if (IsBlocked(grounded))
            {
                Debug.LogWarning($"[ReviveController] 부활 지점이 막혀 있어 사망 지점을 쓴다: {grounded}", this);
                return fallback;
            }

            return grounded;
        }

        [Server]
        private bool IsBlocked(Vector3 groundPosition)
        {
            if (_capsule == null)
            {
                return false;
            }

            float radius = _capsule.radius;
            float height = Mathf.Max(_capsule.height, (radius * 2f));

            Vector3 bottom = groundPosition + (Vector3.up * radius);
            Vector3 top    = groundPosition + (Vector3.up * (height - radius));

            int blockingMask = LayerMask.GetMask(Constants.Layer.GROUND, Constants.Layer.DEFAULT);

            return Physics.CheckCapsule(bottom, top, radius, blockingMask, QueryTriggerInteraction.Ignore);
        }

        [Server]
        private void ServerCancelRescue()
        {
            _rescueTargetNetId = NO_TARGET;
            _rescueProgress    = 0f;
        }

        [Server]
        private bool ServerCanContinueRescue(double now, ReviveController target)
        {
            if (target.GetComponent<PlayerStatus>().LifeState != PlayerLifeState.Spectating)
            {
                return false;
            }

            if ((_status == null) || (_status.IsDead))
            {
                return false;
            }

            CombatConfigSO combat = GameDB.Combat;
            Vector3        delta  = target.transform.position - transform.position;

            if (delta.sqrMagnitude > (combat.RescueRadius * combat.RescueRadius))
            {
                return false;
            }

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
        private void CmdBeginRescue(uint targetNetId, uint holdGeneration)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            if (targetNetId == netId)
            {
                return;
            }

            if (holdGeneration <= _serverHoldGeneration)
            {
                return;
            }

            _serverHoldGeneration = holdGeneration;

            if (!TryGetRescueTarget(targetNetId, out ReviveController target))
            {
                return;
            }

            _rescueTargetNetId = targetNetId;
            _rescueProgress    = 0f;

            if (!ServerCanContinueRescue(NetworkTime.time, target))
            {
                ServerCancelRescue();
            }
        }

        /// <summary> 홀드를 놓는다 </summary>
        [Command]
        private void CmdCancelRescue(uint holdGeneration)
        {
            if (holdGeneration < _serverHoldGeneration)
            {
                return;
            }

            _serverHoldGeneration = holdGeneration;

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
                ++_holdGeneration;

                if (TryFindNearestDownedAlly(out uint targetNetId))
                {
                    CmdBeginRescue(targetNetId, _holdGeneration);
                }

                return;
            }

            if ((!isHeld) && (_isHoldingRescue))
            {
                _isHoldingRescue = false;

                CmdCancelRescue(_holdGeneration);
            }
        }

        /// <summary> 사거리 안에서 가장 가까운 쓰러진 팀원을 찾는다 </summary>
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
