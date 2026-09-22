// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 필드 레벨별 등급 가중치. 여기서 등급을 정한 뒤 EquipmentSO의 롤 범위로 스탯을 굴린다
    /// 필드 레벨 3은 일반 잡귀 드랍이 아니라 보스 처치 후 팀 보상 1회에만 쓴다
    /// </summary>
    [CreateAssetMenu(fileName = "DropTableSO", menuName = "Geumeum/DropTableSO")]
    public class DropTableSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct FieldLevelEntry
        {
            public int   FieldLevel;
            public float WeightCommon;
            public float WeightTalisman;
            public float WeightSpiritual;
            public float WeightDivine;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private FieldLevelEntry[] _entries = new FieldLevelEntry[Constants.Combat.FIELD_LEVEL_COUNT];

        // 런 전체 목표 처치 60~65마리에 15%면 9~10개가 나온다. 3인이 나눠 가지면 1인당 3개꼴이고,
        // 12칸에 물약과 권이 자리를 차지해도 여유가 남는다 (2026-09-08 확정)
        [Header("드랍")]
        [Tooltip("잡귀 하나가 죽었을 때 장비가 떨어질 확률. 0이면 떨어지지 않고 1이면 항상 떨어진다")]
        [Range(0f, 1f)]
        [SerializeField] private float _dropChance = 0.15f;

        /****************************************
        *              Properties
        ****************************************/

        public float DropChance => _dropChance;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 필드 레벨의 등급 가중치를 찾는다, 없으면 false </summary>
        public bool TryGetEntry(int fieldLevel, out FieldLevelEntry entry)
        {
            if (_entries == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _entries.Length; ++i)
            {
                if (_entries[i].FieldLevel == fieldLevel)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 필드 레벨, 등급의 가중치, 해당 필드 레벨이 없으면 0 </summary>
        public float GetWeight(int fieldLevel, ItemGrade grade)
        {
            if (!TryGetEntry(fieldLevel, out FieldLevelEntry entry))
            {
                return 0f;
            }

            switch (grade)
            {
                case ItemGrade.Common:
                    return entry.WeightCommon;
                case ItemGrade.Talisman:
                    return entry.WeightTalisman;
                case ItemGrade.Spiritual:
                    return entry.WeightSpiritual;
                case ItemGrade.Divine:
                    return entry.WeightDivine;
                default:
                    return 0f;
            }
        }
    }
}
