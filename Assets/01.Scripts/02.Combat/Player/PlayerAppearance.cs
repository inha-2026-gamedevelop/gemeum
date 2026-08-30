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
        /// 무기에 매칭되는 모델만 켠다. 매칭되는 모델이 없으면 현재 외형을 보존한다.
        /// 새 외형이 준비되지 않은 상태에서 기존 모델부터 끄면 플레이어가 통째로 사라지기 때문이다
        /// </summary>
        public void ApplyAppearance(WeaponKind weapon)
        {
            GameObject matchedModel = null;

            for (int i = 0; i < _bindings.Length; ++i)
            {
                WeaponModelBinding binding = _bindings[i];

                if ((binding.Weapon != weapon) || (binding.Model == null))
                {
                    continue;
                }

                matchedModel = binding.Model;
                break;
            }

            if (matchedModel == null)
            {
                Debug.LogWarning($"[PlayerAppearance] {weapon}에 매칭되는 모델 바인딩이 없어 현재 외형을 유지한다", this);
                return;
            }

            // 사용할 모델을 먼저 확정한 뒤 한 번에 전환한다. 같은 모델을 여러 무기의 임시 외형으로
            // 공유해도 마지막 바인딩 순서에 따라 다시 꺼지지 않는다
            for (int i = 0; i < _bindings.Length; ++i)
            {
                GameObject model = _bindings[i].Model;
                if (model != null)
                {
                    model.SetActive(model == matchedModel);
                }
            }
        }
    }
}
