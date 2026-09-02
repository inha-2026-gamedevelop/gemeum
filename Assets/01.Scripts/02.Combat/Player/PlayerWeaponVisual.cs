// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// PlayerWeapon의 무기 종류와 PlayerAppearance가 켠 모델의 오른손 뼈를 보고 무기 모델을 붙인다.
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

        /// <summary> 무기 하나의 모델과 손 뼈 기준 부착 보정. 무기마다 자루 방향과 크기가 달라 값이 따로 필요하다 </summary>
        [Serializable]
        public struct WeaponAttachBinding
        {
            public WeaponKind Weapon;
            public GameObject ModelSource;

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
        private PlayerAppearance _appearance;

        private GameObject _spawnedModel;
        private WeaponKind _spawnedWeapon;
        private bool       _hasSpawnedModel = false;

        // 무기는 활성 모델의 손 뼈에 자식으로 붙는다. 외형이 바뀌면 이전 모델이 꺼지면서
        // 무기도 같이 안 보이게 되므로, 어느 모델에 붙였는지 기억해 두고 바뀌면 다시 붙인다
        private GameObject _attachedAppearanceModel;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weapon     = GetComponent<PlayerWeapon>();
            _appearance = GetComponent<PlayerAppearance>();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> PlayerWeapon, PlayerAppearance의 SyncVar 훅이 값이 바뀔 때마다 호출한다. 순서는 상관없다 </summary>
        public void Refresh()
        {
            if ((_weapon == null) || (!_weapon.HasWeapon) || (_appearance == null) || (_appearance.ActiveModel == null))
            {
                return;
            }

            if ((_hasSpawnedModel) &&
                (_spawnedWeapon == _weapon.Weapon) &&
                (_spawnedModel != null) &&
                (_attachedAppearanceModel == _appearance.ActiveModel))
            {
                return; // 이미 같은 무기가 지금 켜져 있는 모델에 붙어 있다
            }

            DetachCurrentModel();

            if (!TryResolveBinding(_weapon.Weapon, out WeaponAttachBinding binding))
            {
                Debug.LogWarning($"[PlayerWeaponVisual] {_weapon.Weapon} 모델이 연결되지 않아 손이 빈 채로 둔다", this);
                return;
            }

            Transform socket = FindDeepChild(_appearance.ActiveModel.transform, RIGHT_HAND_BONE_NAME);
            if (socket == null)
            {
                Debug.LogWarning($"[PlayerWeaponVisual] {_appearance.ActiveModel.name}에서 {RIGHT_HAND_BONE_NAME} 뼈를 찾지 못했다", this);
                return;
            }

            _spawnedModel = Instantiate(binding.ModelSource, socket);

            // 손 뼈 원점에 그대로 놓으면 자루 방향과 크기가 맞지 않아 몸통을 뚫는다
            _spawnedModel.transform.localPosition = binding.LocalPosition;
            _spawnedModel.transform.localRotation = Quaternion.Euler(binding.LocalEuler);
            _spawnedModel.transform.localScale    = ResolveScale(binding.LocalScale);

            _spawnedWeapon           = _weapon.Weapon;
            _hasSpawnedModel         = true;
            _attachedAppearanceModel = _appearance.ActiveModel;

            // TODO(민성): 무기가 간헐적으로 안 보이는 원인을 좁히려고 남긴 진단이다, 원인이 잡히면 지운다
            Debug.Log($"[PlayerWeaponVisual] {_spawnedWeapon}를 {_appearance.ActiveModel.name}의 {socket.name}에 붙였다", this);
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool TryResolveBinding(WeaponKind weapon, out WeaponAttachBinding binding)
        {
            for (int i = 0; i < _bindings.Length; ++i)
            {
                if ((_bindings[i].Weapon != weapon) || (_bindings[i].ModelSource == null))
                {
                    continue;
                }

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
            _attachedAppearanceModel = null;
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
