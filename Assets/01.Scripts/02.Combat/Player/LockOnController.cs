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
    /// 락온 대상의 선택, 유지, 해제를 소유한다.
    ///
    /// 락온은 로컬 표현이 아니다. 바라보는 방향이 동기화돼야 남의 화면에서 그 사람이 옳게 서 있고,
    /// 패링이 0.32초인 게임에서 차사가 보스를 보고 자세를 잡는 것이 안 보이면 아무도 예측할 수 없다.
    /// 그래서 대상 선택만 로컬이고 대상 자체는 서버가 검증해 SyncVar로 확정한다.
    ///
    /// 카메라에는 오브젝트가 아니라 값으로만 넘긴다(CameraFrame) -
    /// 02.Combat이 05.Art를 참조하지 않는 것과 같은 이유다
    /// </summary>
    // 대상 선택 규칙(35/55도 히스테리시스, 각도 우선, 동률 시 거리)은
    public class LockOnController : NetworkBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        // 락온 대상이 될 수 있는 것은 잡귀와 보스뿐이다. PlayerAttack의 판정 대상과 같은 층이어야
        // "잠근 것을 때린다"가 성립한다 - 다르면 잠글 수는 있는데 맞지 않는 대상이 생긴다
        private static readonly string[] TARGET_LAYERS = { Constants.Layer.ENEMY, Constants.Layer.BOSS };

        // 한 번의 탐색에서 볼 콜라이더 상한. 넘치면 가까운 것부터 잘리는 것이 아니라 물리 질의가
        // 임의 순서로 채우므로, 25m 안에 이보다 많은 콜라이더가 들어올 일이 없는 값으로 잡는다
        private const int CANDIDATE_BUFFER_SIZE = 64;

        // 유효성 재검사 주기(초). 매 프레임 전체 후보를 다시 탐색하지 않는다 -
        // 대상 추적은 매 프레임이지만 "아직 유효한가"는 10Hz로 충분하다
        private const float REVALIDATE_INTERVAL = 0.1f;

        // 대상이 없음을 뜻하는 netId. Mirror는 0을 유효한 netId로 발급하지 않는다
        private const uint NO_TARGET = 0u;

        // 휠 입력의 문턱. GetAxisRaw는 노치 하나에 0.1 안팎을 돌려준다
        private const float WHEEL_THRESHOLD = 0.01f;

        /****************************************
        *                Fields
        ****************************************/

        // 서버가 확정한 대상. 표현(모델이 보는 방향)과 판정(화살 유도)이 둘 다 이 값을 본다.
        // 부위 번호가 따로 있는 이유는 보스 꼬리 세 덩이가 몸통과 같은 NetworkIdentity라
        // netId 하나로는 "꼬리를 잠갔다"를 표현할 수 없기 때문이다
        [SyncVar(hook = nameof(OnTargetChanged))] private uint _serverTargetNetId = NO_TARGET;
        [SyncVar]                                 private byte _serverTargetPart  = 0;

        private PlayerStatus _status;
        private PlayerPresentation _presentation;

        // 해석된 대상. netId만으로는 매 프레임 사전을 뒤져야 하므로 훅에서 한 번만 푼다
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

        // 대상이 사라져서 풀린 것인지 손으로 푼 것인지 가른다. 사라진 쪽만 다음 대상으로 넘어간다
        private bool _wasLocked     = false;
        private bool _manualRelease = false;

        /****************************************
        *                Events
        ****************************************/

        // 조준점은 매 프레임 움직이므로 UI가 프로퍼티로 읽는다.
        // 이벤트는 걸고 푸는 이산 전이만 알린다
        /// <summary> 락온을 걸거나 풀었다 </summary>
        public event System.Action<bool> LockOnChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 확정한 락온 대상이 이 화면에서 실제로 해석됐는지 </summary>
        public bool IsLockedOn => (_targetTransform != null);

        /// <summary>
        /// 겨누는 월드 좌표. 계약이 없는 임시 대상은 콜라이더의 bounds.center로 폴백한다 -
        /// 폴백 규칙을 호출부마다 다르게 만들지 않기 위해 여기 하나만 둔다
        /// </summary>
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

        /// <summary>
        /// 대상의 부위 번호. 화살 유도가 netId와 함께 받아야 잠근 그 꼬리로 휜다 -
        /// netId만 넘기면 꼬리를 잠가도 화살은 몸통으로 간다
        /// </summary>
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
            // 대상 해석은 모든 인스턴스가 한다. 원격 캐릭터도 자기 화면에서 대상을 보고 서 있어야 한다
            ResolveTargetIfNeeded();
            ApplyPresentation();

            if (!isOwned)
            {
                return;
            }

            // 카메라에 밀어 넣는 것은 자기 화면뿐이다. 남의 락온이 내 카메라를 돌리면 안 된다
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

        /// <summary>
        /// 서버가 대상을 끊었을 때(사망, despawn) 다음 대상으로 넘어간다.
        /// 손으로 푼 것과 구분한다 - 중클릭으로 껐는데 다음 적이 다시 잡히면
        /// 락온을 끌 방법이 없어진다. 소울즈에서 사람들이 싫어하는 것이 정확히 그 종류다
        /// </summary>
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

            // 이쪽에서 푼 요청이 아직 서버에 안 닿은 프레임이 있으므로 플래그는 여기서 건드리지 않는다.
            // 값은 요청을 보낼 때 정해지고 다음 요청까지 그대로다
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
            // 사망, 씬 전환, 파괴로 이 컴포넌트가 멈추면 카메라가 지난 대상을 계속 본다
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

        /// <summary>
        /// 대상이 사라졌거나 죽었으면 서버가 락온을 끊는다.
        /// PlayerController의 단일 권위 틱에서 부른다 - 여기서 따로 Update를 돌지 않는다
        /// </summary>
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

            // 살아 있는 대상은 절대 안 옮긴다. 여기서 끊는 것은 "사라졌을 때"뿐이고
            // 다음 대상 선택은 클라이언트가 다시 요청한다
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

        /// <summary>
        /// 대상이 아직 유효한지 서버가 다시 본다. 화살 발사 시점의 재확인용이다 -
        /// 선딜 0.25초 동안 대상이 죽거나 멀어졌을 수 있고, 유도는 표현이 아니라 판정이라
        /// 그 사이를 그냥 넘기면 사람마다 다른 궤적이 나온다 -> 카메라와 락온 11.3
        /// </summary>
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

        /// <summary>
        /// 락온을 걸거나 푼다. 걸 때와 풀 때만 오간다 - 대상 추적은 양쪽이 각자 한다.
        /// netId 0은 해제 요청이다
        /// </summary>
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
                return; // 죽은 사람이 대상을 보고 서 있으면 안 된다
            }

            if (!TryFindServerTarget(targetNetId, out Transform root))
            {
                return; // 없는 대상을 보고 서 있는 캐릭터가 생기지 않게 한다
            }

            if ((DamageTargetResolver.TryResolve(root, out IDamageable damageable)) && (damageable.IsDead))
            {
                return;
            }

            // 맵 반대편 잡귀를 잠그는 것을 막는다. 클라이언트가 보낸 거리를 믿지 않고 서버가 잰다
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

            // 휠은 대상 전환이다. 인게임 무기 교체가 없어 휠이 비어 있다
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

        // 걸 때, 풀 때, 대상이 바뀔 때만 보낸다. 같은 대상을 다시 보내지 않는다
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

            // 이쪽에서 푼 것인지 대상이 사라진 것인지를 여기서 정한다.
            // 유지 조건 이탈, 가림, 사망으로 인한 UI 해제도 전부 "이쪽에서 푼 것"이다 -
            // 그래야 다음 적이 저절로 잡히지 않는다
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

        /// <summary>
        /// 유지 조건(30m, 55도, 가림)을 10Hz로 확인한다.
        /// 획득보다 넓은 조건을 쓰는 이유는 같으면 경계에서 붙었다 떨어졌다 하기 때문이다
        /// </summary>
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

            // 서버가 이미 끊었는데 이쪽 해석만 남은 경우다. 다음 동기화가 오면 정리된다
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

            // 가림은 즉시 풀지 않는다. 기둥 모서리에서 마커가 깜빡이는 것을 완화하는 유예이고,
            // 사격 관통을 허용하는 것이 아니다 - 화살 판정은 서버가 따로 한다
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

        /// <summary>
        /// 화면 중심 각도가 작은 순, 같으면 가까운 순으로 고른다.
        /// 가중 점수를 쓰지 않는 이유는 예측 가능해야 하기 때문이다 -
        /// 각도와 거리를 섞으면 왜 저것이 잡혔는지 사람이 설명할 수 없다.
        /// 탐색은 획득 조건(25m, 35도)으로만 한다 - 유지 조건은 이미 잠근 대상에게만 쓰는 넓은 값이라
        /// 새 대상을 고르는 데 쓰면 화면 밖 적이 걸린다
        /// </summary>
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
                    continue; // 카메라가 없으면 화면 기준 선택 자체가 성립하지 않는다
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

        /// <summary>
        /// 콜라이더에서 조준점과 부위 번호를 함께 구한다.
        /// 부위 번호는 NetworkIdentity 아래 IAimAssistTarget의 순서다 -
        /// 같은 프리팹이므로 서버와 모든 클라이언트가 같은 번호를 매긴다.
        /// 비활성 부위도 세는 이유는 꼬리 한 덩이가 부서져 꺼졌을 때 나머지 번호가 밀리면
        /// 잠가 둔 대상이 조용히 다른 부위로 바뀌기 때문이다
        /// </summary>
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

        // 서버가 확정한 netId, 부위 번호를 이 화면의 실제 오브젝트로 푼다.
        // 스폰 순서상 대상이 아직 도착하지 않은 프레임이 있어 잡을 때까지 다시 시도한다
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

            // 계약이 MonoBehaviour가 아닐 수도 있으므로 Transform은 따로 확인해서 잡는다
            Component aimComponent = _targetAim as Component;

            _targetTransform = (aimComponent != null) ? aimComponent.transform : identity.transform;
            _targetCollider  = _targetTransform.GetComponentInChildren<Collider>();
            _isTargetBoss    = (identity.gameObject.layer == LayerMask.NameToLayer(Constants.Layer.BOSS));
            _isTargetPart    = (_targetTransform != identity.transform);

            // 해석에 성공한 시점이 화면에서 "잠겼다"가 되는 순간이다.
            // SyncVar 훅에서 알리면 대상이 아직 스폰되지 않은 프레임에도 잠겼다고 나간다
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

        /// <summary>
        /// 모델이 바라볼 방향을 표현 계층에 넘긴다.
        /// 대상 오브젝트를 넘기고 방향은 표현이 그때그때 계산한다 -
        /// 방향을 미리 계산해 넘기면 컴포넌트 실행 순서에 따라 한 프레임 묵은 값이 들어간다
        /// </summary>
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

        /// <summary>
        /// 대상에 맞는 붐 길이. 보스 몸통이 먼 이유는 꼬리 펼침이 프레임에 들어와야 하기 때문이고,
        /// 부위를 잠갔을 때 다시 당기는 이유는 그로기 10초 동안 그 덩이를 쳐야 하기 때문이다
        /// </summary>
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

            // 마우스를 쓰는 UI가 열려 있으면 중클릭, 휠이 그쪽 것이다
            return CameraFrame.InputEnabled;
        }

        // 화면 중심에서 대상까지의 각도. 카메라가 없는 씬에서는 판단할 수 없으므로 false다
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

        // 카메라에서 조준점까지 벽이 있는지. 카메라와 화살이 같은 마스크를 쓴다 - 벽은 둘에게 같은 벽이다
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
