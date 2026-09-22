// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.Art
{
    /// <summary> 3인칭 오버숄더 추적 카메라. 대상 추적, 마우스 회전, 지형 충돌 회피를 처리한다 </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN_DEGREES = 360f;
        private const float HALF_TURN_DEGREES = 180f;

        // 동시에 살아 있을 수 있는 셰이크 수
        private const int SHAKE_SLOT_COUNT = 4;

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

        // 셰이크 슬롯
        private readonly ShakeSlot[] _shakes = new ShakeSlot[SHAKE_SLOT_COUNT];

        /****************************************
        *                Types
        ****************************************/

        private struct ShakeSlot
        {
            public float Amplitude;
            public float Duration;
            public float Elapsed;
        }

        /****************************************
        *              Properties
        ****************************************/

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
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 셰이크를 하나 더한다 </summary>
        public void AddShake(float amplitude, float duration)
        {
            if ((amplitude <= 0f) || (duration <= 0f))
            {
                return;
            }

            int weakest      = 0;
            float weakestMag = float.MaxValue;

            for (int i = 0; i < _shakes.Length; ++i)
            {
                if (_shakes[i].Amplitude <= 0f)
                {
                    weakest = i;
                    break;
                }

                float remaining = _shakes[i].Amplitude * (1f - (_shakes[i].Elapsed / _shakes[i].Duration));
                if (remaining < weakestMag)
                {
                    weakestMag = remaining;
                    weakest    = i;
                }
            }

            _shakes[weakest] = new ShakeSlot { Amplitude = amplitude, Duration = duration, Elapsed = 0f };
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
        }

        private void OnDisable()
        {
            ReleaseCursor();
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
            if (!Mathf.Approximately(_camera.fieldOfView, config.FieldOfView))
            {
                _camera.fieldOfView = config.FieldOfView;
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

            transform.position = _smoothedPivot
                + (boomDirection * distance)
                + ResolveShakeOffset(config);

            transform.rotation = rotation;

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

        /// <summary> 살아 있는 셰이크를 합쳐 이번 프레임의 위치 오프셋을 만든다 </summary>
        private Vector3 ResolveShakeOffset(CameraConfigSO config)
        {
            float scale = config.ShakeScale;
            float total = 0f;

            for (int i = 0; i < _shakes.Length; ++i)
            {
                if (_shakes[i].Amplitude <= 0f)
                {
                    continue;
                }

                _shakes[i].Elapsed += Time.deltaTime;

                if (_shakes[i].Elapsed >= _shakes[i].Duration)
                {
                    _shakes[i] = default;
                    continue;
                }

                total += _shakes[i].Amplitude * (1f - (_shakes[i].Elapsed / _shakes[i].Duration));
            }

            if ((scale <= 0f) || (total <= 0f))
            {
                return Vector3.zero;
            }

            return UnityEngine.Random.insideUnitSphere * Mathf.Min(total, config.ShakeMaxAmplitude) * scale;
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
