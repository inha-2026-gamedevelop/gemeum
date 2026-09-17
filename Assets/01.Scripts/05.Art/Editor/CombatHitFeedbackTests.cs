// System
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art.Tests
{
    public sealed class CombatHitFeedbackTests
    {
        private const float TOLERANCE = 0.0001f;

        private static readonly Color G_Original = new Color(0.2f, 0.3f, 0.4f, 1f);
        private static readonly Color G_Parry    = new Color(1f, 0.92f, 0.4f, 1f);
        private static readonly Color G_Damage   = new Color(1f, 0.12f, 0.08f, 1f);

        private GameObject _rendererObject;
        private Material   _material;

        [TearDown]
        public void TearDown()
        {
            if (_rendererObject != null)
            {
                Object.DestroyImmediate(_rendererObject);
            }

            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }
        }

        [Test]
        public void TrailRecordsAroundImpactOnly()
        {
            MeleeSlashView.ResolveRecordWindow(10d, 0.15f, 0.12f, out double from, out double until);
            Assert.That(from, Is.EqualTo(9.85d).Within(TOLERANCE));
            Assert.That(until, Is.EqualTo(10.12d).Within(TOLERANCE));

            MeleeSlashView.ResolveRecordWindow(10d, -1f, -1f, out from, out until);
            Assert.That(from, Is.EqualTo(10d).Within(TOLERANCE));
            Assert.That(until, Is.EqualTo(10d).Within(TOLERANCE));
        }

        [Test]
        public void TrailPointAgesToOneOverTrailSeconds()
        {
            Assert.That(MeleeSlashView.ResolveAge01(1f, 1f, 0.4f), Is.EqualTo(0f).Within(TOLERANCE));
            Assert.That(MeleeSlashView.ResolveAge01(1.2f, 1f, 0.4f), Is.EqualTo(0.5f).Within(TOLERANCE));
            Assert.That(MeleeSlashView.ResolveAge01(2f, 1f, 0.4f), Is.EqualTo(1f).Within(TOLERANCE));
        }

        [Test]
        public void CurveBetweenSamplesPassesThroughRecordedPoints()
        {
            var p0 = new Vector3(-1f, 0f, 0f);
            var p1 = new Vector3(0f, 0f, 1f);
            var p2 = new Vector3(1f, 0f, 0f);
            var p3 = new Vector3(0f, 0f, -1f);

            Assert.That(Vector3.Distance(p1, MeleeSlashView.CatmullRom(p0, p1, p2, p3, 0f)), Is.LessThan(TOLERANCE));
            Assert.That(Vector3.Distance(p2, MeleeSlashView.CatmullRom(p0, p1, p2, p3, 1f)), Is.LessThan(TOLERANCE));
        }

        [Test]
        public void CurveDoesNotOvershootWhenSampleSpacingJumps()
        {
            // 빠른 휘두르기에서는 프레임 사이 이동 거리가 크게 달라진다. 짧은 구간이 긴 이웃에 끌려 튀면 가시가 생긴다
            var p0 = new Vector3(-10f, 0f, 0f);
            var p1 = Vector3.zero;
            var p2 = new Vector3(0.1f, 0f, 0f);
            var p3 = new Vector3(0.1f, 0f, 10f);

            for (int i = 0; i <= 10; ++i)
            {
                Vector3 point = MeleeSlashView.CatmullRom(p0, p1, p2, p3, i / 10f);
                Assert.That(point.x, Is.InRange(-0.05f, 0.15f));
                Assert.That(point.z, Is.InRange(-0.05f, 0.05f));
            }
        }

        [TestCase(0.53f)]
        [TestCase(-0.53f)]
        public void BladeTipIsTheEndFarFromTheHand(float meshOffset)
        {
            // 검 모델은 1.5m 길이 메시가 손 기준 meshOffset만큼 떨어져 있다. 손은 메시 좌표로 -meshOffset이다
            var bounds = new Bounds(Vector3.zero, new Vector3(1.5f, 0.14f, 0.07f));
            var grip = new Vector3(-meshOffset, 0f, 0f);

            Assert.IsTrue(PlayerWeaponVisual.TryResolveBladeEnds(bounds, grip,
                out Vector3 hilt, out Vector3 tip));
            Assert.That(tip.x, Is.EqualTo(Mathf.Sign(meshOffset) * 0.75f).Within(TOLERANCE));
            Assert.That(hilt.x, Is.EqualTo(-Mathf.Sign(meshOffset) * 0.75f).Within(TOLERANCE));
        }

        [Test]
        public void FlatMeshHasNoBlade()
        {
            Assert.IsFalse(PlayerWeaponVisual.TryResolveBladeEnds(
                new Bounds(Vector3.zero, Vector3.zero), Vector3.zero, out _, out _));
        }

        [Test]
        public void TrailFollowsBladePathThenReturnsAfterFading()
        {
            _rendererObject = new GameObject("MeleeSlashTrailTests");
            _rendererObject.AddComponent<LocalPoolRoot>();
            _rendererObject.AddComponent<MeshFilter>();
            _rendererObject.AddComponent<MeshRenderer>();
            PooledVisualLifetime lifetime = _rendererObject.AddComponent<PooledVisualLifetime>();
            MeleeSlashView view = _rendererObject.AddComponent<MeleeSlashView>();
            lifetime.OnRentFromPool();

            var blade = new SweepingBlade();
            var cue = new PlayerActionCue
            {
                ExecutionId        = 1u,
                Kind               = PlayerActionCueKind.Attack,
                Weapon             = WeaponKind.Sword,
                ComboStage         = 1,
                StartGameplayTime  = 0.8d,
                ImpactGameplayTime = 1d,
                EndGameplayTime    = 1.5d,
            };
            view.Begin(in cue, blade, new MeleeSlashView.TrailSettings
            {
                TrailSeconds              = 0.4f,
                RecordBeforeImpactSeconds = 0.2f,
                RecordAfterImpactSeconds  = 0.2f,
                HiltRatio                 = 0.25f,
                BodyColor                 = Color.red,
                EdgeColor                 = Color.white,
            });

            try
            {
                // 기록 구간 전에는 검이 움직여도 그리지 않는다
                blade.AngleDegrees = -60f;
                view.Tick(0.05f, 0.7d);
                Assert.AreEqual(0, view.SampleCount);

                // 기록은 판정 0.2초 뒤(1.2)에 끝나므로 1.15까지 움직인다
                for (int frame = 0; frame <= 7; ++frame)
                {
                    blade.AngleDegrees = -60f + (15f * frame);
                    view.Tick(0.05f, 0.8d + (0.05d * frame));
                }

                Assert.GreaterOrEqual(view.SampleCount, 2);
                Vector3[] vertices = view.TrailMesh.vertices;
                Assert.Greater(vertices.Length, 0);

                // 마지막 꼭짓점 쌍은 가장 최근 검날 위치다
                blade.TryGetBladeSegment(out Vector3 hilt, out Vector3 tip);
                Assert.That(Vector3.Distance(tip, vertices[vertices.Length - 1]), Is.LessThan(TOLERANCE));
                Assert.That(Vector3.Distance(Vector3.Lerp(hilt, tip, 0.25f), vertices[vertices.Length - 2]),
                    Is.LessThan(TOLERANCE));

                // 기록이 끝나고 잔상 시간이 지나면 스스로 풀에 돌아간다
                view.ReleaseCue();
                view.Tick(0.2f, 1.3d);
                Assert.IsTrue(view.IsActive);
                view.Tick(0.3f, 1.6d);
                Assert.IsFalse(view.IsActive);
                Assert.IsFalse(lifetime.IsRented);
            }
            finally
            {
                if (view.TrailMesh != null)
                {
                    Object.DestroyImmediate(view.TrailMesh);
                }
            }
        }

        [Test]
        public void OverlappingTintsRestoreOriginalInEitherOrder()
        {
            Renderer renderer = CreateTintedRenderer();

            RendererTintRegistry.Push(renderer, RendererTintLayer.ParryWindow, G_Parry);
            RendererTintRegistry.Push(renderer, RendererTintLayer.Damage, G_Damage);
            AssertBaseColor(renderer, G_Damage);

            RendererTintRegistry.Pop(renderer, RendererTintLayer.ParryWindow);
            AssertBaseColor(renderer, G_Damage);
            RendererTintRegistry.Pop(renderer, RendererTintLayer.Damage);
            AssertBaseColor(renderer, G_Original);
            Assert.IsFalse(RendererTintRegistry.IsTinted(renderer));

            RendererTintRegistry.Push(renderer, RendererTintLayer.Damage, G_Damage);
            RendererTintRegistry.Push(renderer, RendererTintLayer.ParryWindow, G_Parry);
            AssertBaseColor(renderer, G_Damage);

            RendererTintRegistry.Pop(renderer, RendererTintLayer.Damage);
            AssertBaseColor(renderer, G_Parry);
            RendererTintRegistry.Pop(renderer, RendererTintLayer.ParryWindow);
            AssertBaseColor(renderer, G_Original);
        }

        [Test]
        public void RepeatedPopDoesNotDisturbOtherLayer()
        {
            Renderer renderer = CreateTintedRenderer();

            RendererTintRegistry.Push(renderer, RendererTintLayer.ParryWindow, G_Parry);
            RendererTintRegistry.Pop(renderer, RendererTintLayer.Damage);
            AssertBaseColor(renderer, G_Parry);

            RendererTintRegistry.Pop(renderer, RendererTintLayer.ParryWindow);
            RendererTintRegistry.Pop(renderer, RendererTintLayer.ParryWindow);
            AssertBaseColor(renderer, G_Original);
            Assert.IsFalse(RendererTintRegistry.IsTinted(renderer));
        }

        [Test]
        public void SlashDefinitionRejectsNonFiniteShape()
        {
            PresentationConfigSO config = ScriptableObject.CreateInstance<PresentationConfigSO>();
            try
            {
                Assert.IsTrue(config.ValidateDefinition(out string failure), failure);

                SetPrivateField(config, "_meleeSlashTrailSeconds", float.NaN);
                Assert.IsFalse(config.ValidateDefinition(out _));

                SetPrivateField(config, "_meleeSlashTrailSeconds", 0.45f);
                SetPrivateField(config, "_meleeSlashHiltRatio", 2f);
                Assert.IsFalse(config.ValidateDefinition(out _));

                SetPrivateField(config, "_meleeSlashHiltRatio", 0.25f);
                SetPrivateField(config, "_meleeSlashEdgeColor", new Color(float.PositiveInfinity, 1f, 1f, 1f));
                Assert.IsFalse(config.ValidateDefinition(out _));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private Renderer CreateTintedRenderer()
        {
            _rendererObject = new GameObject("TintRegistryTests", typeof(MeshRenderer));
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader);
            _material = new Material(shader);

            var renderer = _rendererObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", G_Original);
            renderer.SetPropertyBlock(block);
            return renderer;
        }

        private static void AssertBaseColor(Renderer renderer, Color expected)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Color actual = block.GetColor("_BaseColor");
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(TOLERANCE));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(TOLERANCE));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(TOLERANCE));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(TOLERANCE));
        }

        private sealed class SweepingBlade : IWeaponBladeSource
        {
            private const float BLADE_LENGTH = 1.2f;

            public float AngleDegrees;

            public bool TryGetBladeSegment(out Vector3 hiltWorld, out Vector3 tipWorld)
            {
                Vector3 direction = Quaternion.Euler(0f, AngleDegrees, 0f) * Vector3.forward;
                hiltWorld = new Vector3(0f, 1f, 0f) + (direction * 0.3f);
                tipWorld  = hiltWorld + (direction * BLADE_LENGTH);
                return true;
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
