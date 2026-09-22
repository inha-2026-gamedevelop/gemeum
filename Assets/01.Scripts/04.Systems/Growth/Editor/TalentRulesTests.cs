// System
using System.Reflection;

// Unity
using UnityEngine;

// Test
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems.Tests
{
    public sealed class TalentRulesTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private TalentTreeSO _tree;

        [SetUp]
        public void SetUp()
        {
            _tree = ScriptableObject.CreateInstance<TalentTreeSO>();
            Set(_tree, "_columns", new[]
            {
                new TalentTreeSO.ColumnEntry
                {
                    WeaponKind = WeaponKind.Sword,
                    ColumnIndex = 0,
                    DisplayName = "패링의 길",
                    NodeCount = 3,
                },
            });
            Set(_tree, "_nodes", new[]
            {
                Node(1, 0, TalentEffectKind.ParryWindowSeconds, 0.02f),
                Node(2, 1, TalentEffectKind.ParryGaugeGain, 5f),
                Node(3, 2, TalentEffectKind.CriticalMultiplierMin, 2.4f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_tree);
        }

        [Test]
        public void AllocationRequiresEveryLowerRowAndCurrentRevisionState()
        {
            TalentAllocationSnapshot empty = new TalentAllocationSnapshot(
                1u, true, WeaponKind.Sword, 0, new int[0]);

            Assert.AreEqual(TalentAllocationResult.MissingPrerequisite,
                TalentRules.CanAllocate(_tree, empty, 3, 2, out _));

            TalentAllocationSnapshot rowZero = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Sword, 1, new[] { 1 });
            Assert.AreEqual(TalentAllocationResult.Success,
                TalentRules.CanAllocate(_tree, rowZero, 3, 2, out _));

            TalentAllocationSnapshot duplicate = new TalentAllocationSnapshot(
                3u, true, WeaponKind.Sword, 2, new[] { 1, 2 });
            Assert.AreEqual(TalentAllocationResult.AlreadyAllocated,
                TalentRules.CanAllocate(_tree, duplicate, 3, 2, out _));
        }

        [Test]
        public void RestoreValidationRejectsForgedCostAndCrossWeaponEffect()
        {
            TalentAllocationSnapshot forged = new TalentAllocationSnapshot(
                4u, true, WeaponKind.Sword, 2, new[] { 1 });
            Assert.IsFalse(TalentRules.TryValidateAllocation(
                _tree, forged, 3, out string failure));
            StringAssert.Contains("합계", failure);

            TalentTreeSO.NodeEntry invalid = Node(
                9, 0, TalentEffectKind.HealAmountPercent, 10f);
            Assert.IsFalse(TalentRules.IsValidNode(_tree, invalid));
        }

        [Test]
        public void StrictDefinitionRequiresEveryUniqueWeaponColumnAndRow()
        {
            var columns = new TalentTreeSO.ColumnEntry[6];
            var nodes = new TalentTreeSO.NodeEntry[30];
            int columnIndex = 0;
            int nodeIndex = 0;
            for (int weapon = 0; weapon < Constants.Combat.WEAPON_KIND_COUNT; ++weapon)
            {
                for (int column = 0; column < 2; ++column)
                {
                    columns[columnIndex++] = new TalentTreeSO.ColumnEntry
                    {
                        WeaponKind = (WeaponKind)weapon,
                        ColumnIndex = column,
                        NodeCount = 5,
                    };

                    for (int row = 0; row < 5; ++row)
                    {
                        nodes[nodeIndex] = new TalentTreeSO.NodeEntry
                        {
                            NodeId = nodeIndex + 1,
                            WeaponKind = (WeaponKind)weapon,
                            ColumnIndex = column,
                            RowIndex = row,
                            EffectKind = TalentEffectKind.AttackSpeedPercent,
                            PointCost = 1,
                            Value = 1f,
                        };
                        ++nodeIndex;
                    }
                }
            }

            Set(_tree, "_columns", columns);
            Set(_tree, "_nodes", nodes);
            Assert.IsTrue(_tree.ValidateDefinition(out string failure), failure);

            nodes[29].NodeId = nodes[0].NodeId;
            Set(_tree, "_nodes", nodes);
            Assert.IsFalse(_tree.ValidateDefinition(out failure));
            StringAssert.Contains("ID", failure);
        }

        [Test]
        public void ConnectedTreeFollowsExplicitPrerequisitesNotRowOrder()
        {
            // 뿌리 하나에서 갈래가 갈라지는 방울식 연결. 화면 행 순서와 판정은 별개다
            SetBellTree(
                new[] { 1, 2 },
                BellNode(101, 0, 0, TalentEffectKind.BellFirstHitSpiritGain, 1f, null),
                BellNode(102, 1, 0, TalentEffectKind.SkillSpiritCost, 2f, new[] { 101 }),
                BellNode(103, 1, 1, TalentEffectKind.FieldRadiusBonus, 0.3f, new[] { 102 }));

            TalentAllocationSnapshot empty = new TalentAllocationSnapshot(
                1u, true, WeaponKind.Bell, 0, new int[0]);
            Assert.AreEqual(TalentAllocationResult.Success,
                TalentRules.CanAllocate(_tree, empty, 3, 101, out _),
                "뿌리는 선행이 없다");
            Assert.AreEqual(TalentAllocationResult.MissingPrerequisite,
                TalentRules.CanAllocate(_tree, empty, 3, 102, out _));

            TalentAllocationSnapshot root = new TalentAllocationSnapshot(
                2u, true, WeaponKind.Bell, 1, new[] { 101 });
            Assert.AreEqual(TalentAllocationResult.Success,
                TalentRules.CanAllocate(_tree, root, 3, 102, out _));
            Assert.AreEqual(TalentAllocationResult.MissingPrerequisite,
                TalentRules.CanAllocate(_tree, root, 3, 103, out _),
                "행 번호가 아니라 이어진 앞 노드를 본다");

            Assert.IsTrue(_tree.ValidateDefinition(out string failure), failure);
        }

        [Test]
        public void MissingAndCyclicPrerequisitesFailDefinition()
        {
            SetBellTree(
                new[] { 2 },
                BellNode(101, 0, 0, TalentEffectKind.BellFirstHitSpiritGain, 1f, new[] { 999 }),
                BellNode(102, 0, 1, TalentEffectKind.SkillSpiritCost, 2f, new[] { 101 }));
            Assert.IsFalse(_tree.ValidateDefinition(out string failure));
            StringAssert.Contains("선행", failure);

            SetBellTree(
                new[] { 2 },
                BellNode(101, 0, 0, TalentEffectKind.BellFirstHitSpiritGain, 1f, new[] { 102 }),
                BellNode(102, 0, 1, TalentEffectKind.SkillSpiritCost, 2f, new[] { 101 }));
            Assert.IsFalse(_tree.ValidateDefinition(out failure));
            StringAssert.Contains("순환", failure);
        }

        /// <summary>
        /// 방울 노드만 바꿔 가며 검사한다. 정의 검증은 세 무기가 모두 있어야 통과하므로
        /// 검/활은 최소 한 노드짜리 열로 채운다
        /// </summary>
        private void SetBellTree(int[] bellColumnCounts,
            params TalentTreeSO.NodeEntry[] bellNodes)
        {
            var columns = new System.Collections.Generic.List<TalentTreeSO.ColumnEntry>
            {
                new TalentTreeSO.ColumnEntry
                { WeaponKind = WeaponKind.Sword, ColumnIndex = 0, NodeCount = 1 },
                new TalentTreeSO.ColumnEntry
                { WeaponKind = WeaponKind.Bow, ColumnIndex = 0, NodeCount = 1 },
            };
            for (int i = 0; i < bellColumnCounts.Length; ++i)
            {
                columns.Add(new TalentTreeSO.ColumnEntry
                { WeaponKind = WeaponKind.Bell, ColumnIndex = i, NodeCount = bellColumnCounts[i] });
            }

            var nodes = new System.Collections.Generic.List<TalentTreeSO.NodeEntry>
            {
                Node(1, 0, TalentEffectKind.ParryWindowSeconds, 0.02f),
                new TalentTreeSO.NodeEntry
                {
                    NodeId = 201, WeaponKind = WeaponKind.Bow, ColumnIndex = 0, RowIndex = 0,
                    DisplayName = "201", EffectKind = TalentEffectKind.ArrowSpeedPercent,
                    PointCost = 1, Value = 5f,
                },
            };
            nodes.AddRange(bellNodes);

            Set(_tree, "_columns", columns.ToArray());
            Set(_tree, "_nodes", nodes.ToArray());
        }

        private static TalentTreeSO.NodeEntry BellNode(int id, int column, int row,
            TalentEffectKind effect, float value, int[] prerequisites)
        {
            return new TalentTreeSO.NodeEntry
            {
                NodeId        = id,
                WeaponKind    = WeaponKind.Bell,
                ColumnIndex   = column,
                RowIndex      = row,
                DisplayName   = id.ToString(),
                EffectKind    = effect,
                PointCost     = 1,
                Value         = value,
                Prerequisites = prerequisites,
            };
        }

        private static TalentTreeSO.NodeEntry Node(int id, int row,
            TalentEffectKind effect, float value)
        {
            return new TalentTreeSO.NodeEntry
            {
                NodeId       = id,
                WeaponKind   = WeaponKind.Sword,
                ColumnIndex  = 0,
                RowIndex     = row,
                DisplayName  = id.ToString(),
                EffectKind   = effect,
                PointCost    = 1,
                Value        = value,
            };
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, PRIVATE_INSTANCE).SetValue(target, value);
        }
    }
}
