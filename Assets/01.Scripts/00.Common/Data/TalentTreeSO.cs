// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 특성 노드 구성과 수치.
    /// 노드 수보다 획득 포인트가 적어야 한 줄을 밀지 양쪽을 섞을지 선택이 생긴다
    /// </summary>
    [CreateAssetMenu(fileName = "TalentTreeSO", menuName = "Geumeum/TalentTreeSO")]
    public class TalentTreeSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct ColumnEntry
        {
            public WeaponKind WeaponKind;
            public int        ColumnIndex;
            public string     DisplayName;
        }

        [Serializable]
        public struct NodeEntry
        {
            public int        NodeId;
            public WeaponKind WeaponKind;
            public int        ColumnIndex;
            public int        RowIndex; // 낮은 쪽이 선행
            public string     DisplayName;
            public float      Value;    // 의미는 노드마다 다르다
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("트리 구조")]
        [SerializeField] private int _columnsPerWeapon = 2;
        [SerializeField] private int _nodesPerColumn   = 5;

        [Header("열")]
        [SerializeField] private ColumnEntry[] _columns = new ColumnEntry[Constants.Combat.WEAPON_KIND_COUNT * 2];

        // TODO(민성): 노드별 구체 수치는 미정, 시스템 파트가 확정 후 채운다
        [Header("노드")]
        [SerializeField] private NodeEntry[] _nodes;

        /****************************************
        *              Properties
        ****************************************/

        public int ColumnsPerWeapon => _columnsPerWeapon;
        public int NodesPerColumn   => _nodesPerColumn;

        /// <summary> 한 직업의 전체 노드 수. 획득 포인트 총합보다 커야 선택이 생긴다 </summary>
        public int NodesPerWeapon => _columnsPerWeapon * _nodesPerColumn;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 노드 정보를 찾는다, 없으면 false </summary>
        public bool TryGetNode(int nodeId, out NodeEntry entry)
        {
            for (int i = 0; i < _nodes.Length; ++i)
            {
                if (_nodes[i].NodeId == nodeId)
                {
                    entry = _nodes[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 열 이름을 찾는다, 없으면 false </summary>
        public bool TryGetColumn(WeaponKind weaponKind, int columnIndex, out ColumnEntry entry)
        {
            for (int i = 0; i < _columns.Length; ++i)
            {
                if ((_columns[i].WeaponKind == weaponKind) && (_columns[i].ColumnIndex == columnIndex))
                {
                    entry = _columns[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
