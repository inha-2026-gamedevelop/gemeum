// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI.Tests
{
    public sealed class EnemyPresentationBindingTests
    {
        private GameObject _enemy;
        private Material _material;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_enemy);
            Object.DestroyImmediate(_material);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParryHighlightReadsCurrentStateWhenBoundOrReenabled(bool reenable)
        {
            _enemy = new GameObject("EnemyPresentationBinding", typeof(NetworkIdentity));
            EnemyController controller = _enemy.AddComponent<EnemyController>();
            var visual = new GameObject("Visual", typeof(MeshRenderer));
            visual.transform.SetParent(_enemy.transform);
            var renderer = visual.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader);
            _material = new Material(shader);
            renderer.sharedMaterial = _material;
            int property = Shader.PropertyToID("_BaseColor");
            Color original = new Color(0.2f, 0.3f, 0.4f, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor(property, original);
            renderer.SetPropertyBlock(block);

            EnemyParryWindowFlash flash = null;
            if (reenable)
            {
                flash = _enemy.AddComponent<EnemyParryWindowFlash>();
                InvokeLifecycle(flash, "Awake");
                InvokeLifecycle(flash, "OnEnable");
                InvokeLifecycle(flash, "OnDisable");
                flash.enabled = false;
            }

            // 구독자가 없는 동안 복제 상태가 바뀐 상황이다. 이벤트를 재발행하지 않는다
            SetState(controller, "_isParryWindowVisuallyOpen", true);
            if (reenable)
            {
                flash.enabled = true;
            }
            else
            {
                flash = _enemy.AddComponent<EnemyParryWindowFlash>();
                InvokeLifecycle(flash, "Awake");
            }
            InvokeLifecycle(flash, "OnEnable");

            renderer.GetPropertyBlock(block);
            AssertColor(new Color(1f, 0.92f, 0.4f, 1f), block.GetColor(property));
            flash.enabled = false;
            InvokeLifecycle(flash, "OnDisable");
            renderer.GetPropertyBlock(block);
            AssertColor(original, block.GetColor(property));

            SetState(controller, "_isParryWindowVisuallyOpen", false);
            flash.enabled = true;
            InvokeLifecycle(flash, "OnEnable");
            renderer.GetPropertyBlock(block);
            AssertColor(original, block.GetColor(property));
            InvokeLifecycle(flash, "OnDisable");
        }

        [Test]
        public void ReadContractExposesLatestReplicatedSnapshotWithoutAnEvent()
        {
            _enemy = new GameObject("EnemyPresentationSnapshot", typeof(NetworkIdentity));
            EnemyController controller = _enemy.AddComponent<EnemyController>();
            SetState(controller, "_visualState", EnemyVisualState.Idle);
            SetState(controller, "_attackVariant", 2);
            SetState(controller, "_isParryWindowVisuallyOpen", true);

            IEnemyPresentationSource source = controller;
            Assert.AreEqual(EnemyVisualState.Idle, source.VisualState);
            Assert.AreEqual(2, source.AttackVariant);
            Assert.IsTrue(source.IsParryWindowVisuallyOpen);
        }

        private static void SetState(EnemyController controller, string name, object value)
        {
            FieldInfo field = typeof(EnemyController).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(controller, value);
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.00001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.00001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.00001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.00001f));
        }

        // EditMode는 일반 MonoBehaviour의 수명 콜백을 자동 실행하지 않는다
        private static void InvokeLifecycle(EnemyParryWindowFlash flash, string name)
        {
            typeof(EnemyParryWindowFlash).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(flash, null);
        }
    }
}
