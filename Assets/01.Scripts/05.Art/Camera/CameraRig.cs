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
    /// <summary>
    /// 3인칭 오버숄더 추적 카메라. 대상 추적, 마우스 회전, 지형 충돌 회피를 소유하고
    /// 화면 기준 yaw를 <see cref="CameraFrame"/>에 민다 - 전투는 그 값만 읽으므로
    /// 02.Combat이 05.Art를 참조하지 않는다.
    /// 완전히 로컬이라 동기화하지 않고, 판정에도 관여하지 않는다.
    /// <b>Target은 바뀔 수 있어야 한다</b> - 관전 카메라가 이것과 같은 물건이고 대상만 갈아 끼운다
    /// </summary>
    /// <remarks>
    /// 수치는 전부 <see cref="CameraConfigSO"/>가 소유하고 여기는 매 프레임 읽기만 한다.
    /// 씬마다 카메라 오브젝트가 따로 있어도 감이 갈라지지 않고, Play 중에 에셋을 밀면 바로 반영된다
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN_DEGREES = 360f;
        private const float HALF_TURN_DEGREES = 180f;

        // 동시에 살아 있을 수 있는 셰이크 수. 넘치면 가장 약한 것을 밀어낸다 -
        // 게임 루프에서 할당이 일어나지 않도록 고정 배열로 잡는다
        private const int SHAKE_SLOT_COUNT = 4;

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("비워 두면 자기 캐릭터가 스폰될 때 자동으로 잡는다. 관전, 미리보기 씬에서는 직접 넣는다")]
        [SerializeField] private Transform _target = null;

        private Camera _camera;

        private float _yaw   = 0f;
        private float _pitch = 0f;

        // 중심을 스무딩하고 붐(카메라까지의 팔)은 그 위에 즉시 붙인다.
        // 카메라 위치 자체를 스무딩하면 화면을 돌릴 때 카메라가 궤도를 따라 끌려와 회전이 늦게 느껴진다
        private Vector3 _smoothedPivot = Vector3.zero;
        private Vector3 _pivotVelocity = Vector3.zero;
        private bool    _hasPivot      = false;

        private float _currentDistance  = 0f;
        private float _distanceVelocity = 0f;

        private bool _cursorLocked = false;

        // 셰이크 슬롯. Amplitude가 0이면 비어 있는 칸이다
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

        /// <summary>
        /// 따라갈 대상. 관전 카메라가 사망 시 다른 생존자로 갈아 끼운다.
        /// 자기 캐릭터로 하드코딩하지 않는다
        /// </summary>
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

                // 새 대상까지 스무딩으로 흘러가면 맵을 가로지르는 긴 카메라 이동이 된다
                _hasPivot = false;
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 셰이크를 하나 더한다. 전투가 아니라 <b>아트 구현체</b>(CombatFeedbackImpl)가 부른다 -
        /// 전투는 ICombatFeedback만 알고 카메라를 참조하지 않는다.
        /// 여러 개가 겹치면 합산하되 설정의 상한을 넘지 않는다
        /// </summary>
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
                // 빈 칸이 있으면 그대로 쓴다
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

            // 씬에 놓인 각도를 시작값으로 삼는다. 0으로 초기화하면 첫 프레임에 카메라가 홱 돈다
            Vector3 euler = transform.eulerAngles;
            _yaw   = euler.y;
            _pitch = NormalizeAngle(euler.x);
        }

        private void OnDisable()
        {
            // 컷신이 리그를 잡아가거나 플레이가 끝났는데 커서가 잠긴 채로 남으면 창을 빠져나갈 수 없다
            ReleaseCursor();
        }

        // 이 프레임의 이동이 전부 끝난 뒤에 붙는다. 원격 캐릭터는 NetworkTransform이,
        // 자기 캐릭터는 PlayerPrediction이 Update에서 위치를 옮기므로
        // 같은 Update에서 돌면 실행 순서에 따라 한 프레임 뒤처져 떨림으로 보인다
        private void LateUpdate()
        {
            // 부트 순서상 GameDBBootstrap보다 먼저 도는 프레임이 있다.
            // 그때는 씬에 놓인 그대로 두고 다음 프레임에 다시 본다
            if (!GameDB.IsInitialized)
            {
                return;
            }

            CameraConfigSO config = GameDB.Camera;
            if (config == null)
            {
                return; // 누락은 GameDBBootstrap이 이미 오류로 알린다. 여기서 매 프레임 또 찍지 않는다
            }

            if (!TryResolveTarget())
            {
                return; // 대상이 없으면 씬에 놓인 그대로 둔다. yaw도 0이라 이동은 지금과 같이 월드 축을 쓴다
            }

            UpdateCursorState();
            UpdateLook(config);

            // 전투가 읽는 유일한 값이다. 회전을 반영한 직후에 밀어야 이번 프레임의 입력과 화면이 맞는다
            CameraFrame.Yaw = _yaw;

            ApplyLens(config);
            ApplyTransform(config);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 자기 캐릭터는 스폰 시점이 카메라보다 늦다. 잡을 때까지만 확인하고 잡은 뒤에는 보지 않는다
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
            if (!CanRotate())
            {
                return;
            }

            // 마우스 축은 이미 프레임 간 이동량이라 deltaTime을 곱하면 프레임률에 따라 감도가 달라진다
            float mouseX = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_X) * ToAxisScale(config.DefaultSensitivityX);
            float mouseY = Input.GetAxisRaw(Constants.Player.AXIS_MOUSE_Y) * ToAxisScale(config.DefaultSensitivityY);

            _yaw = Mathf.Repeat(_yaw + mouseX, FULL_TURN_DEGREES);

            // 마우스를 올리면 Mouse Y가 양수고, Unity의 X 회전은 양수가 아래를 본다
            _pitch = Mathf.Clamp(
                _pitch + ((config.DefaultInvertY) ? mouseY : -mouseY),
                config.PitchMin,
                config.PitchMax);
        }

        // 매 프레임 같은 값을 넣으면 카메라가 투영 행렬을 다시 만든다
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

            // 오버숄더는 중심 자체를 옆으로 민다. 붐 끝만 옆으로 밀면 카메라가 대상을 비껴본다
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

            // 셰이크는 붐 계산이 끝난 뒤에 더한다. 충돌 판정에 흔들림을 넣으면
            // 벽 근처에서 거리가 흔들림에 따라 진동한다
            transform.position = _smoothedPivot
                + (boomDirection * ResolveDistance(config, boomDirection))
                + ResolveShakeOffset(config);

            transform.rotation = rotation; // 회전은 스무딩하지 않는다. 회전 셰이크도 넣지 않는다
        }

        /// <summary>
        /// 살아 있는 셰이크를 합쳐 이번 프레임의 위치 오프셋을 만든다.
        /// <b>위치만 흔든다</b> - 회전을 흔들면 FOV를 고정한 것과 같은 이유로 패링 0.32초 판단을 방해한다.
        /// 감쇠는 지속시간 동안 선형이다
        /// </summary>
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
                    _shakes[i] = default; // 칸을 비운다
                    continue;
                }

                total += _shakes[i].Amplitude * (1f - (_shakes[i].Elapsed / _shakes[i].Duration));
            }

            // 접근성 옵션이 0이면 여기서 끝난다. 슬롯 수명은 위에서 이미 흘렀으므로 켜고 끄기가 자연스럽다
            if ((scale <= 0f) || (total <= 0f))
            {
                return Vector3.zero;
            }

            // System.Random과 이름이 겹쳐 모호해지므로 명시한다 (이 파일은 using System을 쓴다)
            return UnityEngine.Random.insideUnitSphere * Mathf.Min(total, config.ShakeMaxAmplitude) * scale;
        }

        /// <summary>
        /// 벽에 걸리면 카메라를 당긴다. 아레나는 담장 R=22와 마당 R=18의 4m가 이미 완충이라
        /// 충돌 처리가 없어도 되지만, 개활맵 768m의 바위 275개는 그대로 뚫는다
        /// </summary>
        private float ResolveDistance(CameraConfigSO config, Vector3 boomDirection)
        {
            float desired = config.Distance;

            // 중심이 이미 벽 안에 있으면 SphereCast가 false를 돌려준다. 그때는 기본 거리를 쓴다 -
            // 여기서 0으로 붙이면 지형 속에서 카메라가 대상에 처박힌다
            bool isBlocked = Physics.SphereCast(
                _smoothedPivot,
                config.ProbeRadius,
                boomDirection,
                out RaycastHit hit,
                config.Distance,
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
                // 당김과 풂이 같은 속도면 벽을 스칠 때마다 거리가 진동한다. 풀 때만 속도를 제한한다
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

            // 커서를 잠그지 않는 개발 모드에서는 씬뷰처럼 Alt를 누른 동안에만 돈다
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
                return; // 매 프레임 같은 값을 넣지 않는다
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

        // 3인 테스트는 대부분 한 PC에서 창 세 개로 한다. 커서를 잠그면 창 하나가 마우스를 독점해
        // 나머지 둘이 카메라를 못 돌리고, 그러면 LAN 3대를 켜야 해서 L3 빈도가 떨어진다.
        // 실행 인자는 프로세스가 사는 동안 바뀌지 않으므로 한 번만 읽는다.
        // NetworkLaunchArgs가 아니라 여기서 읽는 이유는 그쪽이 씬 컴포넌트라
        // 그 오브젝트가 없는 씬에서는 인자가 파싱되지 않기 때문이다
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

        // 감도를 도/픽셀로 적어 두기 위해 InputManager가 이미 곱한 배수를 되돌린다
        private static float ToAxisScale(float degreesPerPixel)
        {
            return degreesPerPixel / Constants.Player.MOUSE_AXIS_SENSITIVITY;
        }

        // eulerAngles는 0~360으로 돌아오므로 -30 같은 pitch 하한과 비교할 수 없다
        private static float NormalizeAngle(float degrees)
        {
            float wrapped = Mathf.Repeat(degrees, FULL_TURN_DEGREES);
            return (wrapped > HALF_TURN_DEGREES) ? (wrapped - FULL_TURN_DEGREES) : wrapped;
        }
    }
}
