// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 특성 노드 구성과 수치
    /// 노드 수보다 획득 포인트가 적어야 한 줄을 밀지 양쪽을 섞을지 선택이 생긴다
    ///
    /// 열/행은 화면 위치이고, 실제 구매 선행은 Prerequisites가 정한다
    /// 방울의 연결 트리처럼 격자가 아닌 모양을 쓰려면 선행을 직접 적는다 -
    /// 비워 두면 같은 열의 낮은 행 전부라는 기존 검/활 규칙을 그대로 쓴다
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

            /// <summary> 이 열이 가지는 노드 수. 행 0부터 이 값 직전까지 빈틈이 없어야 한다 </summary>
            public int NodeCount;
        }

        [Serializable]
        public struct NodeEntry
        {
            public int              NodeId;
            public WeaponKind       WeaponKind;
            public int              ColumnIndex;
            public int              RowIndex; // 화면 위치. 선행이 비었을 때만 판정에도 쓴다
            public string           DisplayName;
            public TalentEffectKind EffectKind;
            public int              PointCost;
            public float            Value;

            /// <summary> 명시 선행 노드. 비면 같은 열의 낮은 행 전부가 선행이다 </summary>
            public int[] Prerequisites;

            public bool HasExplicitPrerequisites =>
                ((Prerequisites != null) && (Prerequisites.Length > 0));
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("트리 구조 한계")]
        [SerializeField] private int _maxColumnsPerWeapon = 4;
        [SerializeField] private int _maxRowsPerColumn    = 5;

        [Header("열")]
        [SerializeField] private ColumnEntry[] _columns;

        // 생산 asset은 임시 플레이테스트 수치를 사용한다. 확정 전에도 전체 정의 검증을 통과해야 한다
        [Header("노드")]
        [SerializeField] private NodeEntry[] _nodes;

        /****************************************
        *              Properties
        ****************************************/

        public int MaxColumnsPerWeapon => _maxColumnsPerWeapon;
        public int MaxRowsPerColumn    => _maxRowsPerColumn;

        public int NodeCount => (_nodes == null) ? 0 : _nodes.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 순번으로 노드를 읽는다. 목록을 그대로 훑어야 하는 편성 화면이 쓴다 </summary>
        public bool TryGetNodeAt(int index, out NodeEntry entry)
        {
            if ((_nodes == null) || (index < 0) || (index >= _nodes.Length))
            {
                entry = default;
                return false;
            }

            entry = _nodes[index];
            return true;
        }

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

        /// <summary> 한 무기의 열 수. 무기마다 갈래 수가 다르다 </summary>
        public int CountColumns(WeaponKind weaponKind)
        {
            if (_columns == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _columns.Length; ++i)
            {
                if (_columns[i].WeaponKind == weaponKind)
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 한 무기의 전체 노드 수 </summary>
        public int CountNodes(WeaponKind weaponKind)
        {
            if (_nodes == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _nodes.Length; ++i)
            {
                if (_nodes[i].WeaponKind == weaponKind)
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 엄격 생산 부트에서 무기별 트리 정의 전체를 검사한다 </summary>
        public bool ValidateDefinition(out string failure)
        {
            if ((_maxColumnsPerWeapon <= 0) || (_maxRowsPerColumn <= 0) ||
                (_columns == null) || (_columns.Length == 0) ||
                (_nodes == null) || (_nodes.Length == 0))
            {
                failure = "특성 트리에 열이나 노드가 없다";
                return false;
            }

            if (!ValidateColumns(out failure))
            {
                return false;
            }

            if (!ValidateNodes(out failure))
            {
                return false;
            }

            if (!ValidateLayoutIsComplete(out failure))
            {
                return false;
            }

            return ValidatePrerequisiteGraph(out failure);
        }

        private bool ValidateColumns(out string failure)
        {
            for (int i = 0; i < _columns.Length; ++i)
            {
                ColumnEntry column = _columns[i];
                if ((!Enum.IsDefined(typeof(WeaponKind), column.WeaponKind)) ||
                    (column.ColumnIndex < 0) ||
                    (column.ColumnIndex >= _maxColumnsPerWeapon) ||
                    (column.NodeCount <= 0) || (column.NodeCount > _maxRowsPerColumn))
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

            // 열 번호가 0부터 이어지지 않으면 화면이 가운데를 비우고 그린다
            for (int weapon = 0; weapon < Constants.Combat.WEAPON_KIND_COUNT; ++weapon)
            {
                WeaponKind kind  = (WeaponKind)weapon;
                int        count = CountColumns(kind);
                if (count == 0)
                {
                    failure = $"특성 열이 비었다: {kind}";
                    return false;
                }

                for (int column = 0; column < count; ++column)
                {
                    if (!TryGetColumn(kind, column, out _))
                    {
                        failure = $"특성 열 번호가 이어지지 않는다: {kind}/{column}";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private bool ValidateNodes(out string failure)
        {
            for (int i = 0; i < _nodes.Length; ++i)
            {
                NodeEntry node = _nodes[i];
                if ((node.NodeId <= 0) ||
                    (!Enum.IsDefined(typeof(WeaponKind), node.WeaponKind)) ||
                    (node.ColumnIndex < 0) || (node.ColumnIndex >= _maxColumnsPerWeapon) ||
                    (node.RowIndex < 0) || (node.RowIndex >= _maxRowsPerColumn) ||
                    (node.PointCost != 1) ||
                    (!TalentEffectRules.IsCompatible(node.WeaponKind, node.EffectKind)) ||
                    (!TalentEffectRules.IsApplicableValue(node.EffectKind, node.Value)) ||
                    (!TryGetColumn(node.WeaponKind, node.ColumnIndex, out ColumnEntry column)) ||
                    (node.RowIndex >= column.NodeCount))
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

            failure = null;
            return true;
        }

        private bool ValidateLayoutIsComplete(out string failure)
        {
            int expected = 0;
            for (int i = 0; i < _columns.Length; ++i)
            {
                ColumnEntry column = _columns[i];
                expected += column.NodeCount;

                for (int row = 0; row < column.NodeCount; ++row)
                {
                    if (!TryGetNode(column.WeaponKind, column.ColumnIndex, row, out _))
                    {
                        failure = $"특성 노드가 비었다: {column.WeaponKind}/" +
                            $"{column.ColumnIndex}/{row}";
                        return false;
                    }
                }
            }

            if (expected != _nodes.Length)
            {
                failure = $"특성 노드 수가 열 정의와 다르다: {_nodes.Length} != {expected}";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary> 선행 참조가 실제로 존재하고 순환하지 않는지 본다 </summary>
        private bool ValidatePrerequisiteGraph(out string failure)
        {
            for (int i = 0; i < _nodes.Length; ++i)
            {
                NodeEntry node = _nodes[i];
                if (!node.HasExplicitPrerequisites)
                {
                    continue;
                }

                for (int j = 0; j < node.Prerequisites.Length; ++j)
                {
                    int required = node.Prerequisites[j];
                    if ((required == node.NodeId) ||
                        (!TryGetNode(required, out NodeEntry parent)) ||
                        (parent.WeaponKind != node.WeaponKind))
                    {
                        failure = $"특성 선행 참조가 유효하지 않다: {node.NodeId} -> {required}";
                        return false;
                    }

                    for (int k = 0; k < j; ++k)
                    {
                        if (node.Prerequisites[k] == required)
                        {
                            failure = $"특성 선행이 중복됐다: {node.NodeId} -> {required}";
                            return false;
                        }
                    }
                }
            }

            var visiting = new HashSet<int>();
            var settled  = new HashSet<int>();
            for (int i = 0; i < _nodes.Length; ++i)
            {
                if (!IsAcyclic(_nodes[i], visiting, settled))
                {
                    failure = $"특성 선행이 순환한다: {_nodes[i].NodeId}";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool IsAcyclic(in NodeEntry node, HashSet<int> visiting, HashSet<int> settled)
        {
            if (settled.Contains(node.NodeId))
            {
                return true;
            }

            if (!visiting.Add(node.NodeId))
            {
                return false;
            }

            bool acyclic = true;
            foreach (int required in EnumeratePrerequisites(node))
            {
                if ((!TryGetNode(required, out NodeEntry parent)) ||
                    (!IsAcyclic(parent, visiting, settled)))
                {
                    acyclic = false;
                    break;
                }
            }

            visiting.Remove(node.NodeId);
            if (acyclic)
            {
                settled.Add(node.NodeId);
            }

            return acyclic;
        }

        /// <summary>
        /// 이 노드를 사기 전에 이미 가지고 있어야 하는 노드들
        /// 명시 선행이 없으면 같은 열의 낮은 행 전부라는 기존 규칙을 쓴다
        /// </summary>
        public IEnumerable<int> EnumeratePrerequisites(NodeEntry node)
        {
            if (node.HasExplicitPrerequisites)
            {
                for (int i = 0; i < node.Prerequisites.Length; ++i)
                {
                    yield return node.Prerequisites[i];
                }

                yield break;
            }

            for (int row = 0; row < node.RowIndex; ++row)
            {
                if (TryGetNode(node.WeaponKind, node.ColumnIndex, row, out NodeEntry legacy))
                {
                    yield return legacy.NodeId;
                }
                else
                {
                    // 빈 칸이 선행이면 살 수 없는 노드다. 0은 어떤 노드도 아니라 거부로 이어진다
                    yield return 0;
                }
            }
        }
    }
}
