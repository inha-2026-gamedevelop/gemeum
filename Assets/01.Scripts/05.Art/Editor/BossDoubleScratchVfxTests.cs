using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Geumeum.Art.Tests
{
    public sealed class BossDoubleScratchVfxTests
    {
        private GameObject _instance;
        private BossDoubleScratchVfx _effect;

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/Art/VFX/Boss_DoubleScratch.prefab");
            Assert.That(prefab, Is.Not.Null);
            _instance = Object.Instantiate(prefab);
            _effect = _instance.GetComponent<BossDoubleScratchVfx>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_instance);
        }

        [Test]
        public void TwoSeparateStrikesHaveAnEmptyGapAndNoResidualGeometry()
        {
            _effect.Sample(-0.1f);
            Assert.That(_effect.VisibleVertexCount, Is.Zero);
            _effect.Sample(0.15f);
            Assert.That(_effect.VisibleVertexCount, Is.GreaterThan(0));
            _effect.Sample(_effect.SecondStrikeDelay - 0.02f);
            Assert.That(_effect.VisibleVertexCount, Is.Zero);
            _effect.Sample(_effect.SecondStrikeDelay + 0.15f);
            Assert.That(_effect.VisibleVertexCount, Is.GreaterThan(0));
            _effect.Sample(_effect.Duration);
            Assert.That(_effect.VisibleVertexCount, Is.Zero);
        }

        [Test]
        public void SeekingAndReplayingReuseOneMeshAndProduceTheSameGeometry()
        {
            _effect.Sample(0.15f);
            Mesh mesh = _instance.GetComponentInChildren<MeshFilter>().sharedMesh;
            Vector3[] expected = mesh.vertices;
            for (int i = 0; i < 10; i++)
            {
                _effect.Play();
                _effect.Sample(1.1f);
                _effect.Stop();
            }
            _effect.Sample(0.15f);
            Assert.That(_instance.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(1));
            Assert.That(_instance.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            CollectionAssert.AreEqual(expected, mesh.vertices);
        }

        [Test]
        public void StopAndDisableClearPresentationWithoutDestroyingReusableRoot()
        {
            _effect.Play();
            _effect.Sample(0.15f);
            _effect.Stop();
            _effect.Stop();
            Assert.That(_effect.IsPlaying, Is.False);
            Assert.That(_effect.VisibleVertexCount, Is.Zero);
            Assert.That(_instance.activeSelf, Is.True);
            _effect.Play();
            Assert.That(_effect.IsPlaying, Is.True);
            _effect.Sample(0.15f);
            _instance.SetActive(false);
            Assert.That(_effect.IsPlaying, Is.False);
            Assert.That(_effect.VisibleVertexCount, Is.Zero);
        }

        [Test]
        public void DestroyReleasesGeneratedMeshAndPrefabHasNoGameplayCollider()
        {
            _effect.Sample(0.15f);
            Mesh mesh = _instance.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(_instance.GetComponentsInChildren<Collider>().Length, Is.Zero);
            Assert.That(mesh.bounds.max.z, Is.GreaterThan(3f));
            Object.DestroyImmediate(_instance);
            _instance = null;
            Assert.That(mesh == null, Is.True);
        }
    }
}
