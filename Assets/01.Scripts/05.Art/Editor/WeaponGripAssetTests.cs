// System
using System;
using System.Collections.Generic;
using System.Linq;

// Unity
using UnityEditor;
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Art.EditorTools;
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    public sealed class WeaponGripAssetTests
    {
        private const string PLAYER_PATH = "Assets/02.Prefabs/Network/Player.prefab";
        private const string GRIP_DATA_PATH = "Assets/09.Data/Presentation/Grip/";
        private const int CHARACTER_COUNT = 3;
        private const int WEAPON_COUNT = 3;
        private const int GRIP_JOINT_COUNT = 30;
        private const int TOTAL_BONE_COUNT = 54;
        private const float WEIGHT_SUM_TOLERANCE = 0.002f;
        private const float REST_POSITION_TOLERANCE = 0.00005f;

        private static readonly string[] G_Characters =
        {
            "Chasa",
            "Singung",
            "Munyeo",
        };

        [Test]
        public void ProductionPlayerHasEveryUniqueCharacterWeaponBinding()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            Assert.That(player, Is.Not.Null);

            PlayerWeaponVisual visual = player.GetComponent<PlayerWeaponVisual>();
            Assert.That(visual, Is.Not.Null);
            SerializedProperty bindings = new SerializedObject(visual)
                .FindProperty("_bindings");
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.arraySize, Is.EqualTo(CHARACTER_COUNT * WEAPON_COUNT));

            var keys = new HashSet<int>();
            for (int i = 0; i < bindings.arraySize; ++i)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(i);
                int character = binding.FindPropertyRelative("Character").intValue;
                int weapon = binding.FindPropertyRelative("Weapon").intValue;
                UnityEngine.Object model = binding.FindPropertyRelative("ModelSource")
                    .objectReferenceValue;

                Assert.That(character, Is.InRange(0, CHARACTER_COUNT - 1), $"binding {i} character");
                Assert.That(weapon, Is.InRange(0, WEAPON_COUNT - 1), $"binding {i} weapon");
                Assert.That(model, Is.Not.Null, $"binding {i} model");
                Assert.That(keys.Add((character * WEAPON_COUNT) + weapon), Is.True,
                    $"duplicate ({(CharacterKind) character}, {(WeaponKind) weapon})");
            }

            for (int character = 0; character < CHARACTER_COUNT; ++character)
            {
                for (int weapon = 0; weapon < WEAPON_COUNT; ++weapon)
                {
                    Assert.That(keys.Contains((character * WEAPON_COUNT) + weapon), Is.True,
                        $"missing ({(CharacterKind) character}, {(WeaponKind) weapon})");
                }
            }
        }

        [TestCase("Chasa")]
        [TestCase("Singung")]
        [TestCase("Munyeo")]
        public void ProductionArtPrefabHasCompleteGripRig(string character)
        {
            GameObject model = LoadArtPrefab(character);
            PlayerWeaponGrip grip = model.GetComponent<PlayerWeaponGrip>();
            Assert.That(grip, Is.Not.Null);

            var serialized = new SerializedObject(grip);
            SerializedProperty joints = serialized.FindProperty("_fingerJoints");
            Assert.That(joints.arraySize, Is.EqualTo(GRIP_JOINT_COUNT));

            Transform wrist = serialized.FindProperty("_wrist").objectReferenceValue as Transform;
            Assert.That(wrist, Is.Not.Null);
            Assert.That(wrist.name, Is.EqualTo("RightHand"));
            Assert.That(wrist.IsChildOf(model.transform), Is.True);

            var jointSet = new HashSet<Transform>();
            int rightCount = 0;
            int leftCount = 0;
            for (int i = 0; i < joints.arraySize; ++i)
            {
                SerializedProperty joint = joints.GetArrayElementAtIndex(i);
                Transform bone = joint.FindPropertyRelative("Bone")
                    .objectReferenceValue as Transform;
                Vector3 axis = joint.FindPropertyRelative("CurlAxis").vector3Value;
                Quaternion rest = joint.FindPropertyRelative("RestLocalRotation")
                    .quaternionValue;

                Assert.That(bone, Is.Not.Null, $"joint {i}");
                Assert.That(bone.IsChildOf(model.transform), Is.True, bone.name);
                Assert.That(jointSet.Add(bone), Is.True, $"duplicate {bone.name}");
                Assert.That(IsFinite(axis), Is.True, bone.name);
                Assert.That(axis.sqrMagnitude, Is.EqualTo(1f).Within(0.001f), bone.name);
                Assert.That(IsFinite(rest), Is.True, bone.name);
                Assert.That(Quaternion.Angle(Quaternion.identity, rest),
                    Is.LessThan(0.001f), bone.name);

                if (bone.name.StartsWith("RightGrip", StringComparison.Ordinal))
                {
                    ++rightCount;
                }
                else if (bone.name.StartsWith("LeftGrip", StringComparison.Ordinal))
                {
                    ++leftCount;
                }
                else
                {
                    Assert.Fail("Unexpected grip joint name: " + bone.name);
                }
            }

            Assert.That(rightCount, Is.EqualTo(GRIP_JOINT_COUNT / 2));
            Assert.That(leftCount, Is.EqualTo(GRIP_JOINT_COUNT / 2));

            SkinnedMeshRenderer renderer = FindBodyRenderer(model);
            Mesh source = LoadSourceMesh(character);
            Assert.That(renderer.sharedMesh, Is.Not.Null);
            Assert.That(renderer.sharedMesh.name, Is.EqualTo(character + "_GripSkin"));
            Assert.That(renderer.bones.Length, Is.EqualTo(TOTAL_BONE_COUNT));
            Assert.That(renderer.sharedMesh.bindposes.Length, Is.EqualTo(TOTAL_BONE_COUNT));
            Assert.That(renderer.bones.Length,
                Is.EqualTo(source.bindposes.Length + GRIP_JOINT_COUNT));
            Assert.That(renderer.bones.All(bone => bone != null), Is.True);
            Assert.That(jointSet.All(joint => renderer.bones.Contains(joint)), Is.True);
            foreach (Matrix4x4 bindPose in renderer.sharedMesh.bindposes)
            {
                Assert.That(IsFinite(bindPose), Is.True);
            }
        }

        [TestCase("Chasa")]
        [TestCase("Singung")]
        [TestCase("Munyeo")]
        public void GripSkinPreservesSourceGeometryAndUv(string character)
        {
            Mesh source = LoadSourceMesh(character);
            Mesh grip = LoadGripMesh(character);

            Assert.That(grip.vertexCount, Is.EqualTo(source.vertexCount));
            Assert.That(grip.subMeshCount, Is.EqualTo(source.subMeshCount));
            for (int subMesh = 0; subMesh < source.subMeshCount; ++subMesh)
            {
                Assert.That(grip.GetTopology(subMesh), Is.EqualTo(source.GetTopology(subMesh)));
                CollectionAssert.AreEqual(source.GetIndices(subMesh), grip.GetIndices(subMesh),
                    $"{character} submesh {subMesh}");
            }

            for (int channel = 0; channel < 8; ++channel)
            {
                var sourceUv = new List<Vector4>();
                var gripUv = new List<Vector4>();
                source.GetUVs(channel, sourceUv);
                grip.GetUVs(channel, gripUv);
                Assert.That(gripUv.Count, Is.EqualTo(sourceUv.Count),
                    $"{character} uv{channel}");
                for (int i = 0; i < sourceUv.Count; ++i)
                {
                    Assert.That(gripUv[i], Is.EqualTo(sourceUv[i]),
                        $"{character} uv{channel} vertex {i}");
                }
            }
        }

        [TestCase("Chasa")]
        [TestCase("Singung")]
        [TestCase("Munyeo")]
        public void GripSkinWeightsAreValidAndNonHandVerticesStayUnchanged(
            string character)
        {
            GameObject model = LoadArtPrefab(character);
            SkinnedMeshRenderer renderer = FindBodyRenderer(model);
            Mesh source = LoadSourceMesh(character);
            Mesh grip = renderer.sharedMesh;
            BoneWeight[] sourceWeights = source.boneWeights;
            BoneWeight[] gripWeights = grip.boneWeights;

            Assert.That(sourceWeights.Length, Is.EqualTo(source.vertexCount));
            Assert.That(gripWeights.Length, Is.EqualTo(grip.vertexCount));

            int originalBoneCount = source.bindposes.Length;
            Transform[] originalBones = renderer.bones.Take(originalBoneCount).ToArray();
            int rightHand = Array.FindIndex(originalBones, bone => bone.name == "RightHand");
            int leftHand = Array.FindIndex(originalBones, bone => bone.name == "LeftHand");
            Assert.That(rightHand, Is.GreaterThanOrEqualTo(0));
            Assert.That(leftHand, Is.GreaterThanOrEqualTo(0));

            for (int i = 0; i < gripWeights.Length; ++i)
            {
                BoneWeight weight = gripWeights[i];
                AssertWeightIndicesInRange(weight, renderer.bones.Length,
                    $"{character} vertex {i}");
                float total = weight.weight0 + weight.weight1 +
                    weight.weight2 + weight.weight3;
                Assert.That(IsFinite(total), Is.True, $"{character} vertex {i}");
                Assert.That(total, Is.EqualTo(1f).Within(WEIGHT_SUM_TOLERANCE),
                    $"{character} vertex {i}");

                if (!HasBoneInfluence(sourceWeights[i], rightHand) &&
                    !HasBoneInfluence(sourceWeights[i], leftHand))
                {
                    AssertBoneWeightEqual(sourceWeights[i], weight,
                        $"{character} non-hand vertex {i}");
                }
            }
        }

        [TestCase("Chasa")]
        [TestCase("Singung")]
        [TestCase("Munyeo")]
        public void AddedGripBonesDoNotDeformTheRestPose(string character)
        {
            GameObject prefab = LoadArtPrefab(character);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                SkinnedMeshRenderer renderer = FindBodyRenderer(instance);
                Mesh source = LoadSourceMesh(character);
                Mesh grip = renderer.sharedMesh;
                Transform[] gripBones = renderer.bones;
                Transform[] sourceBones = gripBones.Take(source.bindposes.Length).ToArray();
                BoneWeight[] sourceWeights = source.boneWeights;
                BoneWeight[] gripWeights = grip.boneWeights;
                Vector3[] sourceVertices = source.vertices;
                Vector3[] gripVertices = grip.vertices;

                Assert.That(gripVertices.Length, Is.EqualTo(sourceVertices.Length));
                for (int i = 0; i < sourceVertices.Length; ++i)
                {
                    Vector3 sourcePosition = SkinVertex(sourceVertices[i], sourceWeights[i],
                        sourceBones, source.bindposes);
                    Vector3 gripPosition = SkinVertex(gripVertices[i], gripWeights[i],
                        gripBones, grip.bindposes);
                    Assert.That(Vector3.Distance(sourcePosition, gripPosition),
                        Is.LessThan(REST_POSITION_TOLERANCE),
                        $"{character} vertex {i}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GameObject LoadArtPrefab(string character)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/02.Prefabs/Art/Player/{character}_Art.prefab");
            Assert.That(prefab, Is.Not.Null, character);
            return prefab;
        }

        private static WeaponGripRigBaker.RigData LoadRigData(string character)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GRIP_DATA_PATH + character + "-rig.json");
            Assert.That(asset, Is.Not.Null, character);
            WeaponGripRigBaker.RigData data =
                JsonUtility.FromJson<WeaponGripRigBaker.RigData>(asset.text);
            Assert.That(data, Is.Not.Null, character);
            return data;
        }

        private static Mesh LoadSourceMesh(string character)
        {
            WeaponGripRigBaker.RigData data = LoadRigData(character);
            Mesh source = AssetDatabase.LoadAllAssetsAtPath(data.sourceMeshPath)
                .OfType<Mesh>().FirstOrDefault(mesh => mesh.name == data.sourceMeshName);
            Assert.That(source, Is.Not.Null,
                $"{character}: {data.sourceMeshPath}/{data.sourceMeshName}");
            return source;
        }

        private static Mesh LoadGripMesh(string character)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                $"Assets/06.Model/Player/{character}/{character}_GripSkin.asset");
            Assert.That(mesh, Is.Not.Null, character);
            return mesh;
        }

        private static SkinnedMeshRenderer FindBodyRenderer(GameObject model)
        {
            SkinnedMeshRenderer renderer = model
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(candidate => candidate.name == "char1");
            Assert.That(renderer, Is.Not.Null);
            return renderer;
        }

        private static Vector3 SkinVertex(Vector3 vertex, BoneWeight weight,
            Transform[] bones, Matrix4x4[] bindPoses)
        {
            Vector3 result = Vector3.zero;
            AddSkinnedPosition(ref result, vertex, weight.boneIndex0,
                weight.weight0, bones, bindPoses);
            AddSkinnedPosition(ref result, vertex, weight.boneIndex1,
                weight.weight1, bones, bindPoses);
            AddSkinnedPosition(ref result, vertex, weight.boneIndex2,
                weight.weight2, bones, bindPoses);
            AddSkinnedPosition(ref result, vertex, weight.boneIndex3,
                weight.weight3, bones, bindPoses);
            return result;
        }

        private static void AddSkinnedPosition(ref Vector3 result, Vector3 vertex,
            int boneIndex, float weight, Transform[] bones, Matrix4x4[] bindPoses)
        {
            if (weight <= 0f)
            {
                return;
            }

            result += (bones[boneIndex].localToWorldMatrix * bindPoses[boneIndex])
                .MultiplyPoint3x4(vertex) * weight;
        }

        private static bool HasBoneInfluence(BoneWeight weight, int boneIndex)
        {
            return ((weight.boneIndex0 == boneIndex) && (weight.weight0 > 0f)) ||
                ((weight.boneIndex1 == boneIndex) && (weight.weight1 > 0f)) ||
                ((weight.boneIndex2 == boneIndex) && (weight.weight2 > 0f)) ||
                ((weight.boneIndex3 == boneIndex) && (weight.weight3 > 0f));
        }

        private static void AssertWeightIndicesInRange(BoneWeight weight,
            int boneCount, string message)
        {
            Assert.That(weight.boneIndex0, Is.InRange(0, boneCount - 1), message);
            Assert.That(weight.boneIndex1, Is.InRange(0, boneCount - 1), message);
            Assert.That(weight.boneIndex2, Is.InRange(0, boneCount - 1), message);
            Assert.That(weight.boneIndex3, Is.InRange(0, boneCount - 1), message);
        }

        private static void AssertBoneWeightEqual(BoneWeight expected,
            BoneWeight actual, string message)
        {
            Assert.That(actual.boneIndex0, Is.EqualTo(expected.boneIndex0), message);
            Assert.That(actual.boneIndex1, Is.EqualTo(expected.boneIndex1), message);
            Assert.That(actual.boneIndex2, Is.EqualTo(expected.boneIndex2), message);
            Assert.That(actual.boneIndex3, Is.EqualTo(expected.boneIndex3), message);
            Assert.That(actual.weight0, Is.EqualTo(expected.weight0), message);
            Assert.That(actual.weight1, Is.EqualTo(expected.weight1), message);
            Assert.That(actual.weight2, Is.EqualTo(expected.weight2), message);
            Assert.That(actual.weight3, Is.EqualTo(expected.weight3), message);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int i = 0; i < 16; ++i)
            {
                if (!IsFinite(value[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
