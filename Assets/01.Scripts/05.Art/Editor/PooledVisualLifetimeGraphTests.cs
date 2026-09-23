using System.Reflection;
using Geumeum.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace Geumeum.Art.Tests
{
    public sealed class PooledVisualLifetimeGraphTests
    {
        private const string GRAPH_PATH =
            "Assets/02.Prefabs/Art/VFX/BossScratchV2/BossScratch.vfx";
        private static readonly MethodInfo UpdateMethod =
            typeof(PooledVisualLifetime).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _root;
        private PooledVisualLifetime _lifetime;
        private VisualEffect _first;
        private VisualEffect _second;
        private IRunTimeSource _previousTime;
        private MutableRunTimeSource _time;

        [SetUp]
        public void SetUp()
        {
            VisualEffectAsset asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GRAPH_PATH);
            Assert.That(asset, Is.Not.Null, GRAPH_PATH);
            Assert.That(UpdateMethod, Is.Not.Null);

            _previousTime = RunTimeLocator.Current;
            _time = new MutableRunTimeSource();
            RunTimeLocator.Register(_time);

            _root = new GameObject("Graph lifetime test");
            _root.AddComponent<LocalPoolRoot>();
            _first = CreateGraphChild("First", asset);
            _second = CreateGraphChild("Second", asset);
            _first.initialEventName = "SlashA";
            _second.initialEventName = "SlashB";
            _lifetime = _root.AddComponent<PooledVisualLifetime>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            RunTimeLocator.Unregister(_time);
            if (_previousTime != null)
            {
                RunTimeLocator.Register(_previousTime);
            }
        }

        [Test]
        public void ReturnAndReuseResetGraphStateAndRejectAnOldLease()
        {
            string initialEvent = _first.initialEventName;
            VisualEffectAsset asset = _first.visualEffectAsset;

            _lifetime.OnRentFromPool();
            uint firstLease = _lifetime.LeaseGeneration;
            Assert.That(_first.initialEventID, Is.EqualTo(Shader.PropertyToID("SlashA")));
            _first.pause = true;
            _lifetime.OnReturnToPool();

            Assert.That(_lifetime.IsRented, Is.False);
            Assert.That(_first.pause, Is.False);
            // EditMode does not await VFX GPU readback; aliveParticleCount can be negative here.
            // Particle clearing is verified in the runtime visual check.
            Assert.That(_first.initialEventName, Is.EqualTo(initialEvent));
            Assert.That(_first.visualEffectAsset, Is.SameAs(asset));

            _lifetime.OnRentFromPool();
            Assert.That(_lifetime.LeaseGeneration, Is.Not.EqualTo(firstLease));
            Assert.That(_lifetime.TryStop(firstLease), Is.False);
            Assert.That(_lifetime.IsRented, Is.True);
            Assert.That(_first.pause, Is.False);
            Assert.That(_first.initialEventID, Is.EqualTo(Shader.PropertyToID("SlashA")));
            _lifetime.OnReturnToPool();
        }

        [Test]
        public void RunPauseResumesOnlyGraphsThatItPaused()
        {
            _lifetime.OnRentFromPool();
            _second.pause = true;

            _time.IsPaused = true;
            UpdateMethod.Invoke(_lifetime, null);
            Assert.That(_first.pause, Is.True);
            Assert.That(_second.pause, Is.True);

            _time.IsPaused = false;
            UpdateMethod.Invoke(_lifetime, null);
            Assert.That(_first.pause, Is.False);
            Assert.That(_second.pause, Is.True);
            _lifetime.OnReturnToPool();
        }

        [Test]
        public void LateSeekSimulatesGraphAndKeepsItsRemainingLeasePaused()
        {
            _lifetime.OnRentFromPool();
            _time.IsPaused = true;
            _lifetime.BeginAtProgress(0.5f);

            Assert.That(_lifetime.IsRented, Is.True);
            Assert.That(_first.pause, Is.True);
            Assert.That(GetPrivateField<float>("_remainingSeconds"),
                Is.EqualTo(_lifetime.LifetimeSeconds * 0.5f).Within(0.0001f));

            _time.IsPaused = false;
            UpdateMethod.Invoke(_lifetime, null);
            Assert.That(_first.pause, Is.False);
            _lifetime.OnReturnToPool();
        }

        [Test]
        public void CookLeaseDoesNotExpireAndSuppressionDoesNotLeakToNextRent()
        {
            _lifetime.OnRentFromPool();
            _lifetime.SetAutomaticReturnSuppressed(true);
            SetPrivateField("_remainingSeconds", -1f);
            UpdateMethod.Invoke(_lifetime, null);

            Assert.That(_lifetime.IsRented, Is.True);
            Assert.That(GetPrivateField<float>("_remainingSeconds"), Is.EqualTo(-1f));

            _lifetime.OnReturnToPool();
            _lifetime.OnRentFromPool();
            Assert.That(GetPrivateField<bool>("_automaticReturnSuppressed"), Is.False);
            _lifetime.OnReturnToPool();
        }

        private VisualEffect CreateGraphChild(string name, VisualEffectAsset asset)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            VisualEffect effect = child.AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            return effect;
        }

        private T GetPrivateField<T>(string name)
        {
            FieldInfo field = typeof(PooledVisualLifetime).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(_lifetime);
        }

        private void SetPrivateField(string name, object value)
        {
            FieldInfo field = typeof(PooledVisualLifetime).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(_lifetime, value);
        }

        private sealed class MutableRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds => 0d;
            public double BossElapsedSeconds => 0d;
            public double GameplayElapsedSeconds => 0d;
            public RunPauseReason PauseReasons => IsPaused
                ? RunPauseReason.SceneTransition
                : RunPauseReason.None;
            public bool IsPaused { get; set; }
        }
    }
}
