// System
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Data;

namespace Geumeum.Common.Tests
{
    public sealed class SfxPlayerTests
    {
        [System.Serializable]
        private sealed class SoundSnapshot
        {
            public SfxPlayer.PlayRecord lastSound;
        }

        private GameObject _host;
        private AudioSource _source;
        private SoundCatalogSO _catalog;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SfxPlayerTests");
            _source = _host.AddComponent<AudioSource>();
            _catalog = ScriptableObject.CreateInstance<SoundCatalogSO>();
            SfxPlayer.ResetStatic();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_catalog);
            SfxPlayer.ResetStatic();
        }

        [TestCase(SoundKey.StaminaEmpty)]
        [TestCase(SoundKey.PerfectDodge)]
        public void PersonalFeedbackRejectsRemoteAndBotEmitters(SoundKey key)
        {
            Assert.IsFalse(CanPlay(key, false, false, true));
            Assert.IsTrue(CanPlay(key, true, false, true));
            Assert.IsTrue(CanPlay(key, true, true, true));
        }

        [TestCase(SoundKey.MeleeHit)]
        [TestCase(SoundKey.Roll)]
        [TestCase(SoundKey.PlayerHurt)]
        [TestCase(SoundKey.BossPhaseShift)]
        [TestCase(SoundKey.PerfectDodge)]
        public void DedicatedServerRejectsPlayback(SoundKey key)
        {
            Assert.IsFalse(CanPlay(key, false, true, false));
            Assert.IsFalse(CanPlay(key, true, true, false));
        }

        [TestCase(SoundKey.MeleeHit)]
        [TestCase(SoundKey.Roll)]
        [TestCase(SoundKey.PlayerHurt)]
        [TestCase(SoundKey.BossPhaseShift)]
        public void RemoteCombatAndWorldEventsRemainAudibleOnClientsAndHost(SoundKey key)
        {
            Assert.IsTrue(CanPlay(key, false, false, true));
            Assert.IsTrue(CanPlay(key, false, true, true));
        }

        [Test]
        public void RemotePlayerSoundUsesWorldPositionEvenWhenCatalogMarksIt2D()
        {
            Vector3 position = new Vector3(50f, 2f, -9f);
            Configure(SoundKey.MeleeSwing1, SoundChannel.PlayerNormal, true, false, position);

            Assert.AreEqual(1f, _source.spatialBlend);
            Assert.AreEqual(position, _source.transform.position);
            Assert.AreEqual(AudioRolloffMode.Linear, _source.rolloffMode);
            Assert.AreEqual(_catalog.SfxMinDistance, _source.minDistance);
            Assert.AreEqual(_catalog.SfxMaxDistance, _source.maxDistance);
            Assert.AreEqual(1f + _catalog.RemotePitchOffset, _source.pitch, 0.0001f);
        }

        [Test]
        public void PositionAudioUsesCatalogDistancesInsteadOfAudioSourceDefaults()
        {
            SetDistance("_sfxMinDistance", 2f);
            SetDistance("_sfxMaxDistance", 18f);
            Configure(SoundKey.Roll, SoundChannel.PlayerNormal, false, true, Vector3.one);

            Assert.AreEqual(1f, _source.spatialBlend);
            Assert.AreEqual(2f, _source.minDistance);
            Assert.AreEqual(18f, _source.maxDistance);
            Assert.AreEqual(Vector3.one, _source.transform.position);
            Assert.AreEqual(1f, _source.pitch);
        }

        [TestCase(SoundKey.PerfectDodge)]
        [TestCase(SoundKey.StaminaEmpty)]
        public void OwnerFeedbackRemains2DEvenWhenCatalogMarksItPositional(SoundKey key)
        {
            Configure(key, SoundChannel.PlayerCritical, false, true, Vector3.one);

            Assert.AreEqual(0f, _source.spatialBlend);
            Assert.AreEqual(Vector3.zero, _source.transform.position);
        }

        [Test]
        public void GlobalBossEventKeeps2DPlayback()
        {
            Configure(SoundKey.BossGateOpen, SoundChannel.World, true, false, Vector3.one);

            Assert.AreEqual(0f, _source.spatialBlend);
            Assert.AreEqual(1f, _source.pitch);
        }

        [Test]
        public void InvalidCatalogDistanceCannotInvertAttenuationRange()
        {
            SetDistance("_sfxMinDistance", 8f);
            SetDistance("_sfxMaxDistance", 3f);
            Configure(SoundKey.Roll, SoundChannel.PlayerNormal, false, false, Vector3.one);

            Assert.GreaterOrEqual(_source.minDistance, 0f);
            Assert.GreaterOrEqual(_source.maxDistance, _source.minDistance);
        }

        [Test]
        public void SameFrameHostDuplicateIsRejectedWithoutSuppressingOtherEmitter()
        {
            MethodInfo method = typeof(SfxPlayer).GetMethod("IsDuplicate",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsFalse((bool)method.Invoke(null, new object[] { SoundKey.Roll, true, 1u, 0f }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { SoundKey.Roll, true, 1u, 0f }));
            Assert.IsFalse((bool)method.Invoke(null, new object[] { SoundKey.Roll, false, 2u, 0f }));
        }

        [Test]
        public void GameCatalogSetsFiniteLocalCombatRange()
        {
            SoundCatalogSO catalog = AssetDatabase.LoadAssetAtPath<SoundCatalogSO>(
                "Assets/09.Data/Sound/SoundCatalogSO.asset");

            Assert.IsNotNull(catalog);
            Assert.Greater(catalog.SfxMaxDistance, catalog.SfxMinDistance);
            Assert.Less(catalog.SfxMaxDistance, _source.maxDistance);
        }

        [Test]
        public void PlaybackRecordSurvivesVerificationSnapshotJsonSerialization()
        {
            var snapshot = new SoundSnapshot
            {
                lastSound = new SfxPlayer.PlayRecord
                {
                    Key = SoundKey.MeleeSwing1,
                    EmitterId = 42u,
                    IsLocalPlayer = false,
                    Is2D = false,
                    GroupName = "SFX_Remote",
                    Position = new Vector3(35f, 2f, 5f),
                    MinDistance = 1f,
                    MaxDistance = 25f,
                },
            };

            string json = JsonUtility.ToJson(snapshot);
            StringAssert.Contains("\"lastSound\"", json);
            SoundSnapshot restored = JsonUtility.FromJson<SoundSnapshot>(json);

            Assert.AreEqual(snapshot.lastSound.Key, restored.lastSound.Key);
            Assert.AreEqual(snapshot.lastSound.EmitterId, restored.lastSound.EmitterId);
            Assert.AreEqual(snapshot.lastSound.GroupName, restored.lastSound.GroupName);
            Assert.AreEqual(snapshot.lastSound.Position, restored.lastSound.Position);
            Assert.AreEqual(snapshot.lastSound.MinDistance, restored.lastSound.MinDistance);
            Assert.AreEqual(snapshot.lastSound.MaxDistance, restored.lastSound.MaxDistance);
            Assert.IsFalse(restored.lastSound.IsLocalPlayer);
            Assert.IsFalse(restored.lastSound.Is2D);
        }

        private static bool CanPlay(SoundKey key, bool isLocalPlayer, bool isServer, bool isClient)
        {
            return (bool)typeof(SfxPlayer).GetMethod("CanPlayForListener",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { key, isLocalPlayer, isServer, isClient });
        }

        private void Configure(SoundKey key, SoundChannel channel, bool is2D,
            bool isLocalPlayer, Vector3 position)
        {
            var entry = new SoundCatalogSO.Entry
            {
                Key = key,
                Channel = channel,
                Is2D = is2D,
            };
            typeof(SfxPlayer).GetMethod("ConfigureSource",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { _source, _catalog, entry, position, isLocalPlayer });
        }

        private void SetDistance(string field, float value)
        {
            typeof(SoundCatalogSO).GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_catalog, value);
        }
    }
}
