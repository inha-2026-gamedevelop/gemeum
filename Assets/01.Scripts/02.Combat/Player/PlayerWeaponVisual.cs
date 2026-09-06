// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// PlayerWeapon의 무기 종류와 PlayerCharacter가 켠 모델의 오른손 뼈를 보고 무기 모델을 붙인다.
    /// 표현 전용이며 판정에는 관여하지 않는다
    /// </summary>
    public class PlayerWeaponVisual : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string RIGHT_HAND_BONE_NAME = "RightHand";

        /****************************************
        *                Types
        ****************************************/

        /// <summary>
        /// 무기 하나의 모델과 손 뼈 기준 부착 보정.
        /// 키가 (캐릭터, 무기) 쌍인 이유 - 손 뼈의 방향이 캐릭터마다 다르다.
        /// 2026-09-04 실측: 해부학 기준으로 차사 대비 신궁 34.4도, 무녀 51.8도 어긋나 있다.
        /// 무기만으로 키를 잡으면 같은 무기를 다른 캐릭터가 들 때 그만큼 틀어진다
        /// </summary>
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

        // 무기는 활성 모델의 손 뼈에 자식으로 붙는다. 캐릭터가 바뀌면 이전 모델이 꺼지면서
        // 무기도 같이 안 보이게 되므로, 어느 모델에 붙였는지 기억해 두고 바뀌면 다시 붙인다
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

        /// <summary> PlayerWeapon, PlayerCharacter의 SyncVar 훅이 값이 바뀔 때마다 호출한다. 순서는 상관없다 </summary>
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
                return; // 이미 같은 무기가 지금 켜져 있는 모델에 붙어 있다
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

            // 손 뼈 원점에 그대로 놓으면 자루 방향과 크기가 맞지 않아 몸통을 뚫는다
            _spawnedModel.transform.localPosition = binding.LocalPosition;
            _spawnedModel.transform.localRotation = Quaternion.Euler(binding.LocalEuler);
            _spawnedModel.transform.localScale    = ResolveScale(binding.LocalScale);

            _spawnedWeapon           = _weapon.Weapon;
            _hasSpawnedModel         = true;
            _attachedCharacterModel = _character.ActiveModel;

            // TODO(민성): 무기가 간헐적으로 안 보이는 원인을 좁히려고 남긴 진단이다, 원인이 잡히면 지운다
            Debug.Log($"[PlayerWeaponVisual] {_spawnedWeapon}를 {_character.ActiveModel.name}의 {socket.name}에 붙였다", this);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 무기 종류에 해당하는 부착 보정을 돌려준다.
        /// public인 이유는 네트워크 없이 도는 미리보기 리그(CharacterPreviewRig)가
        /// 같은 값으로 무기를 붙여야 하기 때문이다. 미리보기에서 맞춘 값이 실제와 달라지면
        /// 눈으로 맞춰 놓고도 게임에서는 여전히 어긋난다
        /// </summary>
        public bool TryResolveBinding(CharacterKind character, WeaponKind weapon, out WeaponAttachBinding binding)
        {
            // 1순위: 캐릭터와 무기가 모두 맞는 값
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

            // 2순위: 무기만 맞는 값. 조합을 다 채우기 전에도 무기가 손에서 사라지지 않게 하되,
            // 손 뼈 방향 차이만큼 틀어진 상태이므로 그대로 두면 안 된다는 것을 남긴다
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

        // 인스펙터에서 배율을 비워 두면 0이 들어와 무기가 통째로 사라진다, 그때는 1로 본다
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
