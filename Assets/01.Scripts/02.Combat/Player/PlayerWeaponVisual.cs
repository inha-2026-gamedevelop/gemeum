// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// PlayerWeapon의 무기 종류와 PlayerAppearance가 켠 모델의 오른손 뼈를 보고 무기 모델을 붙인다.
    /// 표현 전용이며 판정에는 관여하지 않는다. 검만 우선 연결하고 나머지 무기는 모델이 준비되면 채운다
    /// </summary>
    public class PlayerWeaponVisual : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string RIGHT_HAND_BONE_NAME = "RightHand";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _swordModelSource;

        private PlayerWeapon     _weapon;
        private PlayerAppearance _appearance;

        private GameObject _spawnedModel;
        private WeaponKind _spawnedWeapon;
        private bool       _hasSpawnedModel = false;

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

            if ((_hasSpawnedModel) && (_spawnedWeapon == _weapon.Weapon) && (_spawnedModel != null))
            {
                return; // 이미 같은 무기가 같은 모델에 붙어 있다
            }

            DetachCurrentModel();

            GameObject source = ResolveModelSource(_weapon.Weapon);
            if (source == null)
            {
                return; // 아직 이 무기의 모델이 연결되지 않았다
            }

            Transform socket = FindDeepChild(_appearance.ActiveModel.transform, RIGHT_HAND_BONE_NAME);
            if (socket == null)
            {
                Debug.LogWarning($"[PlayerWeaponVisual] {_appearance.ActiveModel.name}에서 {RIGHT_HAND_BONE_NAME} 뼈를 찾지 못했다", this);
                return;
            }

            _spawnedModel = Instantiate(source, socket);
            _spawnedModel.transform.localPosition = Vector3.zero;
            _spawnedModel.transform.localRotation = Quaternion.identity;

            _spawnedWeapon   = _weapon.Weapon;
            _hasSpawnedModel = true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private GameObject ResolveModelSource(WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword: return _swordModelSource;
                default:                return null;
            }
        }

        private void DetachCurrentModel()
        {
            if (_spawnedModel != null)
            {
                Destroy(_spawnedModel);
            }

            _spawnedModel    = null;
            _hasSpawnedModel = false;
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
