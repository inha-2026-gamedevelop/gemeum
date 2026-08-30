// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 현재 무기(직업)에 맞는 모델을 켠다. PlayerWeapon의 SyncVar 훅에서 호출되며,
    /// 판정에는 관여하지 않는 표현 전용 컴포넌트다
    /// </summary>
    public class PlayerAppearance : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct WeaponModelBinding
        {
            public WeaponKind Weapon;
            public GameObject Model;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private WeaponModelBinding[] _bindings = Array.Empty<WeaponModelBinding>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 무기에 매칭되는 모델만 켠다. 아직 모델이 없는 직업(신궁·무녀)은 바인딩을 비워 두면
        /// 매칭이 없어 아무것도 켜지지 않는다 — 임의로 다른 모델을 대신 보여주지 않는다
        /// </summary>
        public void ApplyAppearance(WeaponKind weapon)
        {
            bool matched = false;

            for (int i = 0; i < _bindings.Length; ++i)
            {
                WeaponModelBinding binding = _bindings[i];

                if (binding.Model == null)
                {
                    continue;
                }

                bool isMatch = (binding.Weapon == weapon);
                binding.Model.SetActive(isMatch);
                matched |= isMatch;
            }

            if (!matched)
            {
                Debug.LogWarning($"[PlayerAppearance] {weapon}에 매칭되는 모델 바인딩이 없다", this);
            }
        }
    }
}
