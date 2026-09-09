// System
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI.Tests
{
    public sealed class EnemyFieldRulesTests
    {
        private GameObject _self;
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _self = new GameObject("EnemyFieldRulesSelf",
                typeof(NetworkIdentity), typeof(EnemyStatus));
            _target = new GameObject("EnemyFieldRulesTarget");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_self);
            Object.DestroyImmediate(_target);
        }

        [Test]
        public void EnhancementRecalculatesFromBaseAndPreservesHealthRatio()
        {
            bool calculated = EnemyHerdingEnhancementRules.TryCalculate(
                50f, 100f, 100f, 10f, 1.6f, 1.4f, 1.5f,
                out float health, out float maxHealth,
                out float attackPower, out float rewardMultiplier);

            Assert.IsTrue(calculated);
            Assert.AreEqual(160f, maxHealth);
            Assert.AreEqual(80f, health);
            Assert.AreEqual(14f, attackPower);
            Assert.AreEqual(1.5f, rewardMultiplier);

            Assert.IsTrue(EnemyHerdingEnhancementRules.TryCalculate(
                health, maxHealth, 100f, 10f, 1.6f, 1.4f, 1.5f,
                out float repeatedHealth, out float repeatedMax,
                out _, out _));
            Assert.AreEqual(160f, repeatedMax);
            Assert.AreEqual(80f, repeatedHealth);
        }

        [Test]
        public void HerdingRevisionOnlyAcceptsNewerInstruction()
        {
            var brain = new HeokkaebiBrain(
                _self.transform, _self.GetComponent<EnemyStatus>());

            Assert.IsTrue(brain.BeginHerding(Vector3.one, 2u));
            Assert.IsFalse(brain.BeginHerding(Vector3.zero, 2u));
            Assert.IsFalse(brain.BeginHerding(Vector3.zero, 1u));
            brain.CompleteHerding(1u);
            Assert.IsTrue(brain.IsHerding);
            brain.CompleteHerding(2u);
            Assert.IsFalse(brain.IsHerding);
        }

        [Test]
        public void HeokkaebiOldExecutionCannotInterruptNextWindup()
        {
            var brain = new HeokkaebiBrain(
                _self.transform, _self.GetComponent<EnemyStatus>());
            brain.Target = _target.transform;
            brain.Tick(0f);
            brain.Tick(0f);
            uint firstExecution = brain.ActiveAttackExecutionId;
            Assert.AreNotEqual(0u, firstExecution);
            Assert.IsTrue(brain.TryInterruptPrecision(firstExecution));

            brain.Tick(0f);
            brain.Tick(0f);
            brain.Tick(0f);
            uint secondExecution = brain.ActiveAttackExecutionId;
            Assert.AreNotEqual(firstExecution, secondExecution);
            Assert.IsFalse(brain.TryInterruptPrecision(firstExecution));
            Assert.IsTrue(brain.TryInterruptPrecision(secondExecution));
        }

        [Test]
        public void ChanggwiOldExecutionCannotInterruptNextCharge()
        {
            var brain = new ChanggwiBrain(
                _self.transform, _self.GetComponent<EnemyStatus>());
            brain.Target = _target.transform;

            Invoke(brain, "EnterCharge");
            uint firstExecution = brain.ActiveAttackExecutionId;
            Assert.IsTrue(brain.TryInterruptPrecision(firstExecution));

            Invoke(brain, "EnterCharge");
            uint secondExecution = brain.ActiveAttackExecutionId;
            Assert.AreNotEqual(firstExecution, secondExecution);
            Assert.IsFalse(brain.TryInterruptPrecision(firstExecution));
            Assert.IsTrue(brain.TryInterruptPrecision(secondExecution));
        }

        [Test]
        public void SwallowedPlayerIsUnavailableToOrdinaryEnemyTargeting()
        {
            SwallowTargetStub stub = _target.AddComponent<SwallowTargetStub>();
            stub.IsSwallowedValue = true;
            Assert.IsTrue(EnemyTargetRules.IsUnavailable(stub));

            stub.IsSwallowedValue = false;
            Assert.IsFalse(EnemyTargetRules.IsUnavailable(stub));

            stub.IsDeadValue = true;
            Assert.IsTrue(EnemyTargetRules.IsUnavailable(stub));
        }

        [Test]
        public void SpawnGraceAndTemporaryCapUseGameplayRules()
        {
            double deadline = SpawnHerdingRules.GetFirstSpawnGameplayTime(
                RunPhase.Shrink1, 100d, 60f);
            Assert.AreEqual(160d, deadline);
            Assert.AreEqual(deadline, SpawnHerdingRules.GetFirstSpawnGameplayTime(
                RunPhase.Shrink1, 100d, 60f));
            Assert.AreEqual(100d, SpawnHerdingRules.GetFirstSpawnGameplayTime(
                RunPhase.Farming, 100d, 60f));

            Assert.AreEqual(14, SpawnHerdingRules.GetDesiredConcurrent(
                14, 16, 1.5f, 0));
            Assert.AreEqual(24, SpawnHerdingRules.GetDesiredConcurrent(
                14, 16, 1.5f, 1));
        }

        [UnityTest]
        public IEnumerator PauseStopsActualAgentWithoutClearingPathAndResumeRestoresMovement()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("NavMeshAgent는 EditMode에서 runtime NavMesh simulation에 가입하지 않는다; PlayMode/M2에서 실행한다");
            }

            NavMeshDataInstance navMesh = AddFlatNavMesh();
            try
            {
                Assert.IsTrue(NavMesh.SamplePosition(Vector3.zero,
                    out NavMeshHit start, 2f, NavMesh.AllAreas));
                _self.transform.position = start.position;
                NavMeshAgent agent = _self.AddComponent<NavMeshAgent>();
                EnemyController controller = _self.AddComponent<EnemyController>();
                Set(controller, "_agent", agent);
                agent.enabled = false;
                agent.enabled = true;
                yield return null;
                Assert.IsTrue(agent.isOnNavMesh);

                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(start.position,
                    Vector3.forward * 4f, NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
                Assert.IsTrue(agent.SetPath(path));
                agent.isStopped = false;
                Vector3 destination = agent.destination;

                float movementDeadline = Time.realtimeSinceStartup + 2f;
                while ((agent.velocity.sqrMagnitude <= 0.0001f) &&
                       (Time.realtimeSinceStartup < movementDeadline))
                {
                    yield return null;
                }

                Assert.Greater(agent.velocity.sqrMagnitude, 0.0001f,
                    "pause 회귀는 실제 이동 중인 agent에서 검증해야 한다");

                Invoke(controller, "ApplyRunPause");

                Assert.IsTrue(agent.isStopped);
                yield return null;
                Assert.AreEqual(Vector3.zero, agent.velocity,
                    "pause 다음 simulation frame에는 NavMeshAgent 관성 속도가 없어야 한다");
                Assert.IsTrue(agent.hasPath, "정지하면서 기존 NavMesh 경로를 지우면 안 된다");
                Assert.AreEqual(destination, agent.destination);

                Invoke(controller, "RestoreAfterRunPause");

                Assert.IsFalse(agent.isStopped);
                Assert.IsTrue(agent.hasPath);
                Assert.AreEqual(destination, agent.destination);
            }
            finally
            {
                navMesh.Remove();
            }
        }

        [Test]
        public void RangedAttackDoesNotFireAtSwallowedTarget()
        {
            bool wasServerActive = NetworkServer.active;
            try
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, true);
                SwallowTargetStub target = _target.AddComponent<SwallowTargetStub>();
                target.IsSwallowedValue = true;

                EnemyController controller = _self.AddComponent<EnemyController>();
                Set(controller, "_brain", new AekgwiBrain(
                    _self.transform, _self.GetComponent<EnemyStatus>()));

                Invoke(controller, "ExecuteAttack", _target.transform);
                Assert.AreEqual(0, Object.FindObjectsByType<CurseProjectile>(
                    FindObjectsInactive.Exclude).Length);
            }
            finally
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        [Test]
        public void CurseProjectilePassesSwallowedTargetWithoutDamageOrConsumption()
        {
            bool wasServerActive = NetworkServer.active;
            GameObject projectileObject = null;
            try
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, true);

                int playerLayer = LayerMask.NameToLayer(Constants.Layer.PLAYER);
                Assert.GreaterOrEqual(playerLayer, 0);
                _target.layer = playerLayer;
                _target.transform.position = Vector3.right;
                _target.AddComponent<SphereCollider>().radius = 0.25f;
                SwallowTargetStub target = _target.AddComponent<SwallowTargetStub>();
                target.IsSwallowedValue = true;

                projectileObject = new GameObject("EnemyFieldRulesProjectile",
                    typeof(NetworkIdentity), typeof(SphereCollider), typeof(CurseProjectile));
                projectileObject.GetComponent<SphereCollider>().radius = 0.1f;
                CurseProjectile projectile = projectileObject.GetComponent<CurseProjectile>();
                Invoke(projectile, "Awake");
                projectile.Initialize(Vector3.right, 10f, 5f, 1u, 10f);
                Physics.SyncTransforms();

                bool consumed = (bool) Invoke(projectile, "TryApplyHit", Vector3.zero, 2f);

                Assert.IsFalse(consumed);
                Assert.AreEqual(0, target.DamageCount);
                Assert.IsNotNull(projectileObject);
            }
            finally
            {
                if (projectileObject != null)
                {
                    Object.DestroyImmediate(projectileObject);
                }

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, wasServerActive);
            }
        }

        private static NavMeshDataInstance AddFlatNavMesh()
        {
            Assert.Greater(NavMesh.GetSettingsCount(), 0);
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(20f, 0.2f, 20f),
                    transform = Matrix4x4.TRS(new Vector3(0f, -0.1f, 0f),
                        Quaternion.identity, Vector3.one),
                    area = 0,
                },
            };

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(30f, 4f, 30f)),
                Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(data);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            Assert.IsTrue(instance.valid);
            return instance;
        }

        private static object Invoke(object target, string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName);
            return method.Invoke(target, arguments);
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private sealed class SwallowTargetStub : MonoBehaviour, IBossSwallowTarget
        {
            public bool IsDeadValue;
            public bool IsSwallowedValue;
            public int DamageCount;

            public bool IsDead => IsDeadValue;
            public ParticipantId Participant => new ParticipantId(1UL);
            public bool IsSwallowed => IsSwallowedValue;
            public uint SwallowGeneration => 1u;

            public void TakeDamage(in DamageInfo damage)
            {
                ++DamageCount;
            }

            public BossSwallowDamageResult ServerApplyBossDamage(in DamageInfo damage,
                in BossSwallowDamageContext context)
            {
                return default;
            }

            public bool ServerTryRescueSwallowed(ParticipantId rescuer,
                uint expectedGeneration, float restoreHealthRatio)
            {
                return false;
            }

            public bool ServerExpireSwallowed(double gameplayTime)
            {
                return false;
            }

            public bool ServerReleaseSwallowed(float restoreHealthRatio)
            {
                return false;
            }
        }
    }
}
