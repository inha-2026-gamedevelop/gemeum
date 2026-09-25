// Unity
using UnityEngine;

// Mirror
using Mirror;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI.Tests
{
    /// <summary>
    /// BossTargetingSystem은 기존 RefreshTarget/ServerCollectAliveTargets의 로직을 그대로
    /// 옮긴 것이라, 여기서는 "동작이 하나도 바뀌지 않았다"만 증명한다. 특히
    /// ResolvePrimaryTarget과 CollectAliveTargets가 서로 다른 필터링을 한다는 사실
    /// (NetworkIdentity 정규화·중복 제거 유무)을 통일하지 않고 그대로 유지했음을
    /// DivergenceTest에서 직접 증명한다
    /// </summary>
    public sealed class BossTargetingSystemTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PLAYER_LAYER_NAME = Constants.Layer.PLAYER;

        private GameObject          _boss;
        private BossTargetingSystem _targeting;
        private int                 _playerLayerMask;

        /****************************************
        *              Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _boss            = new GameObject("BossTargetingTest");
            _playerLayerMask = LayerMask.GetMask(PLAYER_LAYER_NAME);
            _targeting       = new BossTargetingSystem(_boss.transform, _playerLayerMask);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_boss);
        }

        /****************************************
        *        ResolvePrimaryTarget
        ****************************************/

        [Test]
        public void ResolvePrimaryTarget_SelectsNearestCandidate()
        {
            GameObject near = CreatePlayerCollider("Near", new Vector3(0f, 0f, 5f));
            GameObject far  = CreatePlayerCollider("Far", new Vector3(0f, 0f, 10f));

            _targeting.ResolvePrimaryTarget(out Transform target, out IDamageable damageable);

            Assert.AreEqual(near.transform, target);

            Object.DestroyImmediate(near);
            Object.DestroyImmediate(far);
        }

        [Test]
        public void ResolvePrimaryTarget_ExcludesDeadTarget()
        {
            GameObject dead  = CreatePlayerCollider("Dead", new Vector3(0f, 0f, 5f));
            dead.AddComponent<DamageTargetStub>().Dead = true;
            GameObject alive = CreatePlayerCollider("Alive", new Vector3(0f, 0f, 10f));
            alive.AddComponent<DamageTargetStub>().Dead = false;

            _targeting.ResolvePrimaryTarget(out Transform target, out IDamageable damageable);

            Assert.AreEqual(alive.transform, target);

            Object.DestroyImmediate(dead);
            Object.DestroyImmediate(alive);
        }

        [Test]
        public void ResolvePrimaryTarget_ExcludesSwallowedTarget()
        {
            GameObject swallowed = CreatePlayerCollider("Swallowed", new Vector3(0f, 0f, 5f));
            swallowed.AddComponent<SwallowTargetStub>().Swallowed = true;
            GameObject alive = CreatePlayerCollider("Alive", new Vector3(0f, 0f, 10f));
            alive.AddComponent<DamageTargetStub>().Dead = false;

            _targeting.ResolvePrimaryTarget(out Transform target, out IDamageable damageable);

            Assert.AreEqual(alive.transform, target);

            Object.DestroyImmediate(swallowed);
            Object.DestroyImmediate(alive);
        }

        // 기존 RefreshTarget의 quirk 보존 확인: IDamageable이 없는 Player 레이어 콜라이더는
        // 걸러지지 않고 후보로 남는다 (Dev_AI TestPlayer가 타겟이 될 수 있는 이유)
        [Test]
        public void ResolvePrimaryTarget_DoesNotExcludeColliderWithoutIDamageable()
        {
            GameObject noDamageable = CreatePlayerCollider("NoDamageable", new Vector3(0f, 0f, 5f));
            GameObject alive        = CreatePlayerCollider("Alive", new Vector3(0f, 0f, 10f));
            alive.AddComponent<DamageTargetStub>().Dead = false;

            _targeting.ResolvePrimaryTarget(out Transform target, out IDamageable damageable);

            Assert.AreEqual(noDamageable.transform, target);
            Assert.IsNull(damageable);

            Object.DestroyImmediate(noDamageable);
            Object.DestroyImmediate(alive);
        }

        /****************************************
        *        CollectAliveTargets
        ****************************************/

        [Test]
        public void CollectAliveTargets_ExcludesDeadTarget()
        {
            GameObject dead  = CreatePlayerCollider("Dead", new Vector3(0f, 0f, 5f));
            dead.AddComponent<DamageTargetStub>().Dead = true;
            GameObject alive = CreatePlayerCollider("Alive", new Vector3(0f, 0f, 10f));
            alive.AddComponent<DamageTargetStub>().Dead = false;

            Transform[] destination = new Transform[4];
            int count = _targeting.CollectAliveTargets(destination);

            Assert.AreEqual(1, count);
            Assert.AreEqual(alive.transform, destination[0]);

            Object.DestroyImmediate(dead);
            Object.DestroyImmediate(alive);
        }

        [Test]
        public void CollectAliveTargets_ExcludesSwallowedTarget()
        {
            GameObject swallowed = CreatePlayerCollider("Swallowed", new Vector3(0f, 0f, 5f));
            swallowed.AddComponent<SwallowTargetStub>().Swallowed = true;
            GameObject alive = CreatePlayerCollider("Alive", new Vector3(0f, 0f, 10f));
            alive.AddComponent<DamageTargetStub>().Dead = false;

            Transform[] destination = new Transform[4];
            int count = _targeting.CollectAliveTargets(destination);

            Assert.AreEqual(1, count);
            Assert.AreEqual(alive.transform, destination[0]);

            Object.DestroyImmediate(swallowed);
            Object.DestroyImmediate(alive);
        }

        [Test]
        public void CollectAliveTargets_NormalizesToNetworkIdentityParent()
        {
            GameObject identityRoot = new GameObject("Player", typeof(NetworkIdentity));
            GameObject collider = CreatePlayerCollider("PlayerCollider", Vector3.zero);
            collider.transform.SetParent(identityRoot.transform);

            Transform[] destination = new Transform[4];
            int count = _targeting.CollectAliveTargets(destination);

            Assert.AreEqual(1, count);
            Assert.AreEqual(identityRoot.transform, destination[0]);

            Object.DestroyImmediate(identityRoot);
        }

        [Test]
        public void CollectAliveTargets_DedupsMultipleCollidersOnSameIdentity()
        {
            GameObject identityRoot = new GameObject("Player", typeof(NetworkIdentity));
            GameObject colliderA = CreatePlayerCollider("ColliderA", Vector3.zero);
            colliderA.transform.SetParent(identityRoot.transform);
            GameObject colliderB = CreatePlayerCollider("ColliderB", new Vector3(0.1f, 0f, 0f));
            colliderB.transform.SetParent(identityRoot.transform);

            Transform[] destination = new Transform[4];
            int count = _targeting.CollectAliveTargets(destination);

            Assert.AreEqual(1, count);
            Assert.AreEqual(identityRoot.transform, destination[0]);

            Object.DestroyImmediate(identityRoot);
        }

        [Test]
        public void CollectAliveTargets_RespectsDestinationCapacity()
        {
            GameObject a = CreatePlayerCollider("A", new Vector3(0f, 0f, 1f));
            GameObject b = CreatePlayerCollider("B", new Vector3(0f, 0f, 2f));
            GameObject c = CreatePlayerCollider("C", new Vector3(0f, 0f, 3f));

            Transform[] destination = new Transform[2];
            int count = _targeting.CollectAliveTargets(destination);

            Assert.AreEqual(2, count);

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(c);
        }

        /****************************************
        *   ResolvePrimaryTarget vs CollectAliveTargets 차이 보존
        ****************************************/

        // 한 NetworkIdentity 아래 여러 콜라이더가 있을 때: CollectAliveTargets는 identity의
        // Transform으로 정규화해 1개로 합치지만, ResolvePrimaryTarget은 정규화를 하지 않고
        // 콜라이더 자신의 Transform을 그대로 돌려준다 - 두 알고리즘이 통일되지 않았다는 증거
        [Test]
        public void ResolvePrimaryTargetAndCollectAliveTargets_DivergeOnNetworkIdentityNormalization()
        {
            GameObject identityRoot = new GameObject("Player", typeof(NetworkIdentity));
            identityRoot.transform.position = new Vector3(0f, 0f, 5f);
            GameObject collider = CreatePlayerCollider("PlayerCollider", new Vector3(0f, 0f, 5f));
            collider.transform.SetParent(identityRoot.transform);

            _targeting.ResolvePrimaryTarget(out Transform primary, out IDamageable damageable);
            Assert.AreEqual(collider.transform, primary,
                "ResolvePrimaryTarget은 NetworkIdentity로 정규화하지 않고 콜라이더 자신의 Transform을 돌려줘야 한다");
            Assert.AreNotEqual(identityRoot.transform, primary);

            Transform[] destination = new Transform[4];
            int count = _targeting.CollectAliveTargets(destination);
            Assert.AreEqual(1, count);
            Assert.AreEqual(identityRoot.transform, destination[0],
                "CollectAliveTargets는 NetworkIdentity의 Transform으로 정규화해야 한다");

            Object.DestroyImmediate(identityRoot);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject CreatePlayerCollider(string name, Vector3 position)
        {
            GameObject go = new GameObject(name, typeof(SphereCollider));
            go.layer             = LayerMask.NameToLayer(PLAYER_LAYER_NAME);
            go.transform.position = position;
            go.GetComponent<SphereCollider>().isTrigger = true;
            return go;
        }

        private sealed class DamageTargetStub : MonoBehaviour, IDamageable
        {
            public bool Dead { get; set; }
            public bool IsDead => Dead;

            public void TakeDamage(in DamageInfo damage)
            {
            }
        }

        private sealed class SwallowTargetStub : MonoBehaviour, IBossSwallowTarget
        {
            public bool Dead      { get; set; }
            public bool Swallowed { get; set; }

            public bool IsDead                  => Dead;
            public ParticipantId Participant    => default;
            public bool IsSwallowed             => Swallowed;
            public uint SwallowGeneration        => 0;

            public void TakeDamage(in DamageInfo damage)
            {
            }

            public BossSwallowDamageResult ServerApplyBossDamage(
                in DamageInfo damage, in BossSwallowDamageContext context)
            {
                return default;
            }

            public bool ServerTryRescueSwallowed(
                ParticipantId rescuer, uint expectedGeneration, float restoreHealthRatio)
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
