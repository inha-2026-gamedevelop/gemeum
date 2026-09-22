// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    /// <summary> 네트워크 없이 캐릭터의 모델, 애니메이션, 무기 부착만 확인하는 개발용 리그다 </summary>
    public class CharacterPreviewRig : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [System.Serializable]
        private struct PreviewCharacter
        {
            public CharacterKind Kind;
            public GameObject    ModelPrefab;

            [Tooltip("이 캐릭터가 기본으로 드는 무기. Q/E로 바꿀 수 있다")]
            public WeaponKind Weapon;

            [Tooltip("Player.prefab에 바인딩이 없을 때만 쓰는 대체 모델")]
            public GameObject WeaponPrefab;

            [Tooltip("무기를 붙일 본 이름. 비우면 무기를 붙이지 않는다")]
            public string HandBoneName;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("미리볼 캐릭터. 숫자키 1~9로 전환한다")]
        [SerializeField] private PreviewCharacter[] _characters = new PreviewCharacter[0];

        [Header("조작")]
        [SerializeField] private KeyCode _attackKey = KeyCode.Mouse0;
        [SerializeField] private KeyCode _rollKey   = KeyCode.Space;
        [SerializeField] private KeyCode _sprintKey = KeyCode.LeftShift;

        [Header("무기 보정 - 실제 게임과 같은 값을 쓴다")]
        [Tooltip("Player.prefab. PlayerWeaponVisual의 무기별 부착 보정을 여기서 읽고, 저장도 여기로 한다")]
        [SerializeField] private GameObject _playerPrefab;

        [Header("표시")]
        [SerializeField] private bool _showOverlay = true;

        private const string PARAM_SPEED      = "Speed";
        private const string PARAM_ROLL       = "Roll";
        private const string PARAM_ROLL_SPEED = "RollSpeed";
        private const string PARAM_ATTACK     = "Attack";

        private const float TURN_DEGREES_PER_SEC = 720f;
        private const float SPEED_SMOOTH_TIME    = 0.12f;

        private const float TUNE_POSITION_RATE = 0.4f;  // m/s
        private const float TUNE_EULER_RATE    = 90f;   // 도/s
        private const float TUNE_SCALE_RATE    = 0.5f;  // 배/s
        private const float TUNE_FAST_FACTOR   = 5f;

        private GameObject _activeModel;
        private GameObject _activeWeapon;
        private Animator   _animator;
        private int        _activeIndex = -1;

        private Vector3 _weaponPosition   = Vector3.zero;
        private Vector3 _weaponEuler      = Vector3.zero;
        private Vector3 _weaponScale      = Vector3.one;
        private bool    _hasPrefabBinding = false;
        private bool    _isTuning         = false;

        private WeaponKind _currentWeapon = WeaponKind.Sword;

        private Vector3 _tuneDelta = Vector3.zero;

        private Vector2 _facing         = Vector2.up;
        private float   _smoothedSpeed  = 0f;
        private float   _rollClipLength = 0f;
        private string  _lastMessage    = "";

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_characters.Length == 0)
            {
                Debug.LogError("[CharacterPreviewRig] 미리볼 캐릭터가 비어 있다", this);
                return;
            }

            Select(0);
        }

        private void Update()
        {
            HandleCharacterSwitch();

            if (_animator == null)
            {
                return;
            }

            HandleWeaponTuning();

            if (!_isTuning)
            {
                HandleMovement();
            }

            HandleActions();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleCharacterSwitch()
        {
            for (int i = 0; (i < _characters.Length) && (i < 9); ++i)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    Select(i);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                CycleWeapon(1);
            }
            else if (Input.GetKeyDown(KeyCode.Q))
            {
                CycleWeapon(-1);
            }
        }


        private void CycleWeapon(int direction)
        {
            if (_activeIndex < 0)
            {
                return;
            }

            var kinds = (WeaponKind[])System.Enum.GetValues(typeof(WeaponKind));
            int index = System.Array.IndexOf(kinds, _currentWeapon);
            index = (index < 0) ? 0 : ((index + direction + kinds.Length) % kinds.Length);

            _currentWeapon = kinds[index];
            ReattachWeapon();
            _lastMessage = "무기 변경: " + _currentWeapon;
        }

        private void ReattachWeapon()
        {
            if (_activeWeapon != null)
            {
                Destroy(_activeWeapon);
                _activeWeapon = null;
            }

            AttachWeapon(_characters[_activeIndex]);
        }

        private void Select(int index)
        {
            if ((index < 0) || (index >= _characters.Length) || (index == _activeIndex))
            {
                return;
            }

            if (_activeModel != null)
            {
                Destroy(_activeModel);
            }

            PreviewCharacter entry = _characters[index];
            if (entry.ModelPrefab == null)
            {
                Debug.LogError("[CharacterPreviewRig] 모델 프리팹이 비어 있다: " + entry.Kind, this);
                return;
            }

            _activeModel = Instantiate(entry.ModelPrefab, transform);
            _activeModel.transform.localPosition = Vector3.zero;
            _activeModel.transform.localRotation = Quaternion.identity;

            _animator = _activeModel.GetComponentInChildren<Animator>(true);
            if (_animator == null)
            {
                Debug.LogError("[CharacterPreviewRig] 모델에 Animator가 없다: " + entry.Kind, this);
            }

            _activeIndex    = index;
            _currentWeapon  = entry.Weapon;
            _rollClipLength = FindRollClipLength();
            _lastMessage    = "";

            AttachWeapon(entry);
            WarnMissingParameters(entry.Kind);
        }

        private void AttachWeapon(PreviewCharacter entry)
        {
            if (string.IsNullOrEmpty(entry.HandBoneName))
            {
                return;
            }

            Transform hand = FindDeep(_activeModel.transform, entry.HandBoneName);
            if (hand == null)
            {
                Debug.LogWarning(
                    "[CharacterPreviewRig] 본을 찾지 못해 무기를 붙이지 않는다: " + entry.HandBoneName, this);
                return;
            }

            GameObject source = (entry.Weapon == _currentWeapon) ? entry.WeaponPrefab : null;
            _weaponPosition   = Vector3.zero;
            _weaponEuler      = Vector3.zero;
            _weaponScale      = Vector3.one;
            _hasPrefabBinding = false;

            PlayerWeaponVisual visual = ResolveWeaponVisual();
            if (visual != null)
            {
                PlayerWeaponVisual.WeaponAttachBinding binding;
                if (visual.TryResolveBinding(entry.Kind, _currentWeapon, out binding))
                {
                    source            = (binding.ModelSource != null) ? binding.ModelSource : source;
                    _weaponPosition   = binding.LocalPosition;
                    _weaponEuler      = binding.LocalEuler;
                    _weaponScale      = ResolveScale(binding.LocalScale);
                    _hasPrefabBinding = true;
                }
            }

            if (source == null)
            {
                return;
            }

            _activeWeapon = Instantiate(source, hand);
            ApplyWeaponTransform();
            var grip = _activeModel.GetComponent<Geumeum.Art.PlayerWeaponGrip>();
            if (grip != null)
            {
                grip.ConfigureWeapon(_currentWeapon);
            }
        }

        private static Vector3 ResolveScale(Vector3 scale)
        {
            bool isUnset = ((Mathf.Approximately(scale.x, 0f))
                && (Mathf.Approximately(scale.y, 0f))
                && (Mathf.Approximately(scale.z, 0f)));

            return isUnset ? Vector3.one : scale;
        }

        private PlayerWeaponVisual ResolveWeaponVisual()
        {
            return (_playerPrefab != null) ? _playerPrefab.GetComponent<PlayerWeaponVisual>() : null;
        }

        private void ApplyWeaponTransform()
        {
            if (_activeWeapon == null)
            {
                return;
            }

            _activeWeapon.transform.localPosition = _weaponPosition;
            _activeWeapon.transform.localRotation = Quaternion.Euler(_weaponEuler);
            _activeWeapon.transform.localScale    = _weaponScale;
        }

        private static Transform FindDeep(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform found = FindDeep(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void HandleMovement()
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f)
            {
                input = input.normalized;
            }

            float walkSpeed = (GameDB.IsInitialized) ? GameDB.Combat.WalkSpeed : 2.5f;
            float runSpeed  = (GameDB.IsInitialized) ? GameDB.Combat.RunSpeed  : 5.5f;
            float target    = (Input.GetKey(_sprintKey)) ? runSpeed : walkSpeed;
            float speed     = (input.sqrMagnitude > 0.0001f) ? target : 0f;

            transform.position += new Vector3(input.x, 0f, input.y) * (speed * Time.deltaTime);

            if (input.sqrMagnitude > 0.0001f)
            {
                _facing = input.normalized;
            }

            float smoothing = 1f - Mathf.Exp(-Time.deltaTime / SPEED_SMOOTH_TIME);
            _smoothedSpeed  = Mathf.Lerp(_smoothedSpeed, speed, smoothing);
            _animator.SetFloat(PARAM_SPEED, PlayerAnimationController.NormalizeSpeed(_smoothedSpeed));

            ApplyFacing();
        }

        private void ApplyFacing()
        {
            Quaternion target = Quaternion.LookRotation(new Vector3(_facing.x, 0f, _facing.y), Vector3.up);
            _activeModel.transform.localRotation = Quaternion.RotateTowards(
                _activeModel.transform.localRotation, target, TURN_DEGREES_PER_SEC * Time.deltaTime);
        }

        private void HandleActions()
        {
            if (Input.GetKeyDown(_attackKey))
            {
                if (HasParameter(PARAM_ATTACK))
                {
                    _animator.SetTrigger(PARAM_ATTACK);
                    _lastMessage = "Attack";
                }
                else
                {
                    _lastMessage = "이 컨트롤러에 Attack 파라미터가 없다";
                }
            }

            if (!Input.GetKeyDown(_rollKey))
            {
                return;
            }

            if (!HasParameter(PARAM_ROLL))
            {
                _lastMessage = "이 컨트롤러에 Roll 파라미터가 없다";
                return;
            }

            float rollSpeed = ResolveRollSpeed();
            if (HasParameter(PARAM_ROLL_SPEED))
            {
                _animator.SetFloat(PARAM_ROLL_SPEED, rollSpeed);
            }

            _animator.SetTrigger(PARAM_ROLL);
            _lastMessage = "Roll (배속 " + rollSpeed.ToString("F2") + ")";
        }

        /****************************************
        *            무기 보정 조정
        ****************************************/

        private void HandleWeaponTuning()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                _isTuning = !_isTuning;
                _lastMessage = (_isTuning) ? "무기 보정 조정 시작" : "무기 보정 조정 종료";
            }

            if ((!_isTuning) || (_activeWeapon == null))
            {
                return;
            }

            float factor = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                ? TUNE_FAST_FACTOR
                : 1f;
            bool isRotating = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            _tuneDelta = Vector3.zero;
            if (Input.GetKey(KeyCode.J))
            {
                _tuneDelta.x -= 1f;
            }

            if (Input.GetKey(KeyCode.L))
            {
                _tuneDelta.x += 1f;
            }

            if (Input.GetKey(KeyCode.I))
            {
                _tuneDelta.z += 1f;
            }

            if (Input.GetKey(KeyCode.K))
            {
                _tuneDelta.z -= 1f;
            }

            if (Input.GetKey(KeyCode.U))
            {
                _tuneDelta.y += 1f;
            }

            if (Input.GetKey(KeyCode.O))
            {
                _tuneDelta.y -= 1f;
            }

            float dt = Time.unscaledDeltaTime;

            if (_tuneDelta != Vector3.zero)
            {
                if (isRotating)
                {
                    Vector3 rotationDelta = new Vector3(_tuneDelta.z, _tuneDelta.x, _tuneDelta.y);
                    _weaponEuler = NormalizeEuler(_weaponEuler + (rotationDelta * (TUNE_EULER_RATE * factor * dt)));
                }
                else
                {
                    _weaponPosition += _tuneDelta * (TUNE_POSITION_RATE * factor * dt);
                }
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                _weaponEuler = new Vector3(
                    Mathf.Round(_weaponEuler.x / 15f) * 15f,
                    Mathf.Round(_weaponEuler.y / 15f) * 15f,
                    Mathf.Round(_weaponEuler.z / 15f) * 15f);
                _lastMessage = "회전을 15도 단위로 맞췄다";
            }

            float scaleDelta = 0f;
            if (Input.GetKey(KeyCode.RightBracket))
            {
                scaleDelta += 1f;
            }

            if (Input.GetKey(KeyCode.LeftBracket))
            {
                scaleDelta -= 1f;
            }

            if (!Mathf.Approximately(scaleDelta, 0f))
            {
                _weaponScale += Vector3.one * (scaleDelta * TUNE_SCALE_RATE * factor * dt);
                _weaponScale  = Vector3.Max(_weaponScale, Vector3.one * 0.01f);
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                _weaponPosition = Vector3.zero;
                _weaponEuler    = Vector3.zero;
                _weaponScale    = Vector3.one;
                _lastMessage    = "보정 초기화";
            }

            ApplyWeaponTransform();

            if (Input.GetKeyDown(KeyCode.F5))
            {
                SaveBindingToPrefab();
            }
        }

        private void SaveBindingToPrefab()
        {
#if UNITY_EDITOR
            if ((_playerPrefab == null) || (_activeIndex < 0))
            {
                _lastMessage = "저장 실패: Player.prefab이 연결되어 있지 않다";
                return;
            }

            PlayerWeaponVisual visual = ResolveWeaponVisual();
            if (visual == null)
            {
                _lastMessage = "저장 실패: Player.prefab에 PlayerWeaponVisual이 없다";
                return;
            }

            CharacterKind character = _characters[_activeIndex].Kind;
            WeaponKind    weapon    = _currentWeapon;
            var serialized          = new UnityEditor.SerializedObject(visual);
            var bindings            = serialized.FindProperty("_bindings");

            UnityEditor.SerializedProperty target = null;
            for (int i = 0; i < bindings.arraySize; ++i)
            {
                var element = bindings.GetArrayElementAtIndex(i);
                if ((element.FindPropertyRelative("Weapon").enumValueIndex == (int)weapon)
                    && (element.FindPropertyRelative("Character").enumValueIndex == (int)character))
                {
                    target = element;
                    break;
                }
            }

            if (target == null)
            {
                UnityEngine.Object modelSource = null;
                for (int i = 0; i < bindings.arraySize; ++i)
                {
                    var element = bindings.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("Weapon").enumValueIndex != (int)weapon)
                    {
                        continue;
                    }

                    modelSource = element.FindPropertyRelative("ModelSource").objectReferenceValue;
                    break;
                }

                bindings.arraySize += 1;
                target = bindings.GetArrayElementAtIndex(bindings.arraySize - 1);
                target.FindPropertyRelative("Character").enumValueIndex = (int)character;
                target.FindPropertyRelative("Weapon").enumValueIndex    = (int)weapon;
                target.FindPropertyRelative("ModelSource").objectReferenceValue = modelSource;
            }

            target.FindPropertyRelative("LocalPosition").vector3Value = _weaponPosition;
            target.FindPropertyRelative("LocalEuler").vector3Value    = _weaponEuler;
            target.FindPropertyRelative("LocalScale").vector3Value    = _weaponScale;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.SetDirty(visual);
            UnityEditor.AssetDatabase.SaveAssets();

            _lastMessage = "(" + character + ", " + weapon + ") 보정을 Player.prefab에 저장했다";
#else
            _lastMessage = "저장은 에디터에서만 가능하다";
#endif
        }

        private float ResolveRollSpeed()
        {
            if ((!GameDB.IsInitialized) || (_rollClipLength <= 0f))
            {
                return 1f;
            }

            float duration = GameDB.Combat.RollTotalDuration;
            return (duration > 0f) ? (_rollClipLength / duration) : 1f;
        }

        private float FindRollClipLength()
        {
            RuntimeAnimatorController controller = (_animator != null) ? _animator.runtimeAnimatorController : null;
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
                if ((lowered.Contains("roll")) || (lowered.Contains("evade")) || (lowered.Contains("dodge")))
                {
                    return clips[i].length;
                }
            }

            return 0f;
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

        private void WarnMissingParameters(CharacterKind kind)
        {
            if (_animator == null)
            {
                return;
            }

            string missing = "";
            if (!HasParameter(PARAM_SPEED))
            {
                missing += PARAM_SPEED + " ";
            }

            if (!HasParameter(PARAM_ROLL))
            {
                missing += PARAM_ROLL + " ";
            }

            if (!HasParameter(PARAM_ATTACK))
            {
                missing += PARAM_ATTACK + " ";
            }

            if (missing.Length > 0)
            {
                Debug.LogWarning(
                    "[CharacterPreviewRig] 컨트롤러에 없는 파라미터 (" + kind + "): " + missing.Trim(), this);
            }
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
        }

        private static string Format(Vector3 v)
        {
            return "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")";
        }

        private void OnGUI()
        {
            if ((!_showOverlay) || (_activeIndex < 0))
            {
                return;
            }

            string names = "";
            for (int i = 0; i < _characters.Length; ++i)
            {
                names += (i + 1) + ":" + _characters[i].Kind + ((i == _activeIndex) ? "*" : "") + "   ";
            }

            string controllerName = ((_animator != null) && (_animator.runtimeAnimatorController != null))
                ? _animator.runtimeAnimatorController.name
                : "없음";

            float speedParam = (_animator != null) ? _animator.GetFloat(PARAM_SPEED) : 0f;

            string body =
                "캐릭터 전환   " + names + "\n"
                + "이동 WASD   달리기 " + _sprintKey + "   구르기 " + _rollKey + "   공격 " + _attackKey + "\n"
                + "컨트롤러 " + controllerName + "\n"
                + "Speed " + speedParam.ToString("F2") + "    구르기 클립 " + _rollClipLength.ToString("F2") + "초\n"
                + "T = 무기 보정 조정" + ((_isTuning) ? "  [조정 중]" : "") + "\n"
                + _lastMessage;

            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize  = 13;
            style.wordWrap  = false;
            style.padding   = new RectOffset(10, 10, 8, 8);

            float infoWidth  = 560f;
            float infoHeight = style.CalcHeight(new GUIContent(body), infoWidth);
            GUI.Box(new Rect(10f, 10f, infoWidth, infoHeight), body, style);

            if (!_isTuning)
            {
                return;
            }

            string source = (_hasPrefabBinding) ? "Player.prefab 바인딩" : "바인딩 없음 (대체 모델)";
            string tune =
                "[ 무기 보정 ("
                + ((_activeIndex >= 0)
                    ? (_characters[_activeIndex].Kind + ", " + _currentWeapon)
                    : "-")
                + ") ]\n"
                + "소스  " + source + "\n"
                + "Position  " + Format(_weaponPosition) + "\n"
                + "Euler     " + Format(_weaponEuler) + "\n"
                + "Scale     " + Format(_weaponScale) + "\n"
                + "입력 " + Format(_tuneDelta) + (( _tuneDelta == Vector3.zero) ? "   (키 입력 없음)" : "   <- 들어오는 중")
                + "\n\n"
                + "이동   J / L 좌우    I / K 앞뒤    U / O 위아래\n"
                + "회전   Ctrl + 같은 키\n"
                + "         J / L 좌우로 돌리기 (Y)\n"
                + "         I / K 앞뒤로 눕히기 (X)\n"
                + "         U / O 자루 비틀기 (Z)\n"
                + "크기   [ / ]\n"
                + "Shift 5배   G 15도 스냅   R 초기화\n"
                + "F5 = Player.prefab에 저장";

            float tuneWidth  = 420f;
            float tuneHeight = style.CalcHeight(new GUIContent(tune), tuneWidth);
            GUI.Box(new Rect(10f, 10f + infoHeight + 8f, tuneWidth, tuneHeight), tune, style);
        }
    }
}
