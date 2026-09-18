// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> PlayerPresentation이 확정한 표현 상태를 읽어 애니메이션을 재생한다 </summary>
    public class PlayerAnimationController : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 무기 하나가 쓰는 애니메이터 </summary>
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

        private const string PARAM_SPEED       = "Speed";
        private const string PARAM_ROLL        = "Roll";
        private const string PARAM_ROLL_SPEED  = "RollSpeed";
        private const string PARAM_ATTACK      = "Attack";
        private const string PARAM_COMBO_STAGE = "ComboStage";
        private const string PARAM_PARRY       = "Parry";
        private const string PARAM_HEAL        = "Heal";

        // 락온 스트레이프용 2D Freeform Directional 파라미터
        private const string PARAM_MOVE_X    = "MoveX";
        private const string PARAM_MOVE_Y    = "MoveY";
        private const string PARAM_IS_LOCK_ON = "IsLockOn";

        private const string PARAM_IS_DEAD = "IsDead";

        private static readonly string[] CLIP_ROLL_KEYWORDS = { "roll", "evade", "dodge" };

        private const string VISUAL_PIVOT_NAME = "VisualPivot";

        private const float SPEED_SMOOTH_TIME    = 0.15f;   // 초, 걷기와 달리기 사이 파라미터가 튀지 않게 한다
        private const float TURN_DEGREES_PER_SEC = 720f;    // 비락온 회전 속도, 락온 중 값은 CombatConfigSO가 갖는다
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
        private PlayerStatus       _status;

        private RuntimeAnimatorController _modelDefaultController;

        private float   _smoothedSpeed  = 0f;
        private Vector2 _smoothedMove   = Vector2.zero;
        private float   _rollClipLength = 0f;

        private bool _hasAttackParam     = false;
        private bool _hasComboStageParam = false;
        private bool _hasStrafeParams    = false;
        private bool _hasDeathParam      = false;
        private bool _hasParryParam      = false;
        private bool _hasHealParam       = false;

        private IPlayerActionCueSink _actionCueSink;
        private PlayerActionCueKind  _currentCueKind;
        private uint                 _currentCueExecutionId;
        private double               _currentCueEndGameplayTime;
        private float                _pendingCueProgress;
        private bool                 _hasPendingCueSeek;
        private AnimatorHitStopState _hitStop;
        private bool                 _isRunPauseHoldingAnimator;
        private float                _prePauseAnimatorSpeed = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _presentation = GetComponent<PlayerPresentation>();
            _weapon       = GetComponent<PlayerWeapon>();
            _status       = GetComponent<PlayerStatus>();
            _actionCueSink = GetComponent<IPlayerActionCueSink>();
            _visualPivot  = transform.Find(VISUAL_PIVOT_NAME);

            if (_visualPivot == null)
            {
                Debug.LogError($"[PlayerAnimationController] 자식에 {VISUAL_PIVOT_NAME}이 없어 모델을 회전시킬 수 없다", this);
            }

            RefreshAnimator();
        }

        private void LateUpdate()
        {
            UpdateAnimatorPause();
            UpdateHitStop();

            if ((_currentCueExecutionId != 0u) &&
                (RunTimeLocator.Current.GameplayElapsedSeconds >= _currentCueEndGameplayTime))
            {
                ClearCurrentCue();
            }

            if (_animator == null)
            {
                return;
            }

            if (_hasPendingCueSeek)
            {
                _hasPendingCueSeek = false;
                _animator.Play(0, 0, _pendingCueProgress);
            }

            if (_presentation == null)
            {
                return;
            }

            float smoothing = 1f - Mathf.Exp(-Time.deltaTime / SPEED_SMOOTH_TIME);
            _smoothedSpeed  = Mathf.Lerp(_smoothedSpeed, _presentation.MoveSpeed, smoothing);

            _animator.SetFloat(PARAM_SPEED, NormalizeSpeed(_smoothedSpeed));

            bool isLockedOn = _presentation.IsLockedOn;

            ApplyDeath();
            ApplyStrafe(isLockedOn, smoothing);
            ApplyFacing(_presentation.FacingDirection, isLockedOn);
        }

        private void OnDisable()
        {
            RestoreHitStop();
            RestoreAnimatorPause();
            ClearCurrentCue();
        }

        /// <summary> 쓰러진 동안 사망 자세를 유지한다 </summary>
        private void ApplyDeath()
        {
            if ((!_hasDeathParam) || (_status == null))
            {
                return;
            }

            _animator.SetBool(PARAM_IS_DEAD, _status.IsDead);
        }

        /// <summary> 이동 방향을 바라보는 방향 기준으로 풀어 2D 블렌드에 넣는다 </summary>
        private void ApplyStrafe(bool isLockedOn, float smoothing)
        {
            if (!_hasStrafeParams)
            {
                return;
            }

            Vector2 target = Vector2.zero;

            if (isLockedOn)
            {
                Vector2 facing = _presentation.FacingDirection;
                Vector2 move   = _presentation.MoveDirection;

                if ((facing.sqrMagnitude > 0f) && (move.sqrMagnitude > 0f))
                {
                    Vector2 right     = new Vector2(facing.y, -facing.x);
                    float   magnitude = Mathf.Clamp01(NormalizeSpeed(_smoothedSpeed));

                    target = new Vector2(Vector2.Dot(move, right), Vector2.Dot(move, facing)) * magnitude;
                }
            }

            _smoothedMove = Vector2.Lerp(_smoothedMove, target, smoothing);

            _animator.SetFloat(PARAM_MOVE_X, _smoothedMove.x);
            _animator.SetFloat(PARAM_MOVE_Y, _smoothedMove.y);
            _animator.SetBool(PARAM_IS_LOCK_ON, isLockedOn);
        }

        private void ApplyFacing(Vector2 facing, bool isLockedOn)
        {
            if ((_visualPivot == null) || (facing.sqrMagnitude <= 0f))
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
            _visualPivot.localRotation = Quaternion.RotateTowards(
                _visualPivot.localRotation, targetRotation, ResolveTurnSpeed(isLockedOn) * Time.deltaTime);
        }

        private static float ResolveTurnSpeed(bool isLockedOn)
        {
            if ((!isLockedOn) || (!GameDB.IsInitialized))
            {
                return TURN_DEGREES_PER_SEC;
            }

            return GameDB.Combat.LockOnTurnDegreesPerSecond;
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

            if (found != _animator)
            {
                RestoreHitStop();
                RestoreAnimatorPause();
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

            _animator.applyRootMotion = false;
            _smoothedSpeed            = 0f;

            ApplyWeaponController();
        }

        /// <summary> 공격 모션을 한 번 재생한다 </summary>
        public void PlayAttack(int comboStage)
        {
            if ((_animator == null) || (!_hasAttackParam))
            {
                return;
            }

            if (_hasComboStageParam)
            {
                _animator.SetInteger(PARAM_COMBO_STAGE, Mathf.Clamp(comboStage, 1, 3));
            }

            _animator.SetTrigger(PARAM_ATTACK);
        }

        /// <summary> 패링 모션을 한 번 재생한다 </summary>
        public void PlayParry()
        {
            if ((_animator == null) || (!_hasParryParam))
            {
                return;
            }

            _animator.SetTrigger(PARAM_PARRY);
        }

        public void PlayHeal()
        {
            if ((_animator == null) || (!_hasHealParam))
            {
                return;
            }

            _animator.SetTrigger(PARAM_HEAL);
        }

        /// <summary> 서버가 확정한 공격 동작이 아직 진행 중인가. 소유 클라의 이동 잠금이 읽는다 </summary>
        public bool IsAttackCueActive
        {
            get
            {
                return (_currentCueKind == PlayerActionCueKind.Attack)
                    && (_currentCueExecutionId != 0u)
                    && (RunTimeLocator.Current.GameplayElapsedSeconds < _currentCueEndGameplayTime);
            }
        }

        /// <summary>Applies an authoritative action cue, including late-join progress.</summary>
        public void PlayActionCue(in PlayerActionCue cue)
        {
            double gameplayTime = RunTimeLocator.Current.GameplayElapsedSeconds;
            if (PlayerActionCueRules.ResolveStart(in cue, gameplayTime,
                    _currentCueKind, _currentCueExecutionId,
                    out float normalizedProgress) != PlayerActionCueDecision.Start)
            {
                return;
            }

            _currentCueKind = cue.Kind;
            _currentCueExecutionId = cue.ExecutionId;
            _currentCueEndGameplayTime = cue.EndGameplayTime;

            switch (cue.Kind)
            {
                case PlayerActionCueKind.Attack:
                    PlayAttack(cue.ComboStage);
                    break;

                case PlayerActionCueKind.Heal:
                    PlayHeal();
                    break;

                case PlayerActionCueKind.Parry:
                    PlayParry();
                    break;
            }

            if (normalizedProgress > 0f)
            {
                _pendingCueProgress = normalizedProgress;
                _hasPendingCueSeek = true;
            }

            _actionCueSink?.PlayActionCue(in cue, normalizedProgress);
        }

        /// <summary>Only the exact action instance may cancel its presentation.</summary>
        public void CancelActionCue(PlayerActionCueKind kind, uint executionId)
        {
            if (!PlayerActionCueRules.CanCancel(kind, executionId,
                    _currentCueKind, _currentCueExecutionId))
            {
                return;
            }

            if (_animator != null)
            {
                if (_hasAttackParam)
                {
                    _animator.ResetTrigger(PARAM_ATTACK);
                }

                if (_hasHealParam)
                {
                    _animator.ResetTrigger(PARAM_HEAL);
                }

                if (_hasParryParam)
                {
                    _animator.ResetTrigger(PARAM_PARRY);
                }
            }

            _actionCueSink?.CancelActionCue(kind, executionId);
            ClearCurrentCue();
        }

        public void PlayActionImpact(PlayerActionCueKind kind, uint executionId,
            bool successful, bool critical, double impactGameplayTime)
        {
            _actionCueSink?.PlayActionImpact(kind, executionId, successful,
                critical, impactGameplayTime);
        }

        public void PlayMarkCue(uint executionId, Vector3 worldPosition,
            double appliedGameplayTime)
        {
            _actionCueSink?.PlayMarkCue(executionId, worldPosition,
                appliedGameplayTime);
        }

        /// <summary>Stops only this character animator and preserves its prior speed.</summary>
        public void PlayHitStop(float duration)
        {
            if ((_animator == null) || (!IsFinite(duration)) || (duration <= 0f))
            {
                return;
            }

            float currentSpeed = _isRunPauseHoldingAnimator
                ? _prePauseAnimatorSpeed
                : _animator.speed;
            _hitStop.Begin(currentSpeed, duration);
            _animator.speed = 0f;
        }

        /// <summary> 무기가 바뀔 때마다 호출한다 </summary>
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

            if ((desired != null) && (_animator.runtimeAnimatorController != desired))
            {
                _animator.runtimeAnimatorController = desired;
            }

            _rollClipLength     = FindRollClipLength();
            _hasAttackParam     = HasParameter(PARAM_ATTACK);
            _hasComboStageParam = HasParameter(PARAM_COMBO_STAGE);

            _hasStrafeParams = (HasParameter(PARAM_MOVE_X)) && (HasParameter(PARAM_MOVE_Y)) && (HasParameter(PARAM_IS_LOCK_ON));
            _hasDeathParam   = HasParameter(PARAM_IS_DEAD);
            _hasParryParam   = HasParameter(PARAM_PARRY);
            _hasHealParam    = HasParameter(PARAM_HEAL);
            _smoothedMove    = Vector2.zero;
        }

        private void UpdateHitStop()
        {
            if ((!_hitStop.IsActive) || (_animator == null))
            {
                return;
            }

            if (_hitStop.Tick(Time.unscaledDeltaTime, RunTimeLocator.Current.IsPaused,
                    out float restoreSpeed))
            {
                _animator.speed = restoreSpeed;
            }
        }

        private void UpdateAnimatorPause()
        {
            if (_animator == null)
            {
                return;
            }

            bool isPaused = RunTimeLocator.Current.IsPaused;
            if (isPaused && !_isRunPauseHoldingAnimator)
            {
                _prePauseAnimatorSpeed = _animator.speed;
                _isRunPauseHoldingAnimator = true;
                _animator.speed = 0f;
            }
            else if (!isPaused && _isRunPauseHoldingAnimator)
            {
                _isRunPauseHoldingAnimator = false;
                _animator.speed = _hitStop.IsActive
                    ? 0f
                    : _prePauseAnimatorSpeed;
            }
        }

        private void RestoreAnimatorPause()
        {
            if (!_isRunPauseHoldingAnimator)
            {
                return;
            }

            if (_animator != null)
            {
                _animator.speed = _hitStop.IsActive
                    ? 0f
                    : _prePauseAnimatorSpeed;
            }

            _isRunPauseHoldingAnimator = false;
        }

        private void RestoreHitStop()
        {
            if ((_animator != null) && _hitStop.Cancel(out float restoreSpeed))
            {
                _animator.speed = restoreSpeed;
            }
        }

        private void ClearCurrentCue()
        {
            _currentCueKind = PlayerActionCueKind.None;
            _currentCueExecutionId = 0u;
            _currentCueEndGameplayTime = 0d;
            _pendingCueProgress = 0f;
            _hasPendingCueSeek = false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

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

        /// <summary> 클립 길이를 CombatConfigSO의 구르기 동작 시간에 맞추는 재생 배속 </summary>
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

        /// <summary> 구르기 클립의 길이를 키워드 포함으로 찾는다 </summary>
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

        /// <summary> 블렌드 트리 문턱값(0=정지, 1=걷기, 2=달리기)에 맞춰 실제 속도를 정규화한다 </summary>
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

    /// <summary>Pure state for overlapping, pause-aware local animator hit stop.</summary>
    public struct AnimatorHitStopState
    {
        private float _remainingSeconds;
        private float _restoreSpeed;

        public bool IsActive { get; private set; }
        public float RemainingSeconds => _remainingSeconds;

        public void Begin(float currentSpeed, float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || (duration <= 0f))
            {
                return;
            }

            if (!IsActive)
            {
                _restoreSpeed = currentSpeed;
                IsActive = true;
            }

            _remainingSeconds = Mathf.Max(_remainingSeconds, duration);
        }

        public bool Tick(float unscaledDeltaTime, bool isRunPaused,
            out float restoreSpeed)
        {
            restoreSpeed = _restoreSpeed;
            if (!IsActive || isRunPaused || float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) || (unscaledDeltaTime <= 0f))
            {
                return false;
            }

            _remainingSeconds = Mathf.Max(0f, _remainingSeconds - unscaledDeltaTime);
            if (_remainingSeconds > 0f)
            {
                return false;
            }

            IsActive = false;
            return true;
        }

        public bool Cancel(out float restoreSpeed)
        {
            restoreSpeed = _restoreSpeed;
            if (!IsActive)
            {
                return false;
            }

            IsActive = false;
            _remainingSeconds = 0f;
            return true;
        }
    }
}
