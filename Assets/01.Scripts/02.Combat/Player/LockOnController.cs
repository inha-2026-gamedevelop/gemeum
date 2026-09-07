// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> 락온 대상의 선택, 유지, 해제를 소유한다 </summary>
    public class LockOnController : NetworkBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private static readonly string[] TARGET_LAYERS = { Constants.Layer.ENEMY, Constants.Layer.BOSS };

        private const int CANDIDATE_BUFFER_SIZE = 64;

        private const float REVALIDATE_INTERVAL = 0.1f; // 초

        private const uint NO_TARGET = 0u;

        private const float WHEEL_THRESHOLD = 0.01f;

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnTargetChanged))] private uint _serverTargetNetId = NO_TARGET;
        [SyncVar]                                 private byte _serverTargetPart  = 0;

        private PlayerStatus _status;
        private PlayerPresentation _presentation;

        private Transform        _targetTransform = null;
        private IAimAssistTarget _targetAim       = null;
        private Collider         _targetCollider  = null;
        private bool             _isTargetBoss    = false;
        private bool             _isTargetPart    = false;

        // 소유 클라이언트 전용
        private readonly Collider[] _candidateBuffer = new Collider[CANDIDATE_BUFFER_SIZE];

        private int   _targetLayerMask   = 0;
        private float _revalidateTimer   = 0f;
        private float _blockedElapsed    = 0f;
        private float _switchCooldown    = 0f;

        private bool _wasLocked     = false;
        private bool _manualRelease = false;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 락온을 걸거나 풀었다 </summary>
        public event System.Action<bool> LockOnChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정한 락온 대상이 이 화면에서 실제로 해석됐는지 </summary>
        public bool IsLockedOn => (_targetTransform != null);

        /// <summary> 겨누는 월드 좌표 </summary>
        public Vector3 TargetAimPoint
        {
            get
            {
                if (_targetAim != null)
                {
                    return _targetAim.AimPoint;
                }

                if (_targetCollider != null)
                {
                    return _targetCollider.bounds.center;
                }

                return (_targetTransform != null) ? _targetTransform.position : transform.position;
            }
        }

        /// <summary> 서버가 확정한 대상의 netId. 화살 유도가 이 값을 함께 보낸다 </summary>
        public uint TargetNetId => _serverTargetNetId;

        /// <summary> 대상의 부위 번호 </summary>
        public byte TargetPartIndex => _serverTargetPart;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status       = GetComponent<PlayerStatus>();
            _presentation = GetComponent<PlayerPresentation>();

            _targetLayerMask = LayerMask.GetMask(TARGET_LAYERS);
        }

        private void Update()
        {
            ResolveTargetIfNeeded();
            ApplyPresentation();

            if (!isOwned)
            {
                return;
            }

            ApplyCameraFrame();

            if (!CanControl())
            {
                ReleaseIfLocked();
                return;
            }

            HandleInput();
            ReacquireIfTargetGone();
            RevalidateOnInterval();
        }

        /// <summary> 서버가 대상을 끊었을 때 다음 대상으로 넘어간다 </summary>
        private void ReacquireIfTargetGone()
        {
            if (_serverTargetNetId != NO_TARGET)
            {
                _wasLocked = true;
                return;
            }

            if (!_wasLocked)
            {
                return;
            }

            _wasLocked = false;

            if (_manualRelease)
            {
                return;
            }

            Candidate next = FindBestCandidate(null);
            if (next.IsValid)
            {
                RequestLockOn(next);
            }
        }

        private void OnDisable()
        {
            if (isOwned)
            {
                CameraFrame.SetLockOn(false, Vector3.zero, 0f);
            }
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnTargetChanged(uint oldNetId, uint newNetId)
        {
            ClearResolvedTarget();
            ResolveTargetIfNeeded();
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 대상이 사라졌거나 죽었으면 서버가 락온을 끊는다 </summary>
        [Server]
        public void ServerUpdateState()
        {
            if (_serverTargetNetId == NO_TARGET)
            {
                return;
            }

            if (!TryFindServerTarget(_serverTargetNetId, out Transform root))
            {
                ServerClearTarget();
                return;
            }

            if ((DamageTargetResolver.TryResolve(root, out IDamageable damageable)) && (damageable.IsDead))
            {
                ServerClearTarget();
                return;
            }

            if ((_status != null) && (_status.IsDead))
            {
                ServerClearTarget();
            }
        }

        /// <summary> 대상이 아직 유효한지 서버가 다시 본다 </summary>
        [Server]
        public bool ServerIsTargetValid(float maxRange)
        {
            if (_serverTargetNetId == NO_TARGET)
            {
                return false;
            }

            if (!TryFindServerTarget(_serverTargetNetId, out Transform root))
            {
                return false;
            }

            if ((DamageTargetResolver.TryResolve(root, out IDamageable damageable)) && (damageable.IsDead))
            {
                return false;
            }

            return ((root.position - transform.position).sqrMagnitude <= (maxRange * maxRange));
        }

        /****************************************
        *              Commands
        ****************************************/

        /// <summary> 락온을 걸거나 푼다 </summary>
        [Command]
        private void CmdSetLockOn(uint targetNetId, byte partIndex)
        {
            if (targetNetId == NO_TARGET)
            {
                ServerClearTarget();
                return;
            }

            if ((_status != null) && (_status.IsDead))
            {
                return;
            }

            if (!TryFindServerTarget(targetNetId, out Transform root))
            {
                return;
            }

            if ((DamageTargetResolver.TryResolve(root, out IDamageable damageable)) && (damageable.IsDead))
            {
                return;
            }

            float range = (GameDB.IsInitialized) ? GameDB.Combat.LockOnAcquireRange : 0f;
            if ((root.position - transform.position).sqrMagnitude > (range * range))
            {
                return;
            }

            _serverTargetNetId = targetNetId;
            _serverTargetPart  = partIndex;
        }

        /****************************************
        *                Input
        ****************************************/

        private void HandleInput()
        {
            if (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_LOCK_ON))
            {
                if (_serverTargetNetId != NO_TARGET)
                {
                    RequestLockOn(null);
                    return;
                }

                RequestLockOn(FindBestCandidate(null));
                return;
            }

            if (_serverTargetNetId == NO_TARGET)
            {
                return;
            }

            float wheel = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_WHEEL);
            if (Mathf.Abs(wheel) > WHEEL_THRESHOLD)
            {
                Candidate next = FindBestCandidate(_targetTransform);
                if (next.IsValid)
                {
                    RequestLockOn(next);
                }
            }
        }

        private void RequestLockOn(Candidate? candidate)
        {
            uint netId = NO_TARGET;
            byte part  = 0;

            if ((candidate.HasValue) && (candidate.Value.IsValid))
            {
                netId = candidate.Value.NetId;
                part  = candidate.Value.PartIndex;
            }

            if ((netId == _serverTargetNetId) && (part == _serverTargetPart))
            {
                return;
            }

            _manualRelease  = (netId == NO_TARGET);
            _blockedElapsed = 0f;
            _switchCooldown = (GameDB.IsInitialized) ? GameDB.Combat.LockOnSwitchCooldown : 0f;

            CmdSetLockOn(netId, part);
        }

        private void ReleaseIfLocked()
        {
            if (_serverTargetNetId == NO_TARGET)
            {
                return;
            }

            RequestLockOn(null);
        }

        /****************************************
        *              Maintenance
        ****************************************/

        /// <summary> 유지 조건(30m, 55도, 가림)을 10Hz로 확인한다 </summary>
        private void RevalidateOnInterval()
        {
            _switchCooldown = Mathf.Max(0f, _switchCooldown - Time.deltaTime);

            _revalidateTimer += Time.deltaTime;
            if (_revalidateTimer < REVALIDATE_INTERVAL)
            {
                return;
            }

            float elapsed = _revalidateTimer;
            _revalidateTimer = 0f;

            if ((_serverTargetNetId == NO_TARGET) || (!GameDB.IsInitialized))
            {
                return;
            }

            if (_targetTransform == null)
            {
                return;
            }

            CombatConfigSO combat   = GameDB.Combat;
            Vector3        aimPoint = TargetAimPoint;

            if ((aimPoint - transform.position).sqrMagnitude > (combat.LockOnHoldRange * combat.LockOnHoldRange))
            {
                ReleaseIfLocked();
                return;
            }

            if (TryGetScreenAngle(aimPoint, out float angle))
            {
                if (angle > combat.LockOnHoldAngle)
                {
                    ReleaseIfLocked();
                    return;
                }
            }

            if (IsOccluded(aimPoint))
            {
                _blockedElapsed += elapsed;

                if ((_blockedElapsed >= combat.LockOnBlockedGrace) && (_switchCooldown <= 0f))
                {
                    Candidate next = FindBestCandidate(_targetTransform);
                    RequestLockOn(next.IsValid ? next : (Candidate?)null);
                }

                return;
            }

            _blockedElapsed = 0f;
        }

        /****************************************
        *              Candidate Search
        ****************************************/

        private struct Candidate
        {
            public uint NetId;
            public byte PartIndex;
            public bool IsValid;
        }

        /// <summary> 화면 중심 각도가 작은 순, 같으면 가까운 순으로 대상을 고른다 </summary>
        private Candidate FindBestCandidate(Transform exclude)
        {
            Candidate best = default;

            if (!GameDB.IsInitialized)
            {
                return best;
            }

            CombatConfigSO combat   = GameDB.Combat;
            float          range    = combat.LockOnAcquireRange;
            float          maxAngle = combat.LockOnAcquireAngle;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, range, _candidateBuffer, _targetLayerMask, QueryTriggerInteraction.Ignore);

            float bestAngle    = float.MaxValue;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                Collider candidate = _candidateBuffer[i];
                if (candidate == null)
                {
                    continue;
                }

                Transform root = candidate.transform.root;
                if ((root == exclude) || (root == transform))
                {
                    continue;
                }

                NetworkIdentity identity = candidate.GetComponentInParent<NetworkIdentity>();
                if (identity == null)
                {
                    continue;
                }

                if ((DamageTargetResolver.TryResolve(candidate, out IDamageable damageable)) && (damageable.IsDead))
                {
                    continue;
                }

                Vector3 aimPoint = ResolveAimPoint(candidate, out byte partIndex);

                if (!TryGetScreenAngle(aimPoint, out float angle))
                {
                    continue;
                }

                if (angle > maxAngle)
                {
                    continue;
                }

                if (IsOccluded(aimPoint))
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, aimPoint);

                bool isBetter = (angle < bestAngle) || ((Mathf.Approximately(angle, bestAngle)) && (distance < bestDistance));
                if (!isBetter)
                {
                    continue;
                }

                bestAngle    = angle;
                bestDistance = distance;
                best = new Candidate
                {
                    NetId     = identity.netId,
                    PartIndex = partIndex,
                    IsValid   = true,
                };
            }

            return best;
        }

        /// <summary> 콜라이더에서 조준점과 부위 번호를 함께 구한다 </summary>
        private static Vector3 ResolveAimPoint(Collider hit, out byte partIndex)
        {
            partIndex = 0;

            NetworkIdentity identity = hit.GetComponentInParent<NetworkIdentity>();
            IAimAssistTarget aim     = hit.GetComponentInParent<IAimAssistTarget>();

            if ((identity != null) && (aim != null))
            {
                IAimAssistTarget[] parts = identity.GetComponentsInChildren<IAimAssistTarget>(true);

                for (int i = 0; i < parts.Length; ++i)
                {
                    if (!ReferenceEquals(parts[i], aim))
                    {
                        continue;
                    }

                    partIndex = (byte)Mathf.Min(i, byte.MaxValue);
                    break;
                }
            }

            return (aim != null) ? aim.AimPoint : hit.bounds.center;
        }

        /****************************************
        *              Resolution
        ****************************************/

        private void ResolveTargetIfNeeded()
        {
            if (_serverTargetNetId == NO_TARGET)
            {
                if (_targetTransform != null)
                {
                    ClearResolvedTarget();
                }

                return;
            }

            if (_targetTransform != null)
            {
                return;
            }

            NetworkIdentity identity = null;

            if ((isServer) && (NetworkServer.spawned.TryGetValue(_serverTargetNetId, out NetworkIdentity serverIdentity)))
            {
                identity = serverIdentity;
            }
            else if ((NetworkClient.active) && (NetworkClient.spawned.TryGetValue(_serverTargetNetId, out NetworkIdentity clientIdentity)))
            {
                identity = clientIdentity;
            }

            if (identity == null)
            {
                return;
            }

            IAimAssistTarget[] parts = identity.GetComponentsInChildren<IAimAssistTarget>(true);

            _targetAim = (_serverTargetPart < parts.Length) ? parts[_serverTargetPart] : null;

            Component aimComponent = _targetAim as Component;

            _targetTransform = (aimComponent != null) ? aimComponent.transform : identity.transform;
            _targetCollider  = _targetTransform.GetComponentInChildren<Collider>();
            _isTargetBoss    = (identity.gameObject.layer == LayerMask.NameToLayer(Constants.Layer.BOSS));
            _isTargetPart    = (_targetTransform != identity.transform);

            LockOnChanged?.Invoke(true);
        }

        private void ClearResolvedTarget()
        {
            bool wasLockedOn = (_targetTransform != null);

            _targetTransform = null;
            _targetAim       = null;
            _targetCollider  = null;
            _isTargetBoss    = false;
            _isTargetPart    = false;
            _blockedElapsed  = 0f;

            if (wasLockedOn)
            {
                LockOnChanged?.Invoke(false);
            }
        }

        [Server]
        private void ServerClearTarget()
        {
            _serverTargetNetId = NO_TARGET;
            _serverTargetPart  = 0;
        }

        private static bool TryFindServerTarget(uint netId, out Transform root)
        {
            root = null;

            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
            {
                return false;
            }

            root = identity.transform;
            return true;
        }

        /****************************************
        *              Output
        ****************************************/

        /// <summary> 모델이 바라볼 방향을 표현 계층에 넘긴다 </summary>
        private void ApplyPresentation()
        {
            if (_presentation == null)
            {
                return;
            }

            _presentation.SetLockOnTarget(_targetTransform);
        }

        private void ApplyCameraFrame()
        {
            if (!IsLockedOn)
            {
                CameraFrame.SetLockOn(false, Vector3.zero, 0f);
                return;
            }

            CameraFrame.SetLockOn(true, TargetAimPoint, ResolveCameraDistance());
        }

        /// <summary> 대상에 맞는 붐 길이 </summary>
        private float ResolveCameraDistance()
        {
            if ((!GameDB.IsInitialized) || (GameDB.Camera == null))
            {
                return 0f;
            }

            CameraConfigSO camera = GameDB.Camera;

            if (!_isTargetBoss)
            {
                return camera.LockOnDistanceEnemy;
            }

            return (_isTargetPart) ? camera.LockOnDistanceBossPart : camera.LockOnDistanceBossBody;
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool CanControl()
        {
            if ((_status != null) && (_status.IsDead))
            {
                return false;
            }

            return CameraFrame.InputEnabled;
        }

        private static bool TryGetScreenAngle(Vector3 point, out float angle)
        {
            angle = 0f;

            Camera view = Camera.main;
            if (view == null)
            {
                return false;
            }

            Vector3 toTarget = point - view.transform.position;
            if (toTarget.sqrMagnitude <= 0f)
            {
                return false;
            }

            angle = Vector3.Angle(view.transform.forward, toTarget);
            return true;
        }

        private static bool IsOccluded(Vector3 point)
        {
            Camera view = Camera.main;
            if ((view == null) || (!GameDB.IsInitialized) || (GameDB.Camera == null))
            {
                return false;
            }

            Vector3 origin = view.transform.position;
            Vector3 delta  = point - origin;

            if (delta.sqrMagnitude <= 0f)
            {
                return false;
            }

            return Physics.Raycast(
                origin,
                delta.normalized,
                delta.magnitude,
                GameDB.Camera.CollisionMask,
                QueryTriggerInteraction.Ignore);
        }
    }
}
