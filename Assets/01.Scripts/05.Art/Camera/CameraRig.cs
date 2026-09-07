// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

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

            // 씬에 놓인 각도를 시작값으로 삼는다
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
            if (Input.GetKeyDown(Constants.Player.KEY_TOGGLE_CURSOR))
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

            UpdateLook(config);

            // 전투가 읽는 화면 기준 yaw
            CameraFrame.Yaw = _yaw;

            ApplyLens(config);
            ApplyTransform(config);
        }

        /****************************************
        *                Methods
        ****************************************/

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

            transform.position = _smoothedPivot
                + (boomDirection * ResolveDistance(config, boomDirection))
                + ResolveShakeOffset(config);

            transform.rotation = rotation;
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
