// System
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat.Tests
{
    /// <summary> 화면 중심 조준점 수렴, 조준선 충돌 선택, 기본 공격 화살 방향을 검증한다 </summary>
    public sealed class ArrowAimGeometryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private const float POSITION_TOLERANCE      = 0.0001f;
        private const float ANGLE_TOLERANCE_DEGREES = 0.01f;
        private const float MIN_CONVERGE_DEGREES    = 1f;    // 평행 발사와 구분되는 최소 수렴각
        private const float AIM_TARGET_DISTANCE     = 30f;
        private const float MUZZLE_FORWARD_OFFSET   = 4f;
        private const float MUZZLE_SIDE_OFFSET      = 1.2f;
        private const float SCREEN_CENTER           = 0.5f;
        private const float AIM_MAX_DISTANCE        = 30f;
        private const float MUZZLE_PROJECTION       = 3f;

        private static readonly Vector3 SHOULDER_CAMERA_POSITION = new Vector3(1.2f, 2f, -4f);

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(0f, 0f)]
        [TestCase(85f, 25f)]
        [TestCase(-120f, -15f)]
        public void ShoulderCameraAndMuzzleConvergeOnScreenCenter(float yaw, float pitch)
        {
            var host = new GameObject("AimCamera", typeof(Camera));
            try
            {
                Camera camera = host.GetComponent<Camera>();
                camera.transform.SetPositionAndRotation(SHOULDER_CAMERA_POSITION, Quaternion.Euler(pitch, yaw, 0f));
                PrecisionAimRay aim = ArrowAimGeometry.BuildCameraRay(camera);
                Vector3 target = aim.Origin + (aim.Direction * AIM_TARGET_DISTANCE);
                Vector3 muzzle = camera.transform.position + (camera.transform.forward * MUZZLE_FORWARD_OFFSET)
                    - (camera.transform.right * MUZZLE_SIDE_OFFSET);
                Assert.IsTrue(ArrowAimGeometry.TryDirection(muzzle, target, out Vector3 direction));
                Vector3 arrival = muzzle + (direction * Vector3.Distance(muzzle, target));
                Vector3 viewport = camera.WorldToViewportPoint(arrival);
                Assert.That(viewport.x, Is.EqualTo(SCREEN_CENTER).Within(POSITION_TOLERANCE));
                Assert.That(viewport.y, Is.EqualTo(SCREEN_CENTER).Within(POSITION_TOLERANCE));
                Assert.That(Vector3.Distance(arrival, target), Is.LessThan(POSITION_TOLERANCE));
                Assert.That(Vector3.Angle(direction, aim.Direction), Is.GreaterThan(MIN_CONVERGE_DEGREES),
                    "평행 발사가 아니라 조준점으로 수렴해야 한다");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InvalidOrCoincidentAimCannotProduceDirection()
        {
            Assert.IsFalse(ArrowAimGeometry.TryDirection(Vector3.one, Vector3.one, out _));
            Assert.IsFalse(ArrowAimGeometry.TryDirection(Vector3.zero, new Vector3(float.NaN, 0f, 0f), out _));
            Assert.IsFalse(ArrowAimGeometry.TryDirection(Vector3.zero, new Vector3(float.PositiveInfinity, 0f, 0f), out _));
        }

        [Test]
        public void MinimumHitDistanceIsMuzzleProjectionOnCameraRay()
        {
            PrecisionAimRay ray = new PrecisionAimRay { Origin = Vector3.zero, Direction = Vector3.forward };

            Assert.That(ArrowAimGeometry.ResolveMinimumHitDistance(ray, new Vector3(1f, 0.5f, MUZZLE_PROJECTION)),
                Is.EqualTo(MUZZLE_PROJECTION).Within(POSITION_TOLERANCE));
            Assert.AreEqual(0f, ArrowAimGeometry.ResolveMinimumHitDistance(ray, new Vector3(0f, 0f, -2f)),
                "카메라보다 뒤에 있는 발사점은 음수 거리가 아니라 0이다");
        }

        [Test]
        public void HitBeyondMuzzleIsSelectedAsAimPoint()
        {
            float[] hits = { 12f, 8f };

            Assert.IsTrue(ArrowAimGeometry.TrySelectAimDistance(
                hits, hits.Length, MUZZLE_PROJECTION, AIM_MAX_DISTANCE, out float aimDistance));
            Assert.AreEqual(8f, aimDistance);
        }

        [Test]
        public void HitBetweenCameraAndMuzzleIsIgnored()
        {
            // 발사점 투영 거리와 같은 충돌도 발사점 뒤쪽 조준이 되므로 버린다
            float[] hits = { 1.5f, MUZZLE_PROJECTION, 9f };

            Assert.IsTrue(ArrowAimGeometry.TrySelectAimDistance(
                hits, hits.Length, MUZZLE_PROJECTION, AIM_MAX_DISTANCE, out float aimDistance));
            Assert.AreEqual(9f, aimDistance);
        }

        [Test]
        public void NoValidHitFailsAndKeepsMaxDistance()
        {
            float[] hits = { 1f, 2.5f, AIM_MAX_DISTANCE + 1f, float.NaN, 5f };

            // count 밖의 5f는 읽지 않는다
            Assert.IsFalse(ArrowAimGeometry.TrySelectAimDistance(
                hits, 4, MUZZLE_PROJECTION, AIM_MAX_DISTANCE, out float aimDistance));
            Assert.AreEqual(AIM_MAX_DISTANCE, aimDistance);

            Assert.IsFalse(ArrowAimGeometry.TrySelectAimDistance(
                null, 0, MUZZLE_PROJECTION, AIM_MAX_DISTANCE, out float nullDistance));
            Assert.AreEqual(AIM_MAX_DISTANCE, nullDistance);
        }

        [Test]
        public void FallbackDirectionUsesCameraThenAimForward()
        {
            Assert.AreEqual(Vector3.forward,
                ArrowAimGeometry.ResolveFallbackDirection(Vector3.forward * 2f, Vector3.right));
            Assert.AreEqual(Vector3.right,
                ArrowAimGeometry.ResolveFallbackDirection(Vector3.zero, Vector3.right));
        }

        [Test]
        public void BasicArrowSkipsWallBetweenCameraAndMuzzle()
        {
            using (var fixture = new BasicArrowFixture())
            {
                Vector3 cameraOrigin = new Vector3(0.8f, 1.6f, -4f);
                fixture.SetCameraAim(cameraOrigin, Vector3.forward);

                // 카메라 바로 앞 벽은 발사점보다 가깝고, 먼 벽의 앞면이 실제 조준점이다
                GameObject nearWall = CreateWall("NearAimWall", new Vector3(0.8f, 1.6f, -2f));
                GameObject farWall  = CreateWall("FarAimWall", new Vector3(0.8f, 1.6f, 10f));
                Physics.SyncTransforms();

                try
                {
                    fixture.Fire();

                    Assert.AreEqual(1, fixture.Attack.SpawnCount);
                    Vector3 expectedPoint = new Vector3(0.8f, 1.6f, 9.5f);
                    Vector3 expected = (expectedPoint - fixture.Attack.LastSnapshot.Origin).normalized;
                    Assert.That(Vector3.Angle(fixture.Attack.LastSnapshot.Direction, expected),
                        Is.LessThan(ANGLE_TOLERANCE_DEGREES));
                    Assert.Greater(Vector3.Dot(fixture.Attack.LastSnapshot.Direction, Vector3.forward), 0f,
                        "카메라와 발사점 사이 충돌을 조준점으로 쓰면 화살이 뒤로 간다");
                }
                finally
                {
                    Object.DestroyImmediate(nearWall);
                    Object.DestroyImmediate(farWall);
                }
            }
        }

        [Test]
        public void BasicArrowFiresAlongCameraWhenAimPointCannotResolve()
        {
            using (var fixture = new BasicArrowFixture())
            {
                Vector3 cameraDirection = new Vector3(0.2f, 0f, 1f).normalized;
                fixture.SetCameraAim(new Vector3(0.8f, 1.6f, -4f), cameraDirection);
                fixture.SetArrowSpeed(0f);
                LogAssert.Expect(LogType.Warning, new Regex("평행 발사"));

                fixture.Fire();

                Assert.AreEqual(1, fixture.Attack.SpawnCount, "스태미나를 쓴 기본 공격은 화살이 나가야 한다");
                Assert.That(Vector3.Angle(fixture.Attack.LastSnapshot.Direction, cameraDirection),
                    Is.LessThan(ANGLE_TOLERANCE_DEGREES));
            }
        }

        [Test]
        public void BasicArrowWithoutCameraDirectionFiresAlongAimForward()
        {
            using (var fixture = new BasicArrowFixture())
            {
                fixture.SetCameraAim(new Vector3(0.8f, 1.6f, -4f), Vector3.zero);
                LogAssert.Expect(LogType.Warning, new Regex("카메라 조준선이 없어"));

                fixture.Fire();

                Assert.AreEqual(1, fixture.Attack.SpawnCount);
                Assert.That(Vector3.Angle(fixture.Attack.LastSnapshot.Direction, Vector3.forward),
                    Is.LessThan(ANGLE_TOLERANCE_DEGREES));
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject CreateWall(string name, Vector3 position)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.layer = 0;
            wall.transform.position = position;
            return wall;
        }

        private static void Set(object target, string fieldName, object value)
        {
            System.Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, PRIVATE_INSTANCE | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new System.MissingFieldException(target.GetType().FullName, fieldName);
        }

        private sealed class BasicArrowFixture : System.IDisposable
        {
            private const uint  ACTION_ID    = 3u;
            private const float ARROW_SPEED  = 30f;
            private const int   DEFAULT_MASK = 1 << 0;

            private readonly bool _wasServerActive;

            private readonly GameObject     _playerObject;
            private readonly GameObject     _arrowPrefabObject;
            private readonly GameDatabaseSO _database;
            private readonly CombatConfigSO _combat;
            private readonly ArrowConfigSO  _arrowConfig;
            private readonly CameraConfigSO _camera;

            private CombatStatsSnapshot _loadout;

            public CapturingPlayerAttack Attack { get; }

            public BasicArrowFixture()
            {
                _wasServerActive = NetworkServer.active;
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

                _combat      = ScriptableObject.CreateInstance<CombatConfigSO>();
                _arrowConfig = ScriptableObject.CreateInstance<ArrowConfigSO>();
                _camera      = ScriptableObject.CreateInstance<CameraConfigSO>();
                _database    = ScriptableObject.CreateInstance<GameDatabaseSO>();
                Set(_arrowConfig, "_worldMask", (LayerMask)DEFAULT_MASK);
                Set(_database, "_combat", _combat);
                Set(_database, "_arrow", _arrowConfig);
                Set(_database, "_camera", _camera);
                GameDB.Initialize(_database);

                _arrowPrefabObject = new GameObject(
                    "BasicArrowTestArrow", typeof(NetworkIdentity), typeof(ArrowProjectile));

                _playerObject = new GameObject("BasicArrowTestPlayer");
                _playerObject.SetActive(false);
                NetworkIdentity identity = _playerObject.AddComponent<NetworkIdentity>();
                _playerObject.AddComponent<PlayerPresentation>();
                Attack = _playerObject.AddComponent<CapturingPlayerAttack>();
                typeof(PlayerAttack).GetMethod("Awake", PRIVATE_INSTANCE).Invoke(Attack, null);
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", PRIVATE_INSTANCE)
                    .Invoke(identity, null);

                _loadout = new CombatStatsSnapshot
                {
                    Revision                   = 1u,
                    MaxHealth                  = 100f,
                    RangedAttackPower          = 20f,
                    AttackSpeedMultiplier      = 1f,
                    StaminaCostMultiplier      = 1f,
                    CriticalMultiplierMin      = 2f,
                    CriticalMultiplierMax      = 3f,
                    ThirdComboDamageMultiplier = 1f,
                    ArrowSpeed                 = ARROW_SPEED,
                    ArrowHitRadius             = 0.4f,
                    PierceCount                = 1,
                    BurstShotCount             = 1,
                };

                Set(Attack, "_targetMask", (LayerMask)0);
                Set(Attack, "_arrowPrefab", _arrowPrefabObject.GetComponent<ArrowProjectile>());
                Set(Attack, "_committedLoadout", _loadout);
                Set(Attack, "_activePresentationActionId", ACTION_ID);
                Set(Attack, "_requestedAim", Vector2.up);
                Set(Attack, "_hasRequestedAim", true);
            }

            public void SetCameraAim(Vector3 origin, Vector3 direction)
            {
                Set(Attack, "_committedCameraAim", new PrecisionAimRay { Origin = origin, Direction = direction });
            }

            public void SetArrowSpeed(float speed)
            {
                _loadout.ArrowSpeed = speed;
                Set(Attack, "_committedLoadout", _loadout);
            }

            public void Fire()
            {
                typeof(PlayerAttack).GetMethod("FireArrow", PRIVATE_INSTANCE)
                    .Invoke(Attack, new object[] { _combat, 0d });
            }

            public void Dispose()
            {
                Object.DestroyImmediate(_playerObject);
                Object.DestroyImmediate(_arrowPrefabObject);

                GameDB.ResetStatic();
                Object.DestroyImmediate(_database);
                Object.DestroyImmediate(_camera);
                Object.DestroyImmediate(_arrowConfig);
                Object.DestroyImmediate(_combat);

                typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                    .SetValue(null, _wasServerActive);
            }
        }

        private sealed class CapturingPlayerAttack : PlayerAttack
        {
            public int               SpawnCount   { get; private set; }
            public ArrowFireSnapshot LastSnapshot { get; private set; }

            protected override void SpawnAcceptedArrow(in ArrowFireSnapshot snapshot, ArrowConfigSO arrowConfig)
            {
                ++SpawnCount;
                LastSnapshot = snapshot;
            }
        }
    }
}
