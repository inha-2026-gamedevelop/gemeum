// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 장비 원본 설계도. 등급별 공격력 롤 범위와 무기별 특수옵션 풀을 가진다.
    /// 굴려진 결과는 EquipmentInstance가 들고 여기에는 두지 않는다
    /// </summary>
    [CreateAssetMenu(fileName = "EquipmentSO", menuName = "Geumeum/EquipmentSO")]
    public class EquipmentSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 등급별 롤 범위. 장비는 공격력만 올리고 생존력은 레벨업으로만 성장한다 </summary>
        [Serializable]
        public struct GradeEntry
        {
            public ItemGrade Grade;
            public string    DisplayName;
            public float     AttackMin;
            public float     AttackMax;
            public float     OptionChance; // 특수옵션이 붙을 확률 0~1
            public int       OptionCount;  // 붙을 경우의 개수
        }

        [Serializable]
        public struct OptionEntry
        {
            public EquipmentOptionKind Kind;
            public float               ValueMin;
            public float               ValueMax;
        }

        /// <summary> 무기 전용 옵션 풀. 패링 게이지는 검만, 유도는 활만 쓰므로 공용 풀로는 성립하지 않는다 </summary>
        [Serializable]
        public struct WeaponOptionPool
        {
            public WeaponKind    WeaponKind;
            public OptionEntry[] Options;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("등급")]
        [SerializeField] private GradeEntry[] _grades = new GradeEntry[Constants.Combat.ITEM_GRADE_COUNT];

        [Header("특수옵션 공용")]
        [SerializeField] private OptionEntry[] _commonOptions;

        [Header("특수옵션 무기 전용")]
        [SerializeField] private WeaponOptionPool[] _weaponOptions = new WeaponOptionPool[Constants.Combat.WEAPON_KIND_COUNT];

        /****************************************
        *              Properties
        ****************************************/

        public OptionEntry[] CommonOptions => _commonOptions;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 등급별 롤 범위를 찾는다, 없으면 false </summary>
        public bool TryGetGrade(ItemGrade grade, out GradeEntry entry)
        {
            for (int i = 0; i < _grades.Length; ++i)
            {
                if (_grades[i].Grade == grade)
                {
                    entry = _grades[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 무기 전용 옵션 풀을 찾는다, 없으면 false </summary>
        public bool TryGetWeaponOptions(WeaponKind weaponKind, out OptionEntry[] options)
        {
            for (int i = 0; i < _weaponOptions.Length; ++i)
            {
                if (_weaponOptions[i].WeaponKind == weaponKind)
                {
                    options = _weaponOptions[i].Options;
                    return true;
                }
            }

            options = null;
            return false;
        }

        /// <summary> 등급 상승권이 쓸 한 단계 위 등급, 최상위면 false </summary>
        public bool TryGetNextGrade(ItemGrade grade, out ItemGrade nextGrade)
        {
            nextGrade = grade + 1;
            return nextGrade <= ItemGrade.Divine;
        }
    }
}
