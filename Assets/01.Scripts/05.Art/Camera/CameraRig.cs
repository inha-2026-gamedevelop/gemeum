// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.Art
{
    /// <summary>
    /// 3인칭 오버숄더 추적 카메라. 대상 추적, 마우스 회전, 지형 충돌 회피를 처리한다
    /// 계산한 자세는 Cinemachine 가상 카메라에 넣고 CinemachineBrain이 실제 카메라에 적용한다 -
    /// 셰이크·카메라 연출은 그 파이프라인의 확장(FrameIndependentShake·CameraEffectPlayer)이 얹는다
    /// 가상 카메라는 씬이 아니라 런타임에 만든다. 씬 파일을 고치지 않아야 씬 충돌이 안 난다
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN_DEGREES = 360f;
        private const float HALF_TURN_DEGREES = 180f;

        private const string VIRTUAL_CAMERA_NAME = "CM 추적 카메라 (런타임)";

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("비워 두면 자기 캐릭터가 스폰될 때 자동으로 잡는다. 관전, 미리보기 씬에서는 직접 넣는다")]
        // 개발용 기즈모 표시 값. 게임 밸런스가 아니라 눈에 보이는 크기와 색이다
        private const float GIZMO_TARGET_SIZE   = 0.4f; // 목표 피벗 십자 크기(m)
        private const float GIZMO_SMOOTHED_SIZE = 0.3f; // 따라간 피벗 십자 크기(m)
        private const float GIZMO_LOCKON_RADIUS = 0.5f; // 락온 대상 원 반지름(m)

        private static readonly Color GIZMO_TARGET_COLOR   = new Color(1f, 0.85f, 0.35f, 0.9f);
        private static readonly Color GIZMO_SMOOTHED_COLOR = new Color(0.45f, 0.9f, 1f, 0.9f);
        private static readonly Color GIZMO_LAG_COLOR      = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color GIZMO_WANTED_COLOR   = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color GIZMO_BOOM_COLOR     = new Color(0.55f, 1f, 0.6f, 0.9f);
        private static readonly Color GIZMO_LOCKON_COLOR   = new Color(1f, 0.4f, 0.4f, 0.9f);

        [SerializeField] private Transform _target = null;

        private Camera _camera;

        // Cinemachine. 브레인은 ManualUpdate라 이 리그가 자세를 넣은 직후에만 카메라를 움직인다 -
        // 리그를 끄면(검증 도구 등) 카메라 Transform을 직접 잡을 수 있다
        private CinemachineBrain      _brain;
        private CinemachineCamera     _virtualCamera;
        private FrameIndependentShake _shake;
        private CameraEffectPlayer    _effects;

        private float _yaw   = 0f;
        private float _pitch = 0f;

        // 중심을 스무딩하고 붐(카메라까지의 팔)은 그 위에 즉시 붙인다
        private Vector3 _smoothedPivot = Vector3.zero;
        private Vector3 _pivotVelocity = Vector3.zero;
        private bool    _hasPivot      = false;

        private float _currentDistance  = 0f;
        private float _distanceVelocity = 0f;

        private bool _defaultPitchApplied = false;
        private bool _cursorLocked = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 화면을 그리는 게임 카메라. 타격감·Timeline 연출이 여기로 들어온다 </summary>
        public static CameraRig Current { get; private set; }

        /// <summary> 이 리그가 구동하는 Cinemachine 가상 카메라 </summary>
        public CinemachineCamera VirtualCamera => _virtualCamera;

        /// <summary> 따라갈 대상 </summary>
        public Transform Target
        {
            get { return _target; }
            set
            {
                if (_target == value)
                {
                    return;
                }

                _target = value;

                _hasPivot = false;

                // 관전 대상이 바뀌면 이전 대상의 흔들림을 새 화면에 끌고 가지 않는다
                ClearFeedback();
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 셰이크를 하나 더한다. 모양(방향·박자·감쇠)은 스펙이 정한다 </summary>
        public void Shake(in CameraConfigSO.ShakeSpec spec)
        {
            if (_shake != null)
            {
                _shake.Add(spec);
            }
        }

        /// <summary> 카메라 연출(거리·화각을 잠깐 바꿨다 되돌림)을 재생한다 </summary>
        public void PlayEffect(in CameraConfigSO.CameraEffectSpec spec)
        {
            if (_effects != null)
            {
                _effects.Play(spec);
            }
        }

        /// <summary> Timeline 카메라 연출 트랙이 이번 프레임 값을 넣는다 </summary>
        public void SetTimelineEffect(float distance, float fov, float dutch)
        {
            if (_effects != null)
            {
                _effects.SetTimelineInput(distance, fov, dutch);
            }
        }

        /// <summary> 셰이크와 연출을 모두 끊는다. 관전 대상이 바뀌는 것처럼 화면이 통째로 바뀔 때 쓴다 </summary>
        public void ClearFeedback()
        {
            if (_shake != null)
            {
                _shake.Clear();
            }

            if (_effects != null)
            {
                _effects.Clear();
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            // yaw는 씬에 놓인 각도를 시작값으로 삼는다. pitch는 GameDB가 깨어난 뒤 설정값으로 덮는다 -
            // 씬 각도를 그대로 쓰면 씬을 만지는 사람에 따라 게임 시작 각도가 따라 바뀐다
            Vector3 euler = transform.eulerAngles;
            _yaw   = euler.y;
            _pitch = NormalizeAngle(euler.x);

            SetUpCinemachine();
        }

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            ReleaseCursor();

            if (Current == this)
            {
                Current = null;
            }
        }

        private void OnDestroy()
        {
            if (_virtualCamera != null)
            {
                Destroy(_virtualCamera.gameObject);
            }
        }

        // 커서는 LateUpdate가 아니라 여기서 다룬다. LateUpdate는 카메라 대상이 없으면 먼저 빠져나가는데,
        // 로비처럼 아직 플레이어가 없는 상태에서도 ESC로 커서를 꺼낼 수 있어야 한다
        private void Update()
        {
            // 설치 조준의 첫 Esc는 조준 취소에 쓰인다. 다음 Esc부터 기존 커서 토글이다
            if (Input.GetKeyDown(Constants.Player.KEY_TOGGLE_CURSOR) &&
                (!LocalPlacementAim.SuppressesCombatInput))
            {
                CameraFrame.InputEnabled = !CameraFrame.InputEnabled;
            }

            UpdateCursorState();
        }

        private void LateUpdate()
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            CameraConfigSO config = GameDB.Camera;
            if (config == null)
            {
                return;
            }

            if (!TryResolveTarget())
            {
                return;
            }

            ApplyDefaultPitchOnce(config);
            UpdateLook(config);

            // 전투가 읽는 화면 기준 yaw
            CameraFrame.Yaw = _yaw;

            ApplyLens(config);
            ApplyTransform(config);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 이 카메라에 브레인을 붙이고, 자세를 받을 가상 카메라와 확장을 만든다
        /// 가상 카메라는 이 카메라의 자식이면 안 된다 - 브레인이 부모를 움직이면 자식이 따라와 되먹임이 생긴다
        /// </summary>
        private void SetUpCinemachine()
        {
            if (!TryGetComponent(out _brain))
            {
                _brain = gameObject.AddComponent<CinemachineBrain>();
            }

            _brain.UpdateMethod      = CinemachineBrain.UpdateMethods.ManualUpdate;
            _brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            _brain.IgnoreTimeScale   = false;

            var host = new GameObject(VIRTUAL_CAMERA_NAME);
            host.transform.SetPositionAndRotation(transform.position, transform.rotation);

            // 이 카메라는 씬을 넘어 살아남는다(SessionPersistence). 가상 카메라도 같이 살고 OnDestroy에서 같이 지운다
            DontDestroyOnLoad(host);

            _virtualCamera      = host.AddComponent<CinemachineCamera>();
            _virtualCamera.Lens = LensSettings.FromCamera(_camera);

            // 확장은 가상 카메라가 먼저 있어야 파이프라인에 붙는다
            _effects = host.AddComponent<CameraEffectPlayer>();
            _shake   = host.AddComponent<FrameIndependentShake>();
        }

        /// <summary> 첫 프레임에 한 번만 설정값 각도로 맞춘다. 그 뒤에는 사람이 돌린 각도를 덮으면 안 된다 </summary>
        private void ApplyDefaultPitchOnce(CameraConfigSO config)
        {
            if (_defaultPitchApplied)
            {
                return;
            }

            _defaultPitchApplied = true;
            _pitch               = Mathf.Clamp(config.DefaultPitch, config.PitchMin, config.PitchMax);
        }

        private bool TryResolveTarget()
        {
            if (_target != null)
            {
                return true;
            }

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return false;
            }

            Target = localPlayer.transform;
            return true;
        }

        private void UpdateLook(CameraConfigSO config)
        {
            // 설치 조준 중에는 자유 커서로 지면을 고르므로 시점을 아예 고정한다
            // 락온 추적도 함께 멈춘다 - 대상이 움직인다고 화면이 돌면 커서가 딸려 간다
            if (LocalPlacementAim.LocksCameraRotation)
            {
                return;
            }

            // 락온 중에는 전투(LockOnController)가 정한 대상이 화면을 정한다
            if (CameraFrame.LockOnActive)
            {
                ApplyLockOnLook(config);
                return;
            }

            if (!CanRotate())
            {
                return;
            }

            // 조준을 끝낸 첫 프레임의 델타는 커서를 옮긴 거리다. 그대로 넣으면 시점이 튄다
            if (LocalPlacementAim.ConsumeSettleFrame())
            {
                return;
            }

            float mouseX = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_X) * ToAxisScale(config.DefaultSensitivityX);
            float mouseY = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_Y) * ToAxisScale(config.DefaultSensitivityY);

            _yaw = Mathf.Repeat(_yaw + mouseX, FULL_TURN_DEGREES);

            _pitch = Mathf.Clamp(
                _pitch + ((config.DefaultInvertY) ? mouseY : -mouseY),
                config.PitchMin,
                config.PitchMax);
        }

        /// <summary> 중심에서 대상 조준점을 향하도록 각도를 돌린다 </summary>
        private void ApplyLockOnLook(CameraConfigSO config)
        {
            Vector3 pivot = _target.position + (Vector3.up * config.PivotHeight);
            Vector3 delta = CameraFrame.LockOnPoint - pivot;

            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 euler = Quaternion.LookRotation(delta, Vector3.up).eulerAngles;

            float step = config.LockOnTurnDegreesPerSecond * Time.deltaTime;

            _yaw = Mathf.Repeat(Mathf.MoveTowardsAngle(_yaw, euler.y, step), FULL_TURN_DEGREES);

            _pitch = Mathf.Clamp(
                Mathf.MoveTowardsAngle(_pitch, NormalizeAngle(euler.x), step),
                config.PitchMin,
                config.PitchMax);
        }

        private void ApplyLens(CameraConfigSO config)
        {
            // 실제 카메라 렌즈는 브레인이 가상 카메라 렌즈로 덮는다. 연출·셰이크의 화각 변화는 그 위에 얹힌다
            if (!Mathf.Approximately(_virtualCamera.Lens.FieldOfView, config.FieldOfView))
            {
                _virtualCamera.Lens.FieldOfView = config.FieldOfView;
            }
        }

        private void ApplyTransform(CameraConfigSO config)
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            Vector3 pivot = _target.position
                + (Vector3.up * config.PivotHeight)
                + ((rotation * Vector3.right) * config.SideOffset);

            if (!_hasPivot)
            {
                _smoothedPivot    = pivot;
                _pivotVelocity    = Vector3.zero;
                _currentDistance  = config.Distance;
                _distanceVelocity = 0f;
                _hasPivot         = true;
            }
            else
            {
                _smoothedPivot = Vector3.SmoothDamp(_smoothedPivot, pivot, ref _pivotVelocity, config.FollowSmoothTime);
            }

            Vector3 boomDirection = -(rotation * Vector3.forward);

            float distance = ResolveDistance(config, boomDirection);

            // 셰이크·연출이 섞이지 않은 기준 자세다. 흔들림은 보정값이라 다음 프레임 기준을 오염시키지 않는다
            _virtualCamera.transform.SetPositionAndRotation(_smoothedPivot + (boomDirection * distance), rotation);
            _effects.BoomLength = distance;

            _brain.ManualUpdate();

            DrawGizmos(config, pivot, boomDirection, distance);
        }

        /// <summary>
        /// 카메라 로직에 엮인 값들을 눈으로 확인할 수 있게 남긴다
        /// 여러 후보 값(목표 피벗과 따라간 피벗, 원하는 거리와 벽에 막힌 거리)을
        /// 한 프레임에 같이 봐야 어디서 튀는지 알 수 있다 - 브레이크포인트로는 못 본다
        /// </summary>
        private void DrawGizmos(CameraConfigSO config, Vector3 pivot, Vector3 boomDirection, float distance)
        {
            // 목표 피벗과 실제로 따라간 피벗. 둘이 벌어진 폭이 곧 따라가는 지연이다
            GGizmo.Point(GGizmoChannel.Camera, pivot, GIZMO_TARGET_SIZE, GIZMO_TARGET_COLOR);
            GGizmo.Point(GGizmoChannel.Camera, _smoothedPivot, GIZMO_SMOOTHED_SIZE, GIZMO_SMOOTHED_COLOR);
            GGizmo.Line(GGizmoChannel.Camera, pivot, _smoothedPivot, GIZMO_LAG_COLOR);

            // 붐. 원하는 거리까지 옅은 선, 실제로 놓인 자리까지 진한 선이다
            Vector3 wanted = _smoothedPivot + (boomDirection * config.Distance);

            GGizmo.Line(GGizmoChannel.Camera, _smoothedPivot, wanted, GIZMO_WANTED_COLOR);
            GGizmo.Line(GGizmoChannel.Camera, _smoothedPivot, transform.position, GIZMO_BOOM_COLOR);

            if (CameraFrame.LockOnActive)
            {
                GGizmo.Line(GGizmoChannel.Camera, _smoothedPivot, CameraFrame.LockOnPoint,
                    GIZMO_LOCKON_COLOR);
                GGizmo.Circle(GGizmoChannel.Camera, CameraFrame.LockOnPoint, Vector3.up,
                    GIZMO_LOCKON_RADIUS, GIZMO_LOCKON_COLOR);
            }

            GGizmo.Value(GGizmoChannel.Camera, "yaw",      _yaw.ToString("0.###"));
            GGizmo.Value(GGizmoChannel.Camera, "pitch",    _pitch.ToString("0.###"));
            GGizmo.Value(GGizmoChannel.Camera, "카메라 거리", distance.ToString("0.###"));
            GGizmo.Value(GGizmoChannel.Camera, "락온",      CameraFrame.LockOnActive ? "켜짐" : "꺼짐");

            // 흔들리는 모양을 봐야 "느낌이 이상한" 구간을 찾는다
            GGizmo.PlotValue("yaw", _yaw);
            GGizmo.PlotValue("카메라 거리", distance);
        }

        /// <summary> 벽에 걸리면 카메라를 당긴다 </summary>
        private float ResolveDistance(CameraConfigSO config, Vector3 boomDirection)
        {
            float baseDistance = ((CameraFrame.LockOnActive) && (CameraFrame.LockOnDistance > 0f))
                ? CameraFrame.LockOnDistance
                : config.Distance;

            float desired = baseDistance;

            bool isBlocked = Physics.SphereCast(
                _smoothedPivot,
                config.ProbeRadius,
                boomDirection,
                out RaycastHit hit,
                baseDistance,
                config.CollisionMask,
                QueryTriggerInteraction.Ignore);

            if (isBlocked)
            {
                desired = Mathf.Max(hit.distance, config.MinDistance);
            }

            if (desired < _currentDistance)
            {
                _currentDistance = Mathf.SmoothDamp(
                    _currentDistance, desired, ref _distanceVelocity, config.PullInSmoothTime);
            }
            else
            {
                _currentDistance  = Mathf.MoveTowards(_currentDistance, desired, config.PushOutSpeed * Time.deltaTime);
                _distanceVelocity = 0f;
            }

            return _currentDistance;
        }

        /****************************************
        *              Cursor
        ****************************************/

        private bool CanRotate()
        {
            if (!CameraFrame.InputEnabled)
            {
                return false;
            }

            if (IsCursorLockDisabled)
            {
                return Input.GetKey(Constants.Player.KEY_FREE_LOOK);
            }

            return true;
        }

        private void UpdateCursorState()
        {
            bool shouldLock = ((CameraFrame.InputEnabled) && (!IsCursorLockDisabled));

            if (shouldLock == _cursorLocked)
            {
                return;
            }

            if (shouldLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
                _cursorLocked    = true;
                return;
            }

            ReleaseCursor();
        }

        private void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            _cursorLocked    = false;
        }

        /****************************************
        *              Dev Flag
        ****************************************/

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        // 실행 인자로 커서 잠금을 끈다
        private const string ARG_NO_CURSOR_LOCK = "-nocursorlock";

        private static bool _cursorLockDisabledCached = false;
        private static bool _hasCursorLockFlag        = false;

        private static bool IsCursorLockDisabled
        {
            get
            {
                if (_hasCursorLockFlag)
                {
                    return _cursorLockDisabledCached;
                }

                string[] args = Environment.GetCommandLineArgs();

                for (int i = 0; i < args.Length; ++i)
                {
                    if (string.Equals(args[i], ARG_NO_CURSOR_LOCK, StringComparison.OrdinalIgnoreCase))
                    {
                        _cursorLockDisabledCached = true;
                        break;
                    }
                }

                _hasCursorLockFlag = true;
                return _cursorLockDisabledCached;
            }
        }

#else

        private static bool IsCursorLockDisabled => false;

#endif

        /****************************************
        *              Helpers
        ****************************************/

        private static float ToAxisScale(float degreesPerPixel)
        {
            return degreesPerPixel / Constants.Player.MOUSE_AXIS_SENSITIVITY;
        }

        // 각도를 -180~180 범위로 정규화한다
        private static float NormalizeAngle(float degrees)
        {
            float wrapped = Mathf.Repeat(degrees, FULL_TURN_DEGREES);
            return (wrapped > HALF_TURN_DEGREES) ? (wrapped - FULL_TURN_DEGREES) : wrapped;
        }
    }
}
