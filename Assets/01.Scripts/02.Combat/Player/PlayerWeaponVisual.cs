// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 무기 종류와 캐릭터 모델의 오른손 뼈를 보고 무기 모델을 붙인다 </summary>
    public class PlayerWeaponVisual : MonoBehaviour
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

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weapon     = GetComponent<PlayerWeapon>();
            _character = GetComponent<PlayerCharacter>();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 무기와 캐릭터 값이 바뀔 때마다 호출한다 </summary>
        public void Refresh()
        {
            if ((_weapon == null) || (!_weapon.HasWeapon) || (_character == null) || (_character.ActiveModel == null))
            {
                return;
            }

            if ((_hasSpawnedModel) &&
                (_spawnedWeapon == _weapon.Weapon) &&
                (_spawnedModel != null) &&
                (_attachedCharacterModel == _character.ActiveModel))
            {
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

            Debug.Log($"[PlayerWeaponVisual] {_spawnedWeapon}를 {_character.ActiveModel.name}의 {socket.name}에 붙였다", this);
        }

        /****************************************
        *                Methods
        ****************************************/

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

        private void DetachCurrentModel()
        {
            if (_spawnedModel != null)
            {
                Destroy(_spawnedModel);
            }

            _spawnedModel            = null;
            _hasSpawnedModel         = false;
            _attachedCharacterModel = null;
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
