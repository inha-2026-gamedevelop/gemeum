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
            public int              NodeId;
            public WeaponKind       WeaponKind;
            public int              ColumnIndex;
            public int              RowIndex; // 낮은 쪽이 선행
            public string           DisplayName;
            public TalentEffectKind EffectKind;
            public int              PointCost;
            public float            Value;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("트리 구조")]
        [SerializeField] private int _columnsPerWeapon = 2;
        [SerializeField] private int _nodesPerColumn   = 5;

        [Header("열")]
        [SerializeField] private ColumnEntry[] _columns = new ColumnEntry[Constants.Combat.WEAPON_KIND_COUNT * 2];

        // 생산 asset은 임시 플레이테스트 수치를 사용한다. 확정 전에도 전체 정의 검증을 통과해야 한다.
        [Header("노드")]
        [SerializeField] private NodeEntry[] _nodes;

        /****************************************
        *              Properties
        ****************************************/

        public int ColumnsPerWeapon => _columnsPerWeapon;
        public int NodesPerColumn   => _nodesPerColumn;

        /// <summary> 한 직업의 전체 노드 수. 획득 포인트 총합보다 커야 선택이 생긴다 </summary>
        public int NodesPerWeapon => _columnsPerWeapon * _nodesPerColumn;

        public int NodeCount => (_nodes == null) ? 0 : _nodes.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 노드 정보를 찾는다, 없으면 false </summary>
        public bool TryGetNode(int nodeId, out NodeEntry entry)
        {
            if (_nodes == null)
            {
                entry = default;
                return false;
            }

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
            if (_columns == null)
            {
                entry = default;
                return false;
            }

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

        /// <summary> 같은 무기와 열에서 지정한 행의 노드를 찾는다 </summary>
        public bool TryGetNode(WeaponKind weaponKind, int columnIndex, int rowIndex,
            out NodeEntry entry)
        {
            if (_nodes == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _nodes.Length; ++i)
            {
                NodeEntry candidate = _nodes[i];
                if ((candidate.WeaponKind == weaponKind) &&
                    (candidate.ColumnIndex == columnIndex) &&
                    (candidate.RowIndex == rowIndex))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 엄격 생산 부트에서 3직업 30노드 정의 전체를 검사한다 </summary>
        public bool ValidateDefinition(out string failure)
        {
            int expectedColumns = Constants.Combat.WEAPON_KIND_COUNT * _columnsPerWeapon;
            int expectedNodes   = expectedColumns * _nodesPerColumn;
            if ((_columnsPerWeapon != 2) || (_nodesPerColumn != 5) ||
                (_columns == null) || (_columns.Length != expectedColumns) ||
                (_nodes == null) || (_nodes.Length != expectedNodes))
            {
                failure = "특성 트리는 3직업 x 2열 x 5행 전체를 가져야 한다";
                return false;
            }

            for (int i = 0; i < _columns.Length; ++i)
            {
                ColumnEntry column = _columns[i];
                if ((!Enum.IsDefined(typeof(WeaponKind), column.WeaponKind)) ||
                    (column.ColumnIndex < 0) ||
                    (column.ColumnIndex >= _columnsPerWeapon))
                {
                    failure = $"특성 열이 유효하지 않다: {i}";
                    return false;
                }

                for (int j = 0; j < i; ++j)
                {
                    if ((_columns[j].WeaponKind == column.WeaponKind) &&
                        (_columns[j].ColumnIndex == column.ColumnIndex))
                    {
                        failure = $"특성 열 좌표가 중복됐다: {column.WeaponKind}/{column.ColumnIndex}";
                        return false;
                    }
                }
            }

            for (int i = 0; i < _nodes.Length; ++i)
            {
                NodeEntry node = _nodes[i];
                if ((node.NodeId <= 0) ||
                    (!Enum.IsDefined(typeof(WeaponKind), node.WeaponKind)) ||
                    (node.ColumnIndex < 0) || (node.ColumnIndex >= _columnsPerWeapon) ||
                    (node.RowIndex < 0) || (node.RowIndex >= _nodesPerColumn) ||
                    (node.PointCost != 1) ||
                    (!TalentEffectRules.IsCompatible(node.WeaponKind, node.EffectKind)) ||
                    (!TalentEffectRules.IsApplicableValue(node.EffectKind, node.Value)) ||
                    (!TryGetColumn(node.WeaponKind, node.ColumnIndex, out _)))
                {
                    failure = $"특성 노드가 적용 가능한 정의가 아니다: {node.NodeId}";
                    return false;
                }

                for (int j = 0; j < i; ++j)
                {
                    if (_nodes[j].NodeId == node.NodeId)
                    {
                        failure = $"특성 노드 ID가 중복됐다: {node.NodeId}";
                        return false;
                    }

                    if ((_nodes[j].WeaponKind == node.WeaponKind) &&
                        (_nodes[j].ColumnIndex == node.ColumnIndex) &&
                        (_nodes[j].RowIndex == node.RowIndex))
                    {
                        failure = $"특성 노드 좌표가 중복됐다: {node.WeaponKind}/" +
                            $"{node.ColumnIndex}/{node.RowIndex}";
                        return false;
                    }
                }
            }

            for (int weapon = 0; weapon < Constants.Combat.WEAPON_KIND_COUNT; ++weapon)
            {
                WeaponKind kind = (WeaponKind)weapon;
                for (int column = 0; column < _columnsPerWeapon; ++column)
                {
                    if (!TryGetColumn(kind, column, out _))
                    {
                        failure = $"특성 열이 비었다: {kind}/{column}";
                        return false;
                    }

                    for (int row = 0; row < _nodesPerColumn; ++row)
                    {
                        if (!TryGetNode(kind, column, row, out _))
                        {
                            failure = $"특성 노드가 비었다: {kind}/{column}/{row}";
                            return false;
                        }
                    }
                }
            }

            failure = null;
            return true;
        }
    }
}
