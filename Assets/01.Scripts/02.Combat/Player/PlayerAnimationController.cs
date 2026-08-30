// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary>
    /// 새 네트워크 값을 만들지 않고, 이미 동기화된 transform.position(서버 권위 또는 로컬 예측)의
    /// 변화량만 관찰해 애니메이션을 재생하는 표현 전용 컴포넌트. 소유·원격·호스트 인스턴스가
    /// 각자 자신이 보는 위치로 독립 재생하므로 별도 동기화가 필요 없다. 판정에는 관여하지 않는다
    /// </summary>
    public class PlayerAnimationController : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string PARAM_SPEED      = "Speed";
        private const string PARAM_ROLL       = "Roll";
        private const string PARAM_ROLL_SPEED = "RollSpeed";

        private const string CLIP_ROLL = "SprintingForwardRoll";

        private const float SPEED_SMOOTH_TIME    = 0.15f;   // 초, 서버 틱 사이 위치가 안 바뀌는 구간을 완화한다
        private const float TURN_DEGREES_PER_SEC = 720f;
        private const float MOVE_SPEED_EPSILON   = 0.05f;   // m/s, 이 미만은 정지로 본다
        private const float MOVE_DELTA_EPSILON   = 0.0005f; // m, 이 미만의 프레임 변위는 방향 계산에 쓰지 않는다
        private const float MIN_DELTA_TIME       = 0.0001f; // 초, 0 나누기를 막는 하한
        private const float MIN_SPEED_RANGE      = 0.0001f; // m/s, 걷기와 달리기 속도가 같을 때 0 나누기를 막는다

        /****************************************
        *                Fields
        ****************************************/

        private Animator  _animator;
        private Transform _visualRoot;

        private Vector3 _lastPosition;
        private bool    _hasLastPosition = false;
        private float   _smoothedSpeed   = 0f;
        private float   _rollClipLength  = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>(true);
            if (_animator == null)
            {
                Debug.LogWarning("[PlayerAnimationController] 자식에서 Animator를 찾지 못했다", this);
                return;
            }

            _animator.applyRootMotion = false; // 이동은 CharacterController가 결정한다, 애니메이션은 표현만 한다
            _visualRoot = _animator.transform;

            _rollClipLength = FindClipLength(CLIP_ROLL);
        }

        private void Update()
        {
            if (_animator == null)
            {
                return;
            }

            Vector3 position = transform.position;

            if (!_hasLastPosition)
            {
                _lastPosition    = position;
                _hasLastPosition = true;
                return;
            }

            Vector3 planarDelta = position - _lastPosition;
            planarDelta.y = 0f;
            _lastPosition = position;

            float instantSpeed = planarDelta.magnitude / Mathf.Max(Time.deltaTime, MIN_DELTA_TIME);
            float smoothing    = 1f - Mathf.Exp(-Time.deltaTime / SPEED_SMOOTH_TIME);
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantSpeed, smoothing);

            _animator.SetFloat(PARAM_SPEED, NormalizeSpeed(_smoothedSpeed));

            if (planarDelta.magnitude > MOVE_DELTA_EPSILON)
            {
                Quaternion targetRotation = Quaternion.LookRotation(planarDelta.normalized, Vector3.up);
                _visualRoot.rotation = Quaternion.RotateTowards(_visualRoot.rotation, targetRotation, TURN_DEGREES_PER_SEC * Time.deltaTime);
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 구르기 모션을 한 번 재생한다. PlayerController의 구르기 SyncVar 훅이 호출한다 </summary>
        public void PlayRoll()
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(PARAM_ROLL_SPEED, ResolveRollSpeed());
            _animator.SetTrigger(PARAM_ROLL);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 클립 길이를 CombatConfigSO의 구르기 동작 시간에 맞추는 재생 배속.
        /// 밸런스로 구르기 시간이 바뀌어도 클립을 다시 만들지 않아도 되고, 무적 시간과 모션이 어긋나지 않는다
        /// </summary>
        private float ResolveRollSpeed()
        {
            if (!GameDB.IsInitialized || (_rollClipLength <= 0f))
            {
                return 1f;
            }

            float rollDuration = GameDB.Combat.RollTotalDuration;
            if (rollDuration <= 0f)
            {
                return 1f;
            }

            return _rollClipLength / rollDuration;
        }

        private float FindClipLength(string clipName)
        {
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            if (controller == null)
            {
                return 0f;
            }

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; ++i)
            {
                if ((clips[i] != null) && (clips[i].name == clipName))
                {
                    return clips[i].length;
                }
            }

            Debug.LogWarning($"[PlayerAnimationController] 애니메이터에 '{clipName}' 클립이 없어 배속을 계산할 수 없다", this);
            return 0f;
        }

        /// <summary>
        /// 블렌드 트리 문턱값(0=정지, 1=걷기, 2=달리기)에 맞춰 실제 속도를 정규화한다.
        /// CombatConfigSO의 걷기 · 달리기 속도가 밸런스 조정으로 바뀌어도 애니메이터 자산을 다시 만들 필요가 없다
        /// </summary>
        private static float NormalizeSpeed(float speed)
        {
            if (!GameDB.IsInitialized || (speed <= MOVE_SPEED_EPSILON))
            {
                return 0f;
            }

            float walkSpeed = GameDB.Combat.WalkSpeed;
            float runSpeed  = GameDB.Combat.RunSpeed;

            if (speed <= walkSpeed)
            {
                return speed / walkSpeed;
            }

            float runRange = Mathf.Max(runSpeed - walkSpeed, MIN_SPEED_RANGE);
            return 1f + Mathf.Clamp01((speed - walkSpeed) / runRange);
        }
    }
}
