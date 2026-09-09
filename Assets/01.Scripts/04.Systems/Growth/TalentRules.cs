// System
using System;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 특성 트리와 서버 배분 요청을 검증하는 순수 규칙 </summary>
    public static class TalentRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static TalentAllocationResult CanAllocate(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, int totalPoints, int nodeId,
            out TalentTreeSO.NodeEntry node)
        {
            node = default;
            if ((tree == null) || (!allocation.IsValid) || (!allocation.HasWeapon) ||
                (totalPoints < 0) || (!tree.TryGetNode(nodeId, out node)) ||
                (!IsValidNode(tree, node)))
            {
                return TalentAllocationResult.InvalidNode;
            }

            if (node.WeaponKind != allocation.Weapon)
            {
                return TalentAllocationResult.WeaponMismatch;
            }

            if (allocation.Contains(nodeId))
            {
                return TalentAllocationResult.AlreadyAllocated;
            }

            if (!HasPrerequisites(tree, allocation, node))
            {
                return TalentAllocationResult.MissingPrerequisite;
            }

            if ((allocation.SpentPoints > totalPoints) ||
                (node.PointCost > (totalPoints - allocation.SpentPoints)))
            {
                return TalentAllocationResult.InsufficientPoints;
            }

            return TalentAllocationResult.Success;
        }

        public static bool TryValidateAllocation(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, int totalPoints, out string failure)
        {
            if ((tree == null) || (!allocation.IsValid) || (!allocation.HasWeapon) ||
                (totalPoints < 0) || (allocation.SpentPoints > totalPoints))
            {
                failure = "특성 배분 헤더가 유효하지 않다";
                return false;
            }

            int spentPoints = 0;
            for (int i = 0; i < allocation.NodeIds.Length; ++i)
            {
                int nodeId = allocation.NodeIds[i];
                if ((!tree.TryGetNode(nodeId, out TalentTreeSO.NodeEntry node)) ||
                    (!IsValidNode(tree, node)) || (node.WeaponKind != allocation.Weapon))
                {
                    failure = $"특성 노드가 유효하지 않다: {nodeId}";
                    return false;
                }

                for (int j = 0; j < i; ++j)
                {
                    if (allocation.NodeIds[j] == nodeId)
                    {
                        failure = $"특성 노드가 중복됐다: {nodeId}";
                        return false;
                    }
                }

                if (!HasPrerequisites(tree, allocation, node))
                {
                    failure = $"특성 선행 노드가 없다: {nodeId}";
                    return false;
                }

                if (spentPoints > (int.MaxValue - node.PointCost))
                {
                    failure = "특성 포인트 합계가 범위를 넘었다";
                    return false;
                }

                spentPoints += node.PointCost;
            }

            if ((spentPoints != allocation.SpentPoints) || (spentPoints > totalPoints))
            {
                failure = "특성 포인트 합계가 서버 배분과 다르다";
                return false;
            }

            failure = null;
            return true;
        }

        public static bool IsValidNode(TalentTreeSO tree, in TalentTreeSO.NodeEntry node)
        {
            if ((tree == null) || (node.NodeId <= 0) ||
                (!Enum.IsDefined(typeof(WeaponKind), node.WeaponKind)) ||
                (!Enum.IsDefined(typeof(TalentEffectKind), node.EffectKind)) ||
                (node.EffectKind == TalentEffectKind.None) ||
                (!TalentEffectRules.IsCompatible(node.WeaponKind, node.EffectKind)) ||
                (!TalentEffectRules.IsApplicableValue(node.EffectKind, node.Value)) ||
                (node.ColumnIndex < 0) || (node.ColumnIndex >= tree.ColumnsPerWeapon) ||
                (node.RowIndex < 0) || (node.RowIndex >= tree.NodesPerColumn) ||
                (node.PointCost <= 0) || (!IsFinite(node.Value)))
            {
                return false;
            }

            return tree.TryGetColumn(node.WeaponKind, node.ColumnIndex, out _);
        }

        private static bool HasPrerequisites(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, in TalentTreeSO.NodeEntry node)
        {
            for (int row = 0; row < node.RowIndex; ++row)
            {
                if ((!tree.TryGetNode(node.WeaponKind, node.ColumnIndex, row,
                        out TalentTreeSO.NodeEntry prerequisite)) ||
                    (!allocation.Contains(prerequisite.NodeId)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
