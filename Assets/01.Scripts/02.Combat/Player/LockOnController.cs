// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.Combat
{
    /// <summary> 락온 대상의 선택, 유지, 해제를 소유한다 </summary>
    public class LockOnController : NetworkBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private static readonly string[] G_TargetLayers = { Constants.Layer.ENEMY, Constants.Layer.BOSS };

        private const int CANDIDATE_BUFFER_SIZE = 64;

        private const float REVALIDATE_INTERVAL = 0.1f; // 초

        private const uint NO_TARGET = 0u;

        private const float WHEEL_THRESHOLD = 0.01f;

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnTargetChanged))]     private uint _serverTargetNetId = NO_TARGET;
        [SyncVar(hook = nameof(OnTargetPartChanged))] private byte _serverTargetPart  = 0;

        private PlayerStatus _status;
        private PlayerPresentation _presentation;

        private Transform        _targetTransform = null;
        private IAimAssistTarget _targetAim       = null;
        private Collider         _targetCollider  = null;
        private bool             _isTargetBoss    = false;
        private bool             _isTargetPart    = false;
        private ITargetPartIdSource _serverTargetPartSource = null;

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
        public byte TargetPartId => _serverTargetPart;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status       = GetComponent<PlayerStatus>();
            _presentation = GetComponent<PlayerPresentation>();

            _targetLayerMask = LayerMask.GetMask(G_TargetLayers);
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

            Candidate next = FindBestCandidate(false);
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

            // 서버가 확정한 SyncVar라 원격 화면에서도 같은 값이 찍힌다
            if (newNetId == 0u)
            {
                GDebug.Log($"{GDebug.Who(netId)} 락온 해제 ({GDebug.Who(oldNetId)})");
                return;
            }

            GDebug.Log($"{GDebug.Who(netId)} -> {GDebug.Who(newNetId)} 락온");
        }

        private void OnTargetPartChanged(byte oldPartId, byte newPartId)
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

            if (!IsServerPartTargetable())
            {
                ServerClearTarget();
                return;
            }

            if ((_status != null) && (_status.IsDead || _status.IsSwallowed))
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

            if (!IsServerPartTargetable())
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

            if ((_status != null) && (_status.IsDead || _status.IsSwallowed))
            {
                GDebug.Log(GLogKind.Server, $"락온 거부: 요청자 {GDebug.Who(netId)} 사망 상태");
                return;
            }

            if (!TryFindServerTarget(targetNetId, out Transform root))
            {
                GDebug.Log(GLogKind.Server, $"락온 거부: 서버가 대상 {targetNetId}를 못 찾음");
                return;
            }

            if ((DamageTargetResolver.TryResolve(root, out IDamageable damageable)) && (damageable.IsDead))
            {
                GDebug.Log(GLogKind.Server, $"락온 거부: 대상 {GDebug.Who(targetNetId)} 이미 사망");
                return;
            }

            ITargetPartIdSource partSource = null;
            if (partIndex != TargetRef.BODY_PART_ID)
            {
                if ((!AimAssistTargetResolver.TryResolve(root, partIndex, out IAimAssistTarget partAim)) ||
                    (!(partAim is ITargetPartIdSource resolvedPart)) || (!resolvedPart.IsTargetable))
                {
                    GDebug.Log(GLogKind.Server,
                        $"락온 거부: {GDebug.Who(targetNetId)} fixed PartId {partIndex} 미존재");
                    return;
                }

                partSource = resolvedPart;
            }

            float range = (GameDB.IsInitialized) ? GameDB.Combat.LockOnAcquireRange : 0f;
            float sqrDistance = (root.position - transform.position).sqrMagnitude;
            if (sqrDistance > (range * range))
            {
                GDebug.Log(GLogKind.Server,
                    $"락온 거부: 거리 {Mathf.Sqrt(sqrDistance):F1}m > 허용 {range:F0}m" +
                    ((range <= 0f) ? " (GameDB 미초기화)" : ""));
                return;
            }

            _serverTargetNetId = targetNetId;
            _serverTargetPart  = partIndex;
            _serverTargetPartSource = partSource;
            GDebug.Log(GLogKind.Server, $"락온 확정: {GDebug.Who(netId)} -> {GDebug.Who(targetNetId)} 부위{partIndex}");
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

                RequestLockOn(FindBestCandidate(false));
                return;
            }

            if (_serverTargetNetId == NO_TARGET)
            {
                return;
            }

            float wheel = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_WHEEL);
            if (Mathf.Abs(wheel) > WHEEL_THRESHOLD)
            {
                Candidate next = FindBestCandidate(true);
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
                    Candidate next = FindBestCandidate(true);
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
        private Candidate FindBestCandidate(bool excludeCurrent)
        {
            Candidate best = default;

            if (!GameDB.IsInitialized)
            {
                GDebug.Warn("락온 실패: GameDB 미초기화");
                return best;
            }

            CombatConfigSO combat   = GameDB.Combat;
            float          range    = combat.LockOnAcquireRange;
            float          maxAngle = combat.LockOnAcquireAngle;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, range, _candidateBuffer, _targetLayerMask, QueryTriggerInteraction.Ignore);

            // 탈락 사유별 집계. 후보마다 로그를 찍으면 한 번 누를 때 화면이 넘친다
            int rejectedNoIdentity = 0;
            int rejectedDead       = 0;
            int rejectedNoCamera   = 0;
            int rejectedAngle      = 0;
            int rejectedOccluded   = 0;
            float closestAngle     = float.MaxValue;

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
                if (root == transform)
                {
                    continue;
                }

                NetworkIdentity identity = candidate.GetComponentInParent<NetworkIdentity>();
                if (identity == null)
                {
                    ++rejectedNoIdentity;
                    continue;
                }

                if ((DamageTargetResolver.TryResolve(candidate, out IDamageable damageable)) && (damageable.IsDead))
                {
                    ++rejectedDead;
                    continue;
                }

                Vector3 aimPoint = ResolveAimPoint(candidate, out byte partIndex);
                if (excludeCurrent && (identity.netId == _serverTargetNetId) &&
                    (partIndex == _serverTargetPart))
                {
                    continue;
                }

                if (!TryGetScreenAngle(aimPoint, out float angle))
                {
                    ++rejectedNoCamera;
                    continue;
                }

                if (angle < closestAngle)
                {
                    closestAngle = angle;
                }

                if (angle > maxAngle)
                {
                    ++rejectedAngle;
                    continue;
                }

                if (IsOccluded(aimPoint))
                {
                    ++rejectedOccluded;
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

            ReportCandidateSearch(best, count, range, maxAngle, closestAngle,
                                  rejectedNoIdentity, rejectedDead, rejectedNoCamera, rejectedAngle, rejectedOccluded);

            return best;
        }

        /// <summary>
        /// 후보 탐색 결과를 한 줄로 남긴다. 실패했을 때 어디서 걸렸는지 남기지 않으면
        /// 3인 테스트에서 "중클릭이 안 먹는다"는 증상만 남고 원인을 좁힐 수 없다
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("GEUMEUM_DEVTOOLS")]
        private void ReportCandidateSearch(Candidate best, int overlapCount, float range, float maxAngle,
                                           float closestAngle, int noIdentity, int dead, int noCamera,
                                           int overAngle, int occluded)
        {
            if (best.IsValid)
            {
                GDebug.Success($"락온 획득: {GDebug.Who(best.NetId)} 부위{best.PartIndex}");
                return;
            }

            if (overlapCount == 0)
            {
                GDebug.Fail($"락온 실패: 반경 {range:F0}m 안에 대상 레이어가 없다 (mask={_targetLayerMask})");
                return;
            }

            if (noCamera > 0)
            {
                GDebug.Fail($"락온 실패: Camera.main이 null이라 화면 각도를 못 잰다 ({noCamera}개)");
                return;
            }

            if (overAngle > 0)
            {
                GDebug.Fail($"락온 실패: 후보 {overlapCount}개가 전부 화면 밖 (최소 {closestAngle:F0}도 > 허용 {maxAngle:F0}도)");
                return;
            }

            if (occluded > 0)
            {
                GDebug.Fail($"락온 실패: 후보 {occluded}개가 전부 가려짐");
                return;
            }

            GDebug.Fail($"락온 실패: 후보 {overlapCount}개, NetworkIdentity없음 {noIdentity} · 사망 {dead}");
        }

        /// <summary> 콜라이더에서 조준점과 고정 PartId를 함께 구한다 </summary>
        private static Vector3 ResolveAimPoint(Collider hit, out byte partIndex)
        {
            partIndex = TargetRef.BODY_PART_ID;

            ITargetPartIdSource directPart = hit.GetComponent<ITargetPartIdSource>();
            if ((directPart != null) && directPart.IsTargetable)
            {
                partIndex = directPart.TargetPartId;
                if (directPart is IAimAssistTarget directAim)
                {
                    return directAim.AimPoint;
                }
            }

            IAimAssistTarget aim = hit.GetComponentInParent<IAimAssistTarget>();
            if ((aim is ITargetPartIdSource inheritedPart) && inheritedPart.IsTargetable)
            {
                partIndex = inheritedPart.TargetPartId;
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

            if (!AimAssistTargetResolver.TryResolve(identity.transform, _serverTargetPart,
                out _targetAim) && (_serverTargetPart != TargetRef.BODY_PART_ID))
            {
                return;
            }

            Component aimComponent = _targetAim as Component;

            _targetTransform = (aimComponent != null) ? aimComponent.transform : identity.transform;
            _targetCollider  = _targetTransform.GetComponentInChildren<Collider>();
            _isTargetBoss    = (identity.gameObject.layer == LayerMask.NameToLayer(Constants.Layer.BOSS));
            _isTargetPart    = (_serverTargetPart != TargetRef.BODY_PART_ID);

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
            _serverTargetPartSource = null;
        }

        [Server]
        private bool IsServerPartTargetable()
        {
            if (_serverTargetPart == TargetRef.BODY_PART_ID)
            {
                return true;
            }

            Component component = _serverTargetPartSource as Component;
            return (component != null) && _serverTargetPartSource.IsTargetable;
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
            if (!LocalGameplayInputGate.AllowsGameplayInput)
            {
                return false;
            }

            if ((_status != null) && (_status.IsDead || _status.IsSwallowed))
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
