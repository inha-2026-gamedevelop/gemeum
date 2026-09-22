// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Art;

namespace Geumeum.Combat
{
    /// <summary> 무기 종류와 캐릭터 모델의 오른손 뼈를 보고 무기 모델을 붙인다 </summary>
    public class PlayerWeaponVisual : MonoBehaviour, IWeaponBladeSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string RIGHT_HAND_BONE_NAME = "RightHand";

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 무기 하나의 모델과 손 뼈 기준 부착 보정 </summary>
        [Serializable]
        public struct WeaponAttachBinding
        {
            public CharacterKind Character;
            public WeaponKind    Weapon;
            public GameObject    ModelSource;

            [Tooltip("손 뼈 기준 위치 보정. 0이면 뼈 원점에 놓여 몸통에 파묻힐 수 있다")]
            public Vector3 LocalPosition;

            [Tooltip("손 뼈 기준 회전 보정(도)")]
            public Vector3 LocalEuler;

            [Tooltip("손 크기에 맞춘 배율. 0이면 1로 본다")]
            public Vector3 LocalScale;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private WeaponAttachBinding[] _bindings = Array.Empty<WeaponAttachBinding>();

        private PlayerWeapon     _weapon;
        private PlayerCharacter _character;

        private GameObject _spawnedModel;
        private WeaponKind _spawnedWeapon;
        private bool       _hasSpawnedModel = false;

        private GameObject _attachedCharacterModel;
        private PlayerWeaponGrip _grip;

        // 붙인 모델에서 한 번 찾아 둔 칼날 양 끝. 매 프레임은 변환만 한다
        private Transform _bladeTransform;
        private Vector3   _bladeHiltLocal;
        private Vector3   _bladeTipLocal;
        private bool      _hasBlade;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weapon     = GetComponent<PlayerWeapon>();
            _character = GetComponent<PlayerCharacter>();
        }

        private void OnEnable()
        {
            Refresh();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 무기와 캐릭터 값이 바뀔 때마다 호출한다 </summary>
        public void Refresh()
        {
            if ((_weapon == null) || (!_weapon.HasWeapon) || (_character == null) || (_character.ActiveModel == null))
            {
                DetachCurrentModel();
                return;
            }

            if ((_hasSpawnedModel) &&
                (_spawnedWeapon == _weapon.Weapon) &&
                (_spawnedModel != null) &&
                (_attachedCharacterModel == _character.ActiveModel))
            {
                if (_grip != null)
                {
                    _grip.ConfigureWeapon(_spawnedWeapon);
                }
                return;
            }

            DetachCurrentModel();

            if (!TryResolveBinding(_character.Character, _weapon.Weapon, out WeaponAttachBinding binding))
            {
                Debug.LogWarning(
                    $"[PlayerWeaponVisual] ({_character.Character}, {_weapon.Weapon}) 모델이 연결되지 않아 손이 빈 채로 둔다", this);
                return;
            }

            Transform socket = FindDeepChild(_character.ActiveModel.transform, RIGHT_HAND_BONE_NAME);
            if (socket == null)
            {
                Debug.LogWarning($"[PlayerWeaponVisual] {_character.ActiveModel.name}에서 {RIGHT_HAND_BONE_NAME} 뼈를 찾지 못했다", this);
                return;
            }

            _spawnedModel = Instantiate(binding.ModelSource, socket);

            _spawnedModel.transform.localPosition = binding.LocalPosition;
            _spawnedModel.transform.localRotation = Quaternion.Euler(binding.LocalEuler);
            _spawnedModel.transform.localScale    = ResolveScale(binding.LocalScale);

            _spawnedWeapon           = _weapon.Weapon;
            _hasSpawnedModel         = true;
            _attachedCharacterModel = _character.ActiveModel;
            _grip = _attachedCharacterModel.GetComponent<PlayerWeaponGrip>();
            if (_grip != null)
            {
                _grip.ConfigureWeapon(_spawnedWeapon);
            }

            _hasBlade = TryResolveBlade(_spawnedModel, socket.position,
                out _bladeTransform, out _bladeHiltLocal, out _bladeTipLocal);

            Debug.Log($"[PlayerWeaponVisual] {_spawnedWeapon}를 {_character.ActiveModel.name}의 {socket.name}에 붙였다", this);
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGetBladeSegment(out Vector3 hiltWorld, out Vector3 tipWorld)
        {
            if ((!_hasBlade) || (_bladeTransform == null))
            {
                hiltWorld = default;
                tipWorld  = default;
                return false;
            }

            hiltWorld = _bladeTransform.TransformPoint(_bladeHiltLocal);
            tipWorld  = _bladeTransform.TransformPoint(_bladeTipLocal);
            return true;
        }

        /// <summary>
        /// 메시 경계의 가장 긴 축을 칼날로 본다. 양 끝 중 손에 가까운 쪽이 손잡이, 먼 쪽이 칼끝이다
        /// 모델마다 축 방향이 달라도 부착 보정값을 따로 적지 않게 하려는 것이다
        /// </summary>
        public static bool TryResolveBladeEnds(Bounds meshBounds, Vector3 gripLocal,
            out Vector3 hiltLocal, out Vector3 tipLocal)
        {
            Vector3 size = meshBounds.size;
            Vector3 axis = (size.x >= size.y)
                ? ((size.x >= size.z) ? Vector3.right : Vector3.forward)
                : ((size.y >= size.z) ? Vector3.up : Vector3.forward);

            float halfLength = Vector3.Dot(meshBounds.extents, axis);
            if (halfLength <= Mathf.Epsilon)
            {
                hiltLocal = default;
                tipLocal  = default;
                return false;
            }

            Vector3 endA = meshBounds.center + (axis * halfLength);
            Vector3 endB = meshBounds.center - (axis * halfLength);
            bool isATip  = (endA - gripLocal).sqrMagnitude >= (endB - gripLocal).sqrMagnitude;

            tipLocal  = isATip ? endA : endB;
            hiltLocal = isATip ? endB : endA;
            return true;
        }

        /// <summary> 무기 종류에 해당하는 부착 보정을 돌려준다 </summary>
        public bool TryResolveBinding(CharacterKind character, WeaponKind weapon, out WeaponAttachBinding binding)
        {
            for (int i = 0; i < _bindings.Length; ++i)
            {
                if ((_bindings[i].ModelSource == null)
                    || (_bindings[i].Weapon != weapon)
                    || (_bindings[i].Character != character))
                {
                    continue;
                }

                binding = _bindings[i];
                return true;
            }

            for (int i = 0; i < _bindings.Length; ++i)
            {
                if ((_bindings[i].ModelSource == null) || (_bindings[i].Weapon != weapon))
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[PlayerWeaponVisual] ({character}, {weapon}) 보정이 없어 {_bindings[i].Character} 값으로 대신 붙였다. "
                    + "손 뼈 방향이 캐릭터마다 달라 무기가 틀어져 보인다", this);

                binding = _bindings[i];
                return true;
            }

            binding = default;
            return false;
        }

        private static Vector3 ResolveScale(Vector3 scale)
        {
            bool isUnset = ((Mathf.Approximately(scale.x, 0f))
                && (Mathf.Approximately(scale.y, 0f))
                && (Mathf.Approximately(scale.z, 0f)));

            return isUnset ? Vector3.one : scale;
        }

        private static bool TryResolveBlade(GameObject model, Vector3 gripWorld,
            out Transform bladeTransform, out Vector3 hiltLocal, out Vector3 tipLocal)
        {
            bladeTransform = null;
            hiltLocal      = default;
            tipLocal       = default;

            // 부착할 때 한 번만 찾는다. 장식 메시가 섞여 있어도 가장 큰 메시가 칼날이다
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter   largest = null;
            float        largestSize = 0f;
            for (int i = 0; i < filters.Length; ++i)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                float meshSize = mesh.bounds.size.sqrMagnitude;
                if (meshSize > largestSize)
                {
                    largest     = filters[i];
                    largestSize = meshSize;
                }
            }

            if (largest == null)
            {
                return false;
            }

            Vector3 gripLocal = largest.transform.InverseTransformPoint(gripWorld);
            if (!TryResolveBladeEnds(largest.sharedMesh.bounds, gripLocal, out hiltLocal, out tipLocal))
            {
                return false;
            }

            bladeTransform = largest.transform;
            return true;
        }

        private void DetachCurrentModel()
        {
            if (_grip != null)
            {
                _grip.ClearGrip();
                _grip = null;
            }
            if (_spawnedModel != null)
            {
                Destroy(_spawnedModel);
            }

            _spawnedModel            = null;
            _hasSpawnedModel         = false;
            _attachedCharacterModel = null;
            _bladeTransform          = null;
            _hasBlade                = false;
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform found = FindDeepChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
