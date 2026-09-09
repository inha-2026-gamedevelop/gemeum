// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 객주 상품 목록과 가격 </summary>
    [CreateAssetMenu(fileName = "ShopSO", menuName = "Geumeum/ShopSO")]
    public class ShopSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct ItemEntry
        {
            public ShopItemKind Kind;
            public string       DisplayName;
            public int          Price;       // 냥
            public float        EffectValue; // 물약 회복량, 권종은 0
            public int          CarryLimit;  // 1인 소지 제한, -1이면 무제한
            public int          MaxStack;    // 한 슬롯에 보관 가능한 수량
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("개방 조건")]
        // 한 구간에만 열어야 상승권이냐 물약이냐를 두고 파티가 갈등한다
        [SerializeField] private RunPhase _openPhase = RunPhase.Shrink2;

        [Header("상품")]
        [SerializeField] private ItemEntry[] _items;

        /****************************************
        *              Properties
        ****************************************/

        public RunPhase    OpenPhase => _openPhase;
        public ItemEntry[] Items     => _items;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 상품 정보를 찾는다, 없으면 false </summary>
        public bool TryGetItem(ShopItemKind kind, out ItemEntry entry)
        {
            if (_items == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _items.Length; ++i)
            {
                if (_items[i].Kind == kind)
                {
                    entry = _items[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
