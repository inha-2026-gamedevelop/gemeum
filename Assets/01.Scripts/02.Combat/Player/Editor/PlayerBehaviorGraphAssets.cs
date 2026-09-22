using System;
using System.Linq;
using Geumeum.Combat;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEngine;

namespace Geumeum.EditorTools
{
    /// <summary>Creates a normal editable Unity Behavior asset. Never overwrites an existing graph</summary>
    public static class PlayerBehaviorGraphAssets
    {
        public const string Path = "Assets/09.Data/BT/PlayerBehavior.asset";
        public const string RuntimePath = "Assets/09.Data/Resources/BT/PlayerBehavior.asset";

        [MenuItem("그믐/AI/Open Player Behavior Graph")]
        public static void Open()
        {
            EnsureCreated();
            AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(Path));
        }

        public static void EnsureCreated()
        {
            if (!AssetDatabase.IsValidFolder("Assets/09.Data/BT"))
            { AssetDatabase.CreateFolder("Assets/09.Data", "BT"); }
            if (AssetDatabase.LoadMainAssetAtPath(RuntimePath) is BehaviorAuthoringGraph)
            {
                string failure = AssetDatabase.MoveAsset(RuntimePath, Path);
                if (!string.IsNullOrEmpty(failure)) { throw new InvalidOperationException(failure); }
            }
            if (AssetDatabase.LoadMainAssetAtPath(Path) is BehaviorAuthoringGraph existing)
            { AddRegroupBranch(existing); ExportRuntime(existing); return; }
            if (!AssetDatabase.IsValidFolder("Assets/09.Data/Resources/BT"))
            { AssetDatabase.CreateFolder("Assets/09.Data/Resources", "BT"); }
            var graph = ScriptableObject.CreateInstance<BehaviorAuthoringGraph>();
            graph.name = "PlayerBehavior";
            graph.Description = "Server decision BT. Left-to-right priority. One synchronous evaluation per authority tick; On Start Repeat OFF. Bot perception is throttled by BotConfig. Wait/Repeat nodes are not supported here.";
            AssetDatabase.CreateAsset(graph, Path);
            graph.EnsureAssetHasBlackboard();
            // Import can insert the default On Start before we author our own root
            graph.Nodes.Clear();

            var start = (StartNodeModel)Create(graph, typeof(Start), typeof(StartNodeModel), 0, 0);
            start.Repeat = false;
            // Start also applies its frame guard when Repeat is OFF; permit same-tick completion
            start.AllowMultipleRepeatsPerTick = true;
            var root = Create(graph, typeof(SelectorComposite), typeof(CompositeNodeModel), 0, 160);
            Connect(start, root);
            Connect(root, Leaf(graph, PlayerBehaviorStep.Blocked, -600, 340));
            Connect(root, Leaf(graph, PlayerBehaviorStep.Human, -300, 340));
            var prepare = Leaf(graph, PlayerBehaviorStep.PrepareBot, 300, 340);
            Connect(root, prepare);
            // Linking an action to a child is compiled by Unity Behavior as an implicit Sequence
            var bots = Create(graph, typeof(SelectorComposite), typeof(CompositeNodeModel), 300, 510);
            Connect(prepare, bots);
            var steps = new[] { PlayerBehaviorStep.Safety, PlayerBehaviorStep.Defend, PlayerBehaviorStep.Regroup, PlayerBehaviorStep.Support,
                PlayerBehaviorStep.Combat, PlayerBehaviorStep.Follow, PlayerBehaviorStep.Idle };
            for (int i = 0; i < steps.Length; ++i)
            { Connect(bots, Leaf(graph, steps[i], -500 + i * 320, 710)); }
            graph.ValidateAsset();
            graph.BuildRuntimeGraph();
            graph.SaveAsset();
            ExportRuntime(graph);
            AssetDatabase.SaveAssets();
        }

        private static void AddRegroupBranch(BehaviorAuthoringGraph graph)
        {
            var leaves = graph.Nodes.OfType<ActionNodeModel>().ToArray();
            if (leaves.Any(n => (PlayerBehaviorStep)n.GetOrCreateField("Step", typeof(PlayerBehaviorStep)).LocalValue.ObjectValue == PlayerBehaviorStep.Regroup)) { return; }
            var support = leaves.First(n => (PlayerBehaviorStep)n.GetOrCreateField("Step", typeof(PlayerBehaviorStep)).LocalValue.ObjectValue == PlayerBehaviorStep.Support);
            var parent = support.InputPortModels.First().Connections.First().NodeModel;
            Vector2 position = support.Position;
            foreach (var leaf in leaves)
            { if (leaf.Position.y == position.y && leaf.Position.x >= position.x) { leaf.Position += Vector2.right * 320f; } }
            Connect(parent, Leaf(graph, PlayerBehaviorStep.Regroup, position.x, position.y));
            graph.SetAssetDirty();
        }

        // Resources에는 편집 전용 타입이 아닌 실제 런타임 그래프/블랙보드만 저장한다
        private static void ExportRuntime(BehaviorAuthoringGraph graph)
        {
            BehaviorGraph source = graph.BuildRuntimeGraph();
            if (source == null) { throw new InvalidOperationException("Player BT compile failed"); }
            BehaviorGraph runtime = AssetDatabase.LoadAssetAtPath<BehaviorGraph>(RuntimePath);
            if (runtime == null)
            {
                runtime = ScriptableObject.CreateInstance<BehaviorGraph>();
                AssetDatabase.CreateAsset(runtime, RuntimePath);
            }
            EditorUtility.CopySerialized(source, runtime);
            runtime.name = "PlayerBehavior";
            var remap = new System.Collections.Generic.Dictionary<UnityEngine.Object, UnityEngine.Object>();
            remap[source] = runtime;
            var existing = AssetDatabase.LoadAllAssetsAtPath(RuntimePath);
            foreach (var blackboard in AssetDatabase.LoadAllAssetsAtPath(Path).OfType<RuntimeBlackboardAsset>())
            {
                var copy = existing.OfType<RuntimeBlackboardAsset>().FirstOrDefault(b => b.name == blackboard.name);
                if (copy == null)
                {
                    copy = ScriptableObject.CreateInstance<RuntimeBlackboardAsset>();
                    AssetDatabase.AddObjectToAsset(copy, runtime);
                }
                EditorUtility.CopySerialized(blackboard, copy);
                copy.name = blackboard.name;
                remap[blackboard] = copy;
            }
            foreach (var output in remap.Values)
            {
                var serialized = new SerializedObject(output);
                var property = serialized.GetIterator();
                var visited = new System.Collections.Generic.HashSet<long>();
                bool enterChildren = true;
                int propertyCount = 0;
                while (property.Next(enterChildren))
                {
                    if (++propertyCount > 20000) { throw new InvalidOperationException("BT export traversal exceeded limit"); }
                    enterChildren = property.propertyType != SerializedPropertyType.String;
                    if (property.propertyType == SerializedPropertyType.ManagedReference)
                    { enterChildren = visited.Add(property.managedReferenceId); }
                    if (property.propertyType != SerializedPropertyType.ObjectReference) { continue; }
                    var value = property.objectReferenceValue;
                    if (value == null) { continue; }
                    if (remap.TryGetValue(value, out var replacement)) { property.objectReferenceValue = replacement; }
                    else if (value is BehaviorGraphDebugInfo) { property.objectReferenceValue = null; }
                    else if (AssetDatabase.GetAssetPath(value) == Path)
                    { throw new InvalidOperationException("Unexported BT dependency: " + value.GetType().Name); }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(output);
            }
            graph.SaveAsset();
            AssetDatabase.SaveAssets();
        }

        private static NodeModel Create(BehaviorAuthoringGraph graph, Type runtime, Type model, float x, float y)
        {
            return graph.CreateNode(model, new Vector2(x, y), args: new object[] { Unity.Behavior.NodeRegistry.GetInfo(runtime) });
        }

        private static NodeModel Leaf(BehaviorAuthoringGraph graph, PlayerBehaviorStep step, float x, float y)
        {
            var node = (ActionNodeModel)Create(graph, typeof(PlayerBehaviorAction), typeof(ActionNodeModel), x, y);
            node.SetField("Step", step);
            return node;
        }

        private static void Connect(NodeModel parent, NodeModel child)
        {
            parent.OutputPortModels.First().ConnectTo(child.InputPortModels.First());
        }
    }
    public sealed class PlayerBehaviorBuildPreparation : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        { PlayerBehaviorGraphAssets.EnsureCreated(); }
    }
}
