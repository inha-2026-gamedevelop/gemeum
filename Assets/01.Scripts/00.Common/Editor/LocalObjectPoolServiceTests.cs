// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

namespace Geumeum.Common.Tests
{
    public class LocalObjectPoolServiceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private GameObject _host;
        private GameObject _prefab;
        private LocalObjectPoolService _pool;

        /****************************************
        *                 Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("LocalObjectPoolServiceTests");
            _pool = _host.AddComponent<LocalObjectPoolService>();

            // EditMode에서는 Awake가 돌지 않아 풀 루트를 직접 만든다
            typeof(LocalObjectPoolService)
                .GetMethod("OnSingletonAwake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_pool, null);

            _prefab = new GameObject("PooledVisual", typeof(LocalPoolRoot));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_prefab);
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void RentSkipsInstanceDestroyedWhilePooled()
        {
            // 씬 전환에 플레이어와 함께 파괴된 인스턴스가 풀에 남은 상황 (보스 입장 후 호스트 연결 끊김)
            _pool.Prewarm(_prefab, 1, 4);
            GameObject first = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(_pool.TryReturn(first));
            Object.DestroyImmediate(first);

            GameObject second = null;
            Assert.DoesNotThrow(() => second = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity));

            Assert.IsTrue(second != null);
            Assert.IsTrue(second.activeSelf);
            Assert.IsTrue(_pool.TryReturn(second));
            Object.DestroyImmediate(second);
        }

        [Test]
        public void ReturnedInstanceIsRentedAgain()
        {
            _pool.Prewarm(_prefab, 1, 4);
            GameObject first = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(_pool.TryReturn(first));

            GameObject second = _pool.Rent(_prefab, Vector3.one, Quaternion.identity);

            Assert.AreSame(first, second);
            Assert.AreEqual(Vector3.one, second.transform.position);
            Object.DestroyImmediate(second);
        }
    }
}
