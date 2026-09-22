using System.Linq;
using Geumeum.EditorTools;
using NUnit.Framework;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

namespace Geumeum.Combat.Tests
{
    public sealed class PlayerUnityBehaviorTests
    {
        [Test]
        public void AssetUsesUnityFlowNodesAndAllDecisionLeaves()
        {
            var authoring = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(PlayerBehaviorGraphAssets.Path);
            Assert.NotNull(authoring);
            Assert.AreEqual(1, authoring.Roots.Count);
            Assert.IsFalse(((StartNodeModel)authoring.Roots[0]).Repeat);
            Assert.IsTrue(((StartNodeModel)authoring.Roots[0]).AllowMultipleRepeatsPerTick);
            Assert.IsFalse(authoring.HasPlaceholderNode());
            var selectors = authoring.Nodes.OfType<CompositeNodeModel>().ToArray();
            Assert.AreEqual(2, selectors.Length);
            Assert.IsTrue(selectors.All(node => node.NodeType.Type == typeof(SelectorComposite)));
            var leaves = authoring.Nodes.OfType<ActionNodeModel>().ToArray();
            CollectionAssert.AreEquivalent(System.Enum.GetValues(typeof(PlayerBehaviorStep)),
                leaves.Select(node => node.GetOrCreateField("Step", typeof(PlayerBehaviorStep)).LocalValue.ObjectValue));
            Assert.NotNull(Resources.Load<BehaviorGraph>("BT/PlayerBehavior"));
        }

        [Test]
        public void RuntimeResourcesDoNotIncludeAuthoringAssets()
        {
            Assert.IsInstanceOf<BehaviorGraph>(AssetDatabase.LoadMainAssetAtPath(PlayerBehaviorGraphAssets.RuntimePath));
            CollectionAssert.DoesNotContain(AssetDatabase.GetDependencies(PlayerBehaviorGraphAssets.RuntimePath), PlayerBehaviorGraphAssets.Path);
            Assert.IsFalse(AssetDatabase.LoadAllAssetsAtPath(PlayerBehaviorGraphAssets.RuntimePath).Any(a => a is BehaviorAuthoringGraph));
        }

        [Test]
        public void InstancesAreIsolatedAndClientLeavesCannotRunWithoutServerBrain()
        {
            var asset = Resources.Load<BehaviorGraph>("BT/PlayerBehavior");
            var first = new GameObject("BehaviorFirst");
            var second = new GameObject("BehaviorSecond");
            try
            {
                var a = first.AddComponent<BehaviorGraphAgent>(); a.enabled = false; a.Graph = asset; a.Init();
                var b = second.AddComponent<BehaviorGraphAgent>(); b.enabled = false; b.Graph = asset; b.Init();
                Assert.AreNotSame(asset, a.Graph);
                Assert.AreNotSame(a.Graph, b.Graph);
                a.Graph.Restart();
                for (int i = 0; i < 128 && a.Graph.IsRunning; ++i) { a.Graph.Tick(); }
                Assert.IsFalse(a.Graph.IsRunning, "Decision graph must terminate in one authority tick");
                Assert.AreEqual(Node.Status.Failure, a.Graph.RootGraph.Root.CurrentStatus);
                Assert.AreEqual(Node.Status.Uninitialized, b.Graph.RootGraph.Root.CurrentStatus);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
