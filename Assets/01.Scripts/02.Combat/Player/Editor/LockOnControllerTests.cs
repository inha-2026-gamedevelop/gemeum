// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat.Tests
{
    /// <summary> 같은 NetworkIdentity 안의 고정 부위 사이 락온 전환을 검증한다 </summary>
    public sealed class LockOnControllerTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private const uint BOSS_NET_ID = 71u;

        private GameDatabaseSO _database;
        private CombatConfigSO _combat;
        private GameObject _cameraObject;
        private GameObject _playerObject;
        private GameObject _bossObject;
        private LockOnController _controller;
        private readonly List<Camera> _disabledMainCameras = new List<Camera>();

        [SetUp]
        public void SetUp()
        {
            _combat = ScriptableObject.CreateInstance<CombatConfigSO>();
            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_combat", _combat);
            GameDB.Initialize(_database);

            Camera[] existingCameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include);
            for (int i = 0; i < existingCameras.Length; ++i)
            {
                Camera existing = existingCameras[i];
                if ((existing != null) && existing.enabled &&
                    existing.CompareTag("MainCamera"))
                {
                    existing.enabled = false;
                    _disabledMainCameras.Add(existing);
                }
            }

            _cameraObject = new GameObject("LockOnTestCamera", typeof(Camera));
            _cameraObject.tag = "MainCamera";
            _cameraObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Mirror requires NetworkIdentity to exist before a NetworkBehaviour is added.
            _playerObject = new GameObject("LockOnTestPlayer", typeof(NetworkIdentity));
            _controller = _playerObject.AddComponent<LockOnController>();
            NetworkIdentity playerIdentity = _playerObject.GetComponent<NetworkIdentity>();
            Invoke(playerIdentity, "InitializeNetworkBehaviours");
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(playerIdentity, true);

            _bossObject = new GameObject("LockOnTestBoss", typeof(NetworkIdentity));
            _bossObject.transform.position = new Vector3(0f, 0f, 10f);
            NetworkIdentity identity = _bossObject.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId))
                .SetValue(identity, BOSS_NET_ID);

            CreatePart("Tail1", BossTargetPartIds.Tail1, new Vector3(-0.25f, 0f, 0f));
            CreatePart("Tail2", BossTargetPartIds.Tail2, new Vector3(0.75f, 0f, 0f));
            int bossLayer = LayerMask.NameToLayer(Constants.Layer.BOSS);
            Set(_controller, "_targetLayerMask", 1 << bossLayer);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            GameDB.ResetStatic();
            Object.DestroyImmediate(_bossObject);
            Object.DestroyImmediate(_playerObject);
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_combat);

            for (int i = 0; i < _disabledMainCameras.Count; ++i)
            {
                if (_disabledMainCameras[i] != null)
                {
                    _disabledMainCameras[i].enabled = true;
                }
            }

            _disabledMainCameras.Clear();
        }

        [Test]
        public void CyclingFromBossBodyKeepsPartsOnSameIdentityEligible()
        {
            Set(_controller, "_serverTargetNetId", BOSS_NET_ID);
            Set(_controller, "_serverTargetPart", TargetRef.BODY_PART_ID);

            object candidate = FindBestCandidateExcludingCurrent();

            Assert.IsTrue(GetCandidate<bool>(candidate, "IsValid"));
            Assert.AreEqual(BOSS_NET_ID, GetCandidate<uint>(candidate, "NetId"));
            Assert.AreEqual(BossTargetPartIds.Tail1,
                GetCandidate<byte>(candidate, "PartIndex"));
        }

        [Test]
        public void CyclingFromPartExcludesOnlyExactNetIdAndPartId()
        {
            Set(_controller, "_serverTargetNetId", BOSS_NET_ID);
            Set(_controller, "_serverTargetPart", BossTargetPartIds.Tail1);

            object candidate = FindBestCandidateExcludingCurrent();

            Assert.IsTrue(GetCandidate<bool>(candidate, "IsValid"));
            Assert.AreEqual(BOSS_NET_ID, GetCandidate<uint>(candidate, "NetId"));
            Assert.AreEqual(BossTargetPartIds.Tail2,
                GetCandidate<byte>(candidate, "PartIndex"));
        }

        private object FindBestCandidateExcludingCurrent()
        {
            MethodInfo method = typeof(LockOnController).GetMethod(
                "FindBestCandidate", PRIVATE_INSTANCE);
            Assert.IsNotNull(method);
            return method.Invoke(_controller, new object[] { true });
        }

        private void CreatePart(string name, byte partId, Vector3 localPosition)
        {
            GameObject part = new GameObject(name, typeof(SphereCollider),
                typeof(LockOnTargetPartStub));
            part.layer = LayerMask.NameToLayer(Constants.Layer.BOSS);
            part.transform.SetParent(_bossObject.transform, false);
            part.transform.localPosition = localPosition;
            part.GetComponent<LockOnTargetPartStub>().PartId = partId;
        }

        private static T GetCandidate<T>(object candidate, string fieldName)
        {
            FieldInfo field = candidate.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(field);
            return (T) field.GetValue(candidate);
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PRIVATE_INSTANCE);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PRIVATE_INSTANCE);
            Assert.IsNotNull(method);
            method.Invoke(target, null);
        }
    }

    public sealed class LockOnTargetPartStub : MonoBehaviour, IAimAssistTarget,
        ITargetPartIdSource
    {
        public byte PartId;

        public Vector3 AimPoint => transform.position;
        public byte TargetPartId => PartId;
        public bool IsTargetable => true;
    }
}
