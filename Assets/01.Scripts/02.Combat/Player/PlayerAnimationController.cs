// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary>
    /// PlayerPresentation이 확정한 표현 상태(이동 속도, 바라보는 방향)를 읽어 애니메이션을
    /// 재생하는 표현 전용 컴포넌트. 새 네트워크 값을 만들지 않고, 소유, 원격, 호스트 인스턴스가
    /// 각자 자기 화면에서 독립 재생하므로 별도 동기화가 필요 없다. 판정에는 관여하지 않는다
    /// </summary>
    public class PlayerAnimationController : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary>
        /// 무기 하나가 쓰는 애니메이터. 동작을 정하는 것은 캐릭터가 아니라 무기다 -
        /// 활을 든 자세와 검을 든 자세는 서고 걷는 것부터 다르다
        /// </summary>
        [Serializable]
        public struct WeaponAnimatorBinding
        {
            public WeaponKind Weapon;

            [Tooltip("Speed, Roll, RollSpeed 파라미터를 가진 컨트롤러여야 한다")]
            public RuntimeAnimatorController Controller;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PARAM_SPEED      = "Speed";
        private const string PARAM_ROLL       = "Roll";
        private const string PARAM_ROLL_SPEED = "RollSpeed";
        private const string PARAM_ATTACK     = "Attack";

        // 구르기 클립 이름은 캐릭터마다 다르다(무료 에셋 SprintingForwardRoll, 검 팩 S_Evade ...).
        // 이름 하나를 박으면 다른 캐릭터에서 배속 계산이 조용히 실패해 구르기 판정(0.9초)과
        // 모션 길이가 어긋난 채로 보인다. 이름 규약으로 찾는다
        private static readonly string[] CLIP_ROLL_KEYWORDS = { "roll", "evade", "dodge" };

        private const string VISUAL_PIVOT_NAME = "VisualPivot";

        private const float SPEED_SMOOTH_TIME    = 0.15f;   // 초, 걷기와 달리기 사이 파라미터가 튀지 않게 한다
        private const float TURN_DEGREES_PER_SEC = 720f;
        private const float MOVE_SPEED_EPSILON   = 0.05f;   // m/s, 이 미만은 정지로 본다
        private const float MIN_SPEED_RANGE      = 0.0001f; // m/s, 걷기와 달리기 속도가 같을 때 0 나누기를 막는다

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("무기별 애니메이터. 비어 있거나 짝이 없는 무기는 캐릭터 모델이 들고 온 컨트롤러를 그대로 쓴다")]
        [SerializeField] private WeaponAnimatorBinding[] _weaponControllers = Array.Empty<WeaponAnimatorBinding>();

        private Animator           _animator;
        private Transform          _visualPivot;
        private PlayerPresentation _presentation;
        private PlayerWeapon       _weapon;

        // 무기 짝이 없을 때 되돌아갈 자리. 모델이 바뀔 때만 다시 잡는다
        private RuntimeAnimatorController _modelDefaultController;

        private float _smoothedSpeed  = 0f;
        private float _rollClipLength = 0f;

        // 공격 모션이 없는 컨트롤러에 트리거를 넣으면 공격할 때마다 경고가 쌓인다
        private bool _hasAttackParam = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _presentation = GetComponent<PlayerPresentation>();
            _weapon       = GetComponent<PlayerWeapon>();
            _visualPivot  = transform.Find(VISUAL_PIVOT_NAME);

            if (_visualPivot == null)
            {
                Debug.LogError($"[PlayerAnimationController] 자식에 {VISUAL_PIVOT_NAME}이 없어 모델을 회전시킬 수 없다", this);
            }

            RefreshAnimator();
        }

        // NetworkTransform 보간과 로컬 예측이 Update에서 루트 위치를 바꾸므로 표현은 그 뒤에 적용한다.
        // 실행 순서를 계약하지 않으면 프레임마다 보간 적용 전후 중 어느 상태를 읽는지 알 수 없다
        private void LateUpdate()
        {
            if ((_animator == null) || (_presentation == null))
            {
                return;
            }

            // 위치 차분이 아니라 명시적 표현 상태를 읽는다.
            // 재조정 보정으로 생긴 뒤, 옆 방향 변위를 이동으로 오인하지 않는다
            float smoothing = 1f - Mathf.Exp(-Time.deltaTime / SPEED_SMOOTH_TIME);
            _smoothedSpeed  = Mathf.Lerp(_smoothedSpeed, _presentation.MoveSpeed, smoothing);

            _animator.SetFloat(PARAM_SPEED, NormalizeSpeed(_smoothedSpeed));

            ApplyFacing(_presentation.FacingDirection);
        }

        // 게임플레이 방향은 VisualPivot의 Y축 회전으로만 만든다.
        // 모델 Transform을 직접 돌리면 FBX importer의 축 보정을 덮어써 캐릭터마다 다른 쪽을 본다
        private void ApplyFacing(Vector2 facing)
        {
            if ((_visualPivot == null) || (facing.sqrMagnitude <= 0f))
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
            _visualPivot.localRotation = Quaternion.RotateTowards(
                _visualPivot.localRotation, targetRotation, TURN_DEGREES_PER_SEC * Time.deltaTime);
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 구르기 모션을 한 번 재생한다. PlayerRollController의 구르기 SyncVar 훅이 호출한다 </summary>
        public void PlayRoll()
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(PARAM_ROLL_SPEED, ResolveRollSpeed());
            _animator.SetTrigger(PARAM_ROLL);
        }

        /// <summary> PlayerCharacter가 모델을 교체한 뒤 현재 활성 모델의 Animator를 다시 캐싱한다 </summary>
        public void RefreshAnimator()
        {
            Animator found = GetComponentInChildren<Animator>();

            // 모델이 실제로 바뀐 경우에만 기본 컨트롤러를 다시 잡는다.
            // 매번 잡으면 직전에 우리가 씌운 무기 컨트롤러를 기본값으로 오인한다
            if (found != _animator)
            {
                _animator               = found;
                _modelDefaultController = (found != null) ? found.runtimeAnimatorController : null;
            }

            if (_animator == null)
            {
                _rollClipLength = 0f;
                _smoothedSpeed  = 0f;
                Debug.LogWarning("[PlayerAnimationController] 활성 모델에서 Animator를 찾지 못했다", this);
                return;
            }

            _animator.applyRootMotion = false; // 이동은 Rigidbody가 결정한다, 애니메이션은 표현만 한다
            _smoothedSpeed            = 0f;

            ApplyWeaponController();
        }

        /// <summary>
        /// 공격 모션을 한 번 재생한다. PlayerAttack의 RpcAttackStarted가 모든 화면에서 호출하므로
        /// 남의 공격도 내 화면에서 보인다. 판정은 서버가 이미 끝냈고 이것은 표현만 한다
        /// </summary>
        public void PlayAttack()
        {
            if ((_animator == null) || (!_hasAttackParam))
            {
                return;
            }

            _animator.SetTrigger(PARAM_ATTACK);
        }

        /// <summary>
        /// PlayerWeapon의 SyncVar 훅이 무기가 바뀔 때마다 호출한다.
        /// 모델 교체와 무기 도착 순서가 보장되지 않아 양쪽에서 각각 다시 시도한다
        /// </summary>
        public void RefreshWeaponAnimation()
        {
            if (_animator == null)
            {
                return;
            }

            ApplyWeaponController();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 무기에 맞는 컨트롤러를 씌운다. 짝이 없으면 모델이 들고 온 것을 그대로 둔다 </summary>
        private void ApplyWeaponController()
        {
            RuntimeAnimatorController desired = _modelDefaultController;

            if ((_weapon != null) && (_weapon.HasWeapon) && (TryResolveController(_weapon.Weapon, out RuntimeAnimatorController bound)))
            {
                desired = bound;
            }

            // 같은 컨트롤러를 다시 넣으면 재생 중인 상태가 처음으로 되감긴다
            if ((desired != null) && (_animator.runtimeAnimatorController != desired))
            {
                _animator.runtimeAnimatorController = desired;
            }

            // 구르기 배속은 지금 걸린 컨트롤러의 클립 길이로 정해지므로 교체 뒤에 다시 잰다
            _rollClipLength = FindRollClipLength();
            _hasAttackParam = HasParameter(PARAM_ATTACK);
        }

        // 아직 공격 모션이 없는 무기와 캐릭터 폴백 컨트롤러가 있어 매번 확인이 필요하다
        private bool HasParameter(string parameterName)
        {
            AnimatorControllerParameter[] parameters = _animator.parameters;

            for (int i = 0; i < parameters.Length; ++i)
            {
                if (parameters[i].name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveController(WeaponKind weapon, out RuntimeAnimatorController controller)
        {
            for (int i = 0; i < _weaponControllers.Length; ++i)
            {
                if ((_weaponControllers[i].Weapon != weapon) || (_weaponControllers[i].Controller == null))
                {
                    continue;
                }

                controller = _weaponControllers[i].Controller;
                return true;
            }

            controller = null;
            return false;
        }

        /// <summary>
        /// 클립 길이를 CombatConfigSO의 구르기 동작 시간에 맞추는 재생 배속.
        /// 밸런스로 구르기 시간이 바뀌어도 클립을 다시 만들지 않아도 되고, 무적 시간과 모션이 어긋나지 않는다
        /// </summary>
        private float ResolveRollSpeed()
        {
            if ((!GameDB.IsInitialized) || (_rollClipLength <= 0f))
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

        /// <summary>
        /// 구르기 클립의 길이를 찾는다. 이름이 캐릭터마다 다르므로 정확한 일치가 아니라
        /// 키워드 포함으로 찾는다 - <b>구르기 클립 이름에는 roll, evade, dodge 중 하나가 들어가야 한다</b>.
        /// 새 캐릭터를 붙일 때 이 규약을 지키지 않으면 배속이 1로 떨어져
        /// 판정(RollTotalDuration)과 모션 길이가 어긋난 채로 보인다
        /// </summary>
        private float FindRollClipLength()
        {
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            if (controller == null)
            {
                return 0f;
            }

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; ++i)
            {
                if (clips[i] == null)
                {
                    continue;
                }

                string lowered = clips[i].name.ToLowerInvariant();
                for (int k = 0; k < CLIP_ROLL_KEYWORDS.Length; ++k)
                {
                    if (lowered.Contains(CLIP_ROLL_KEYWORDS[k]))
                    {
                        return clips[i].length;
                    }
                }
            }

            Debug.LogWarning(
                "[PlayerAnimationController] 애니메이터에서 구르기 클립을 찾지 못해 배속을 계산할 수 없다. "
                + "클립 이름에 roll, evade, dodge 중 하나가 들어가야 한다", this);
            return 0f;
        }

        /// <summary>
        /// 블렌드 트리 문턱값(0=정지, 1=걷기, 2=달리기)에 맞춰 실제 속도를 정규화한다.
        /// CombatConfigSO의 걷기, 달리기 속도가 밸런스 조정으로 바뀌어도 애니메이터 자산을 다시 만들 필요가 없다
        /// </summary>
        /// <remarks>
        /// public인 이유는 네트워크 없이 도는 캐릭터 미리보기 리그(<see cref="CharacterPreviewRig"/>)가
        /// 같은 규칙을 써야 하기 때문이다. 규칙을 복사하면 두 곳이 조용히 갈라진다
        /// </remarks>
        public static float NormalizeSpeed(float speed)
        {
            if ((!GameDB.IsInitialized) || (speed <= MOVE_SPEED_EPSILON))
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
