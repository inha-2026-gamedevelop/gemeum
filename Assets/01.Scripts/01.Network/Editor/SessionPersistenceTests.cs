// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.Network.Tests
{
    /// <summary> 세션 매니저의 씬 해시 처리 전후 수명과 초기화 구독을 검증한다 </summary>
    public sealed class SessionPersistenceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /****************************************
        *              Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            SessionPersistence.ResetStatic();
        }

        [TearDown]
        public void TearDown()
        {
            SessionPersistence.ResetStatic();

            for (int i = 0; i < _createdObjects.Count; ++i)
            {
                if (_createdObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void AwakeRegistersWithoutMovingObjectBeforeSceneLoaded()
        {
            GameObject host = new GameObject("SessionPersistenceTests_Awake");
            _createdObjects.Add(host);
            SessionPersistence persistence = host.AddComponent<SessionPersistence>();

            InvokeAwake(persistence);

            Assert.AreNotEqual("DontDestroyOnLoad", host.scene.name);
        }

        [Test]
        public void SceneCollectionIncludesInactiveDescendants()
        {
            GameObject root = new GameObject("SessionPersistenceTests_Root");
            _createdObjects.Add(root);

            GameObject child = new GameObject("SessionPersistenceTests_Inactive");
            child.transform.SetParent(root.transform);
            child.SetActive(false);
            SessionPersistence persistence = child.AddComponent<SessionPersistence>();

            SessionPersistence[] found = CollectScenePersistences(SceneManager.GetActiveScene());

            CollectionAssert.Contains(found, persistence);
        }

        [Test]
        public void SceneLoadedSubscriptionIsIdempotentAndResettable()
        {
            InvokeStatic("RegisterSceneLoaded");
            InvokeStatic("RegisterSceneLoaded");

            Assert.IsTrue(GetHookedState());

            SessionPersistence.ResetStatic();

            Assert.IsFalse(GetHookedState());
        }

        [Test]
        public void OnDestroyRemovesRegisteredInstance()
        {
            GameObject host = new GameObject("SessionPersistenceTests_Destroy");
            _createdObjects.Add(host);
            SessionPersistence persistence = host.AddComponent<SessionPersistence>();

            InvokeAwake(persistence);
            InvokeOnDestroy(persistence);
            Object.DestroyImmediate(host);

            Assert.IsFalse(GetInstances().ContainsKey("SessionPersistenceTests_Destroy"));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void InvokeAwake(SessionPersistence persistence)
        {
            MethodInfo method = typeof(SessionPersistence).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(persistence, null);
        }

        private static void InvokeOnDestroy(SessionPersistence persistence)
        {
            MethodInfo method = typeof(SessionPersistence).GetMethod(
                "OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(persistence, null);
        }

        private static void InvokeStatic(string methodName)
        {
            MethodInfo method = typeof(SessionPersistence).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }

        private static SessionPersistence[] CollectScenePersistences(Scene scene)
        {
            MethodInfo method = typeof(SessionPersistence).GetMethod(
                "CollectScenePersistences", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (SessionPersistence[]) method.Invoke(null, new object[] { scene });
        }

        private static bool GetHookedState()
        {
            FieldInfo field = typeof(SessionPersistence).GetField(
                "G_SceneLoadedHooked", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (bool) field.GetValue(null);
        }

        private static Dictionary<string, SessionPersistence> GetInstances()
        {
            FieldInfo field = typeof(SessionPersistence).GetField(
                "G_Instances", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (Dictionary<string, SessionPersistence>) field.GetValue(null);
        }
    }
}
