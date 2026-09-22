using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Geumeum.Art.EditorTools
{
    /// <summary> 원본 FBX를 유지하고 양손 관절과 파지용 스킨을 별도 에셋으로 만든다 </summary>
    public static class WeaponGripRigBaker
    {
        [Serializable] public sealed class RigData
        {
            public string character;
            public string sourceMeshPath;
            public string sourceMeshName;
            public Vector3 bowWristEuler;
            public JointData[] bones;
            public WeightData[] weights;
        }

        [Serializable] public sealed class JointData
        {
            public string name;
            public int parent;
            public Vector3 position;
            public Vector3 axis;
            public Vector3 angles;
            public bool left;
        }

        [Serializable] public sealed class WeightData
        {
            public int vertex;
            public int a;
            public int b;
            public float blend;
            public bool left;
        }

        private const string DATA_PATH = "Assets/09.Data/Presentation/Grip/";

        [MenuItem("Geumeum/Art/Rebuild Player Hand Rigs")]
        public static void Bake()
        {
            foreach (string character in new[] { "Chasa", "Singung", "Munyeo" })
            {
                string path = "Assets/02.Prefabs/Art/Player/" + character + "_Art.prefab";
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Mesh mesh = BuildPreview(contents, character);
                    string meshPath = "Assets/06.Model/Player/" + character + "/" + character + "_GripSkin.asset";
                    Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existing == null)
                    {
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(mesh, existing);
                        contents.GetComponentsInChildren<SkinnedMeshRenderer>()
                            .First(r => r.name == "char1").sharedMesh = existing;
                        UnityEngine.Object.DestroyImmediate(mesh);
                        AssetDatabase.SaveAssetIfDirty(existing);
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        public static Mesh BuildPreview(GameObject model, string character)
        {
            var data = JsonUtility.FromJson<RigData>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                DATA_PATH + character + "-rig.json").text);
            var renderer = model.GetComponentsInChildren<SkinnedMeshRenderer>()
                .First(r => r.name == "char1");
            // 재생성 때도 원본의 정점 순서와 bind pose를 기준으로 한다.
            Mesh source = AssetDatabase.LoadAllAssetsAtPath(data.sourceMeshPath).OfType<Mesh>()
                .First(m => m.name == data.sourceMeshName);
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = character + "_GripSkin";
            var originalBones = renderer.bones.Take(source.bindposes.Length).ToArray();
            int handIndex = Array.FindIndex(originalBones, b => b.name == "RightHand");
            if (handIndex < 0)
            {
                throw new InvalidOperationException(character + ": RightHand missing");
            }

            Transform hand = originalBones[handIndex];
            int leftIndex = Array.FindIndex(originalBones, b => b.name == "LeftHand");
            if (leftIndex < 0)
            {
                throw new InvalidOperationException(character + ": LeftHand missing");
            }

            data = AddLeftHand(data, renderer, source, handIndex, leftIndex);
            var oldGrip = model.GetComponent<PlayerWeaponGrip>();
            if (oldGrip != null)
            {
                UnityEngine.Object.DestroyImmediate(oldGrip);
            }

            foreach (Transform child in hand.Cast<Transform>()
                         .Concat(originalBones[leftIndex].Cast<Transform>()).ToArray())
            {
                if (child.name.StartsWith("RightGrip", StringComparison.Ordinal)
                    || child.name.StartsWith("LeftGrip", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            var bones = new List<Transform>(originalBones);
            var poses = new List<Matrix4x4>(source.bindposes);
            var created = new Transform[data.bones.Length];
            var grip = model.AddComponent<PlayerWeaponGrip>();
            var serialized = new SerializedObject(grip);
            serialized.FindProperty("_wrist").objectReferenceValue = hand;
            serialized.FindProperty("_bowWristEuler").vector3Value = data.bowWristEuler;
            var joints = serialized.FindProperty("_fingerJoints");
            joints.arraySize = data.bones.Length;
            for (int i = 0; i < data.bones.Length; ++i)
            {
                JointData joint = data.bones[i];
                Transform socket = joint.left ? originalBones[leftIndex] : hand;
                Transform bone = new GameObject((joint.left ? "LeftGrip" : "RightGrip") + joint.name).transform;
                bone.SetParent(joint.parent < 0 ? socket : created[joint.parent], false);
                bone.position = socket.TransformPoint(joint.position);
                bone.rotation = socket.rotation;
                created[i] = bone;
                bones.Add(bone);
                // 모델 프리팹의 현재 포즈가 FBX bind pose와 다를 수 있으므로 손의 bind 행렬에서 유도한다.
                poses.Add(Matrix4x4.Translate(-joint.position) * source.bindposes[joint.left ? leftIndex : handIndex]);
                var property = joints.GetArrayElementAtIndex(i);
                property.FindPropertyRelative("Bone").objectReferenceValue = bone;
                property.FindPropertyRelative("RestLocalRotation").quaternionValue = Quaternion.identity;
                property.FindPropertyRelative("CurlAxis").vector3Value = joint.axis.normalized;
                property.FindPropertyRelative("WeaponCurlDegrees").vector3Value = joint.angles;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BoneWeight[] weights = source.boneWeights;
            foreach (WeightData edit in data.weights)
            {
                int socketIndex = edit.left ? leftIndex : handIndex;
                BoneWeight original = weights[edit.vertex];
                var entries = new Dictionary<int, float>();
                int[] indices = { original.boneIndex0, original.boneIndex1, original.boneIndex2, original.boneIndex3 };
                float[] values = { original.weight0, original.weight1, original.weight2, original.weight3 };
                for (int k = 0; k < 4; ++k)
                {
                    if (values[k] <= 0f)
                    {
                        continue;
                    }

                    if (indices[k] != socketIndex)
                    {
                        AddWeight(entries, indices[k], values[k]);
                    }
                    else
                    {
                        AddWeight(entries, edit.a < 0 ? socketIndex : originalBones.Length + edit.a,
                            values[k] * (1f - edit.blend));
                        AddWeight(entries, edit.b < 0 ? socketIndex : originalBones.Length + edit.b,
                            values[k] * edit.blend);
                    }
                }
                var sorted = entries.OrderByDescending(pair => pair.Value).Take(4).ToArray();
                float total = sorted.Sum(pair => pair.Value);
                var result = new BoneWeight();
                if (sorted.Length > 0) { result.boneIndex0 = sorted[0].Key; result.weight0 = sorted[0].Value / total; }
                if (sorted.Length > 1) { result.boneIndex1 = sorted[1].Key; result.weight1 = sorted[1].Value / total; }
                if (sorted.Length > 2) { result.boneIndex2 = sorted[2].Key; result.weight2 = sorted[2].Value / total; }
                if (sorted.Length > 3) { result.boneIndex3 = sorted[3].Key; result.weight3 = sorted[3].Value / total; }
                weights[edit.vertex] = result;
            }
            mesh.boneWeights = weights;
            mesh.bindposes = poses.ToArray();
            renderer.sharedMesh = mesh;
            renderer.bones = bones.ToArray();
            renderer.quality = SkinQuality.Bone4;
            return mesh;
        }

        private static RigData AddLeftHand(RigData data, SkinnedMeshRenderer renderer,
            Mesh source, int rightIndex, int leftIndex)
        {
            // 세 모델의 양손은 같은 원본 형상에서 약간 비대칭이다. 모델 중심면 반사 뒤
            // 가장 가까운 원본 오른손 정점의 관절 가중치를 왼손에 옮긴다. 옷/손목은 그대로 둔다.
            Transform model = renderer.GetComponentInParent<Animator>().transform;
            Matrix4x4 mirror = renderer.transform.worldToLocalMatrix * model.localToWorldMatrix
                * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))
                * model.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Matrix4x4 handMirror = source.bindposes[leftIndex] * mirror * source.bindposes[rightIndex].inverse;
            int count = data.bones.Length;
            var joints = new List<JointData>(data.bones);
            foreach (JointData joint in data.bones)
            {
                joints.Add(new JointData
                {
                    name = joint.name, left = true,
                    parent = joint.parent < 0 ? -1 : joint.parent + count,
                    position = handMirror.MultiplyPoint3x4(joint.position),
                    // 회전축은 반사 시 부호도 뒤집어야 같은 방향으로 손가락이 굽는다.
                    axis = -handMirror.MultiplyVector(joint.axis).normalized,
                    angles = new Vector3(0f, joint.angles.y, 0f)
                });
            }
            var edits = data.weights.ToDictionary(w => w.vertex);
            var weights = new List<WeightData>(data.weights);
            BoneWeight[] original = source.boneWeights;
            Vector3[] vertices = source.vertices;
            int[] candidates = Enumerable.Range(0, vertices.Length)
                .Where(i => original[i].boneIndex0 == rightIndex).ToArray();
            for (int i = 0; i < vertices.Length; ++i)
            {
                if (original[i].boneIndex0 != leftIndex)
                {
                    continue;
                }

                Vector3 reflected = mirror.MultiplyPoint3x4(vertices[i]);
                float minimum = float.PositiveInfinity;
                int nearest = -1;
                foreach (int candidate in candidates)
                {
                    float distance = (vertices[candidate] - reflected).sqrMagnitude;
                    if (distance >= minimum)
                    {
                        continue;
                    }

                    minimum = distance;
                    nearest = candidate;
                }
                if (!edits.TryGetValue(nearest, out WeightData edit))
                {
                    continue;
                }

                weights.Add(new WeightData
                {
                    vertex = i, left = true, blend = edit.blend,
                    a = edit.a < 0 ? -1 : edit.a + count,
                    b = edit.b < 0 ? -1 : edit.b + count
                });
            }
            data.bones = joints.ToArray();
            data.weights = weights.ToArray();
            return data;
        }

        private static void AddWeight(Dictionary<int, float> entries, int index, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            entries.TryGetValue(index, out float existing);
            entries[index] = existing + weight;
        }
    }
}
