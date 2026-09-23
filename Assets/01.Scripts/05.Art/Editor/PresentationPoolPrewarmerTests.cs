using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Geumeum.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Geumeum.Art.Tests
{
    public sealed class PresentationPoolPrewarmerTests
    {
        [Test]
        public void DuplicatePrefabPlanUsesLargestInitialAndMaxWithoutDuplicatingPool()
        {
            Type owner = typeof(PresentationPoolPrewarmer);
            Type planType = owner.GetNestedType("PoolPlan", BindingFlags.NonPublic);
            Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(GameObject), planType);
            IDictionary plans = (IDictionary)Activator.CreateInstance(dictionaryType);
            MethodInfo add = owner.GetMethod("AddPlan", BindingFlags.Static | BindingFlags.NonPublic);
            GameObject prefab = new GameObject("SharedVfx");
            try
            {
                add.Invoke(null, new object[] { plans, prefab, 3, 9 });
                add.Invoke(null, new object[] { plans, prefab, 6, 18 });
                add.Invoke(null, new object[] { plans, prefab, 2, 4 });

                Assert.AreEqual(1, plans.Count);
                object plan = plans[prefab];
                Assert.AreEqual(6, planType.GetField("Initial").GetValue(plan));
                Assert.AreEqual(18, planType.GetField("Maximum").GetValue(plan));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void InvalidSerializedPlayerEntryFailsBootCollection()
        {
            GameObject visual = new GameObject("ValidVfx", typeof(LocalPoolRoot),
                typeof(PooledVisualLifetime));
            GameObject owner = new GameObject("Player");
            owner.SetActive(false);
            PlayerVfxController controller = owner.AddComponent<PlayerVfxController>();
            try
            {
                var entries = new List<PlayerVfxEntry>
                {
                    NewEntry("valid", visual, 1, 2),
                    NewEntry("valid", visual, 2, 2)
                };
                typeof(PlayerVfxController).GetField("_entries",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, entries);
                var copied = new List<PlayerVfxEntry>();

                LogAssert.Expect(LogType.Error,
                    "[PlayerVfxController] Boot 목록 1가 유효하지 않다");
                Assert.IsFalse(controller.CopyPrewarmEntries(copied));
                Assert.AreEqual(1, copied.Count);
                Assert.AreSame(visual, copied[0].Prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(visual);
            }
        }

        [Test]
        public void DevSceneWithoutBootDoesNotWait()
        {
            if (SceneManager.GetSceneByName(Constants.Scene.BOOT).isLoaded)
            {
                Assert.Ignore("Boot 씬이 열린 상태에서는 Dev 씬 단독 시작을 검증할 수 없다");
            }
            Type type = typeof(PresentationPoolPrewarmer);
            FieldInfo readyField = type.GetField("_ready", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo failureField = type.GetField("_failure", BindingFlags.Static | BindingFlags.NonPublic);
            object previousReady = readyField.GetValue(null);
            object previousFailure = failureField.GetValue(null);
            try
            {
                readyField.SetValue(null, false);
                failureField.SetValue(null, null);
                Assert.IsTrue(PresentationPoolPrewarmer.WaitForBootAsync().IsCompleted);
            }
            finally
            {
                readyField.SetValue(null, previousReady);
                failureField.SetValue(null, previousFailure);
            }
        }

        [UnityTest]
        public IEnumerator BootGatePropagatesCancellationAndFailure()
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene boot = SceneManager.GetSceneByName(Constants.Scene.BOOT);
            bool renamedScene = !boot.isLoaded;
            string previousName = null;
            if (renamedScene)
            {
                // EditMode runner의 미저장 임시 씬은 Additive 생성을 허용하지 않는다.
                // 실제 에셋 씬의 이름은 바꾸지 않는다.
                if (!previousActiveScene.IsValid() || !previousActiveScene.isLoaded ||
                    !string.IsNullOrEmpty(previousActiveScene.path))
                {
                    Assert.Ignore("미저장 EditMode 테스트 씬이 없어 Boot 게이트 씬 이름을 임시 지정할 수 없다");
                }
                boot = previousActiveScene;
                previousName = boot.name;
                boot.name = Constants.Scene.BOOT;
            }
            GameObject owner = new GameObject("GateTest");
            owner.SetActive(false);
            PresentationPoolPrewarmer prewarmer = owner.AddComponent<PresentationPoolPrewarmer>();
            Type type = typeof(PresentationPoolPrewarmer);
            FieldInfo instanceField = type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo completionField = type.GetField("_completion", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo readyField = type.GetField("_ready", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo failureField = type.GetField("_failure", BindingFlags.Static | BindingFlags.NonPublic);
            object previousInstance = instanceField.GetValue(null);
            object previousCompletion = completionField.GetValue(null);
            object previousReady = readyField.GetValue(null);
            object previousFailure = failureField.GetValue(null);
            var completion = new TaskCompletionSource<bool>();
            try
            {
                instanceField.SetValue(null, prewarmer);
                completionField.SetValue(null, completion);
                readyField.SetValue(null, false);
                failureField.SetValue(null, null);
                using (var cancellation = new CancellationTokenSource())
                {
                    Task wait = PresentationPoolPrewarmer.WaitForBootAsync(cancellation.Token);
                    Assert.IsFalse(wait.IsCompleted);
                    cancellation.Cancel();
                    float deadline = Time.realtimeSinceStartup + 5f;
                    while (!wait.IsCompleted && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }
                    Assert.IsTrue(wait.IsCompleted, "취소가 제한 시간 안에 전파되어야 한다");
                    Assert.IsTrue(wait.IsCanceled);
                }

                completion.SetException(new InvalidOperationException("warmup failed"));
                Task failed = PresentationPoolPrewarmer.WaitForBootAsync();
                float failureDeadline = Time.realtimeSinceStartup + 5f;
                while (!failed.IsCompleted && Time.realtimeSinceStartup < failureDeadline)
                {
                    yield return null;
                }
                Assert.IsTrue(failed.IsCompleted, "실패가 제한 시간 안에 전파되어야 한다");
                Assert.IsTrue(failed.IsFaulted);
                Assert.IsInstanceOf<InvalidOperationException>(failed.Exception.InnerException);
            }
            finally
            {
                instanceField.SetValue(null, previousInstance);
                completionField.SetValue(null, previousCompletion);
                readyField.SetValue(null, previousReady);
                failureField.SetValue(null, previousFailure);
                UnityEngine.Object.DestroyImmediate(owner);
                if (renamedScene)
                {
                    boot.name = previousName;
                }
            }
        }

        private static PlayerVfxEntry NewEntry(string key, GameObject prefab,
            int initial, int maximum)
        {
            PlayerVfxEntry entry = new PlayerVfxEntry();
            Type type = typeof(PlayerVfxEntry);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("_key", flags).SetValue(entry, key);
            type.GetField("_prefab", flags).SetValue(entry, prefab);
            type.GetField("_initialCapacity", flags).SetValue(entry, initial);
            type.GetField("_maxSize", flags).SetValue(entry, maximum);
            return entry;
        }
    }
}
