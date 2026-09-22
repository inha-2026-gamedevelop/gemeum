// System
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    public sealed class PlayerWeaponGripTests
    {
        private const float ROTATION_TOLERANCE_DEGREES = 0.001f;

        private static readonly BindingFlags G_PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly Quaternion G_FingerRest =
            Quaternion.Euler(4f, 8f, 12f);
        private static readonly Quaternion G_WristAnimated =
            Quaternion.Euler(11f, 17f, 23f);

        private static readonly Vector3 G_FingerCurl = new Vector3(10f, 20f, 30f);
        private static readonly Vector3 G_SwordWrist = new Vector3(2f, 0f, 0f);
        private static readonly Vector3 G_BowWrist = new Vector3(0f, 3f, 0f);
        private static readonly Vector3 G_BellWrist = new Vector3(0f, 0f, 4f);

        private GameObject       _root;
        private Transform        _finger;
        private Transform        _wrist;
        private PlayerWeaponGrip _grip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PlayerWeaponGripTests");
            _finger = new GameObject("Finger").transform;
            _finger.SetParent(_root.transform, false);
            _finger.localRotation = G_FingerRest;

            _wrist = new GameObject("Wrist").transform;
            _wrist.SetParent(_root.transform, false);
            _wrist.localRotation = G_WristAnimated;

            _grip = _root.AddComponent<PlayerWeaponGrip>();
            ConfigureSerializedFields(G_SwordWrist, G_BowWrist, G_BellWrist);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void GripStartsInactive()
        {
            Quaternion fingerBefore = Quaternion.Euler(31f, 37f, 41f);
            Quaternion wristBefore = Quaternion.Euler(43f, 47f, 53f);
            _finger.localRotation = fingerBefore;
            _wrist.localRotation = wristBefore;

            InvokeLifecycle("LateUpdate");

            AssertRotation(fingerBefore, _finger.localRotation);
            AssertRotation(wristBefore, _wrist.localRotation);
        }

        [TestCase(WeaponKind.Sword, 10f, 2f, 0f, 0f)]
        [TestCase(WeaponKind.Bow, 20f, 0f, 3f, 0f)]
        [TestCase(WeaponKind.Bell, 30f, 0f, 0f, 4f)]
        public void ConfigureWeaponAppliesItsFingerAndWristAngles(
            WeaponKind weapon, float fingerDegrees,
            float wristX, float wristY, float wristZ)
        {
            _grip.ConfigureWeapon(weapon);

            InvokeLifecycle("LateUpdate");

            AssertRotation(G_FingerRest * Quaternion.AngleAxis(fingerDegrees, Vector3.right),
                _finger.localRotation);
            AssertRotation(G_WristAnimated * Quaternion.Euler(wristX, wristY, wristZ),
                _wrist.localRotation);
        }

        [Test]
        public void ClearGripAndDisableRestoreOriginalRotations()
        {
            _grip.ConfigureWeapon(WeaponKind.Bow);
            InvokeLifecycle("LateUpdate");

            _grip.ClearGrip();
            AssertRotation(G_FingerRest, _finger.localRotation);
            AssertRotation(G_WristAnimated, _wrist.localRotation);

            Quaternion nextAnimated = Quaternion.Euler(61f, 67f, 71f);
            _wrist.localRotation = nextAnimated;
            _grip.ConfigureWeapon(WeaponKind.Bell);
            InvokeLifecycle("LateUpdate");
            InvokeLifecycle("OnDisable");

            AssertRotation(G_FingerRest, _finger.localRotation);
            AssertRotation(nextAnimated, _wrist.localRotation);
        }

        [Test]
        public void WristOffsetDoesNotAccumulateAcrossOneHundredFrames()
        {
            _grip.ConfigureWeapon(WeaponKind.Bow);
            Quaternion expected = G_WristAnimated * Quaternion.Euler(G_BowWrist);

            for (int i = 0; i < 100; ++i)
            {
                InvokeLifecycle("Update");
                AssertRotation(G_WristAnimated, _wrist.localRotation);

                InvokeLifecycle("LateUpdate");
                AssertRotation(expected, _wrist.localRotation);
            }

            InvokeLifecycle("Update");
            AssertRotation(G_WristAnimated, _wrist.localRotation);
        }

        [Test]
        public void WristOffsetUsesTheLatestAnimatorRotation()
        {
            _grip.ConfigureWeapon(WeaponKind.Bow);
            InvokeLifecycle("LateUpdate");
            InvokeLifecycle("Update");

            Quaternion nextAnimated = Quaternion.Euler(73f, 79f, 83f);
            _wrist.localRotation = nextAnimated;
            InvokeLifecycle("LateUpdate");

            AssertRotation(nextAnimated * Quaternion.Euler(G_BowWrist),
                _wrist.localRotation);

            InvokeLifecycle("Update");
            AssertRotation(nextAnimated, _wrist.localRotation);
        }

        [Test]
        public void ZeroWristOffsetsNeverWriteTheWrist()
        {
            ConfigureSerializedFields(Vector3.zero, Vector3.zero, Vector3.zero);
            _grip.ConfigureWeapon(WeaponKind.Sword);
            Quaternion first = Quaternion.Euler(89f, 97f, 101f);
            _wrist.localRotation = first;

            InvokeLifecycle("LateUpdate");
            AssertRotation(first, _wrist.localRotation);

            Quaternion second = Quaternion.Euler(103f, 107f, 109f);
            _wrist.localRotation = second;
            InvokeLifecycle("Update");
            AssertRotation(second, _wrist.localRotation);
        }

        [Test]
        public void WeaponSwitchRestoresPreviousOffsetBeforeApplyingTheNextOne()
        {
            _grip.ConfigureWeapon(WeaponKind.Sword);
            InvokeLifecycle("LateUpdate");
            AssertRotation(G_FingerRest * Quaternion.AngleAxis(
                G_FingerCurl.x, Vector3.right), _finger.localRotation);
            AssertRotation(G_WristAnimated * Quaternion.Euler(G_SwordWrist),
                _wrist.localRotation);

            _grip.ConfigureWeapon(WeaponKind.Bow);
            AssertRotation(G_WristAnimated, _wrist.localRotation);
            InvokeLifecycle("LateUpdate");
            AssertRotation(G_FingerRest * Quaternion.AngleAxis(
                G_FingerCurl.y, Vector3.right), _finger.localRotation);
            AssertRotation(G_WristAnimated * Quaternion.Euler(G_BowWrist),
                _wrist.localRotation);

            _grip.ConfigureWeapon(WeaponKind.Bell);
            AssertRotation(G_WristAnimated, _wrist.localRotation);
            InvokeLifecycle("LateUpdate");
            AssertRotation(G_FingerRest * Quaternion.AngleAxis(
                G_FingerCurl.z, Vector3.right), _finger.localRotation);
            AssertRotation(G_WristAnimated * Quaternion.Euler(G_BellWrist),
                _wrist.localRotation);
        }

        [TestCase("Locomotion", false, TestName = "ProductionBowWrist_Idle")]
        [TestCase("BowHold", true, TestName = "ProductionBowWrist_BowHold")]
        [TestCase("BowStab", false, TestName = "ProductionBowWrist_BowStab")]
        [TestCase("Roll", false, TestName = "ProductionBowWrist_Roll")]
        public void ProductionBowWristOffsetIsLimitedToAimStates(
            string stateName, bool expectsWristOffset)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/Art/Player/Chasa_Art.prefab");
            RuntimeAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/05.Animator/BowLocomotion.controller");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);

            GameObject model = Object.Instantiate(prefab);
            try
            {
                Animator animator = model.GetComponent<Animator>();
                PlayerWeaponGrip grip = model.GetComponent<PlayerWeaponGrip>();
                Assert.That(animator, Is.Not.Null);
                Assert.That(grip, Is.Not.Null);

                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                animator.Play("Base Layer." + stateName, 0, 0f);
                animator.Update(0.001f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                    Is.EqualTo(Animator.StringToHash(stateName)), stateName);

                var serialized = new SerializedObject(grip);
                Transform wrist = serialized.FindProperty("_wrist")
                    .objectReferenceValue as Transform;
                Vector3 bowWristEuler = serialized.FindProperty("_bowWristEuler")
                    .vector3Value;
                Assert.That(wrist, Is.Not.Null);
                Assert.That(bowWristEuler, Is.Not.EqualTo(Vector3.zero));

                Quaternion animatedRotation = wrist.localRotation;
                grip.ConfigureWeapon(WeaponKind.Bow);
                InvokeLifecycle(grip, "LateUpdate");

                Quaternion expected = animatedRotation;
                if (expectsWristOffset)
                {
                    expected *= Quaternion.Euler(bowWristEuler);
                }

                AssertRotation(expected, wrist.localRotation);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void GripRunsAfterAnimationAndBeforeCombatPresentation()
        {
            DefaultExecutionOrder animationOrder =
                typeof(PlayerAnimationController)
                    .GetCustomAttribute<DefaultExecutionOrder>();
            DefaultExecutionOrder gripOrder =
                typeof(PlayerWeaponGrip)
                    .GetCustomAttribute<DefaultExecutionOrder>();
            DefaultExecutionOrder presentationOrder =
                typeof(PlayerCombatPresentation)
                    .GetCustomAttribute<DefaultExecutionOrder>();

            Assert.That(animationOrder, Is.Null,
                "PlayerAnimationController must keep Unity's default order 0");
            Assert.That(gripOrder, Is.Not.Null);
            Assert.That(presentationOrder, Is.Not.Null);
            Assert.That(gripOrder.order, Is.EqualTo(50));
            Assert.That(presentationOrder.order, Is.EqualTo(100));
            Assert.That(0, Is.LessThan(gripOrder.order));
            Assert.That(gripOrder.order, Is.LessThan(presentationOrder.order));
        }

        private void ConfigureSerializedFields(Vector3 swordWrist,
            Vector3 bowWrist, Vector3 bellWrist)
        {
            var serialized = new SerializedObject(_grip);
            SerializedProperty joints = serialized.FindProperty("_fingerJoints");
            joints.arraySize = 1;

            SerializedProperty joint = joints.GetArrayElementAtIndex(0);
            joint.FindPropertyRelative("Bone").objectReferenceValue = _finger;
            joint.FindPropertyRelative("RestLocalRotation").quaternionValue = G_FingerRest;
            joint.FindPropertyRelative("CurlAxis").vector3Value = Vector3.right;
            joint.FindPropertyRelative("WeaponCurlDegrees").vector3Value = G_FingerCurl;

            serialized.FindProperty("_wrist").objectReferenceValue = _wrist;
            serialized.FindProperty("_swordWristEuler").vector3Value = swordWrist;
            serialized.FindProperty("_bowWristEuler").vector3Value = bowWrist;
            serialized.FindProperty("_bellWristEuler").vector3Value = bellWrist;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void InvokeLifecycle(string methodName)
        {
            InvokeLifecycle(_grip, methodName);
        }

        private static void InvokeLifecycle(PlayerWeaponGrip grip,
            string methodName)
        {
            MethodInfo method = typeof(PlayerWeaponGrip).GetMethod(methodName,
                G_PrivateInstance);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(grip, null);
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual)
        {
            Assert.That(Quaternion.Angle(expected, actual),
                Is.LessThan(ROTATION_TOLERANCE_DEGREES));
        }
    }
}
