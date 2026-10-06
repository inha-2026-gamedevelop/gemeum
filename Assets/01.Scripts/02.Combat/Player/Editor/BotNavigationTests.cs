// System
using System.Collections.Generic;

// Third party
using NUnit.Framework;

// Unity
using UnityEngine;
using UnityEngine.AI;

// Project
using Geumeum.AI;
using Geumeum.Data;

namespace Geumeum.Combat.Tests
{
    /// <summary>
    /// 봇 경로 방향의 회귀. 판이 끊기거나 봇이 판 밖으로 밀려도 제자리에 서지 않고,
    /// 판은 지나가지만 몸은 못 넘는 낮은 바위를 돌아가며, 막히면 같은 곳을 계속 밀지 않고 비켜 서는지 본다.
    /// 판 밖으로 곧장 가거나 비켜 설 때는 발밑 지면과 앞을 판단마다 다시 보고, 안전한 곳이 없으면 서 있는지도 본다
    /// </summary>
    public sealed class BotNavigationTests
    {
        // 열려 있는 에디터 씬의 NavMesh, 충돌체와 섞이지 않게 멀리 떨어진 곳에서 굽는다
        private static readonly Vector3 G_Origin = new Vector3(4000f, 300f, 4000f);

        private const float  DECISION       = 0.1f;   // 봇 판단 간격(초)
        private const float  STEP           = 0.25f;  // 판단 한 번에 걷는 거리(m), 걷기 2.5m/s
        private const float  RUN_STEP       = 0.55f;  // 달리기 5.5m/s로 판단 한 번에 가는 거리(m)
        private const float  FLOOR_SIZE     = 30f;
        private const float  FLOOR_HALF     = FLOOR_SIZE * 0.5f;
        private const float  FLOOR_THICK    = 0.2f;
        private const float  WALL_HEIGHT    = 3f;     // 판의 오르기 한계 0.75m보다 높아 판을 끊는다
        private const float  WALL_THICKNESS = 0.5f;
        private const float  CLEARANCE      = 0.2f;   // 몸 중심이 장애물 면에 이보다 가까우면 뚫고 지나간 것으로 본다
        private const double START          = 10d;
        private const int    MAX_DECISIONS  = 200;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private NavMeshDataInstance _navMesh;
        private BotConfigSO         _config;

        [SetUp]
        public void SetUp()
        {
            // 운영 BotConfig.asset과 같은 값으로 고정한다. 에셋 값을 바꿔도 이 회귀의 의미는 바뀌지 않는다
            _config = ScriptableObject.CreateInstance<BotConfigSO>();
            _config.DecisionInterval       = DECISION;
            _config.PathInterval           = 0.25f;
            _config.NavSampleRadius        = 2f;
            _config.NavRecoverRadius       = 6f;
            _config.CornerTolerance        = 0.4f;
            _config.StuckSeconds           = 1f;
            _config.StuckDistance          = 0.15f;
            _config.StepClearance          = 0.2f;
            _config.AvoidanceProbeDistance = 1f;
            _config.AvoidanceAngleStep     = 30f;
            _config.AvoidanceMaxAngle      = 120f;
            _config.UnstickSeconds         = 0.6f;
            _config.DirectApproachDistance = 4f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_navMesh.valid)
            {
                _navMesh.Remove();
            }

            for (int i = 0; i < _objects.Count; ++i)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void UnreachableGoalStillMovesTowardClosestReachablePoint()
        {
            // 목표를 사방 벽으로 가둬 판을 끊는다. 예전에는 부분 경로를 실패로 보고 제자리에 섰다
            Vector3 goal = new Vector3(8f, 0f, 0f);
            BakeNavMesh(
                Wall(goal + new Vector3(-2f, 0f, 0f), new Vector3(WALL_THICKNESS, WALL_HEIGHT, 4.5f)),
                Wall(goal + new Vector3(2f, 0f, 0f), new Vector3(WALL_THICKNESS, WALL_HEIGHT, 4.5f)),
                Wall(goal + new Vector3(0f, 0f, -2f), new Vector3(4.5f, WALL_HEIGHT, WALL_THICKNESS)),
                Wall(goal + new Vector3(0f, 0f, 2f), new Vector3(4.5f, WALL_HEIGHT, WALL_THICKNESS)));
            var navigation = new BotNavigation(_config);

            Vector2 direction = navigation.Direction(At(-8f, 0f), G_Origin + goal, START);

            Assert.IsTrue(navigation.IsPartialPath, "경로가 부분 경로로 잡혀야 이 회귀를 재현한다");
            Assert.Greater(Vector2.Dot(direction, Vector2.right), 0.7f, "가까운 곳까지라도 목표 쪽으로 가야 한다");
            Assert.AreEqual(1, navigation.PartialPaths);
            Assert.AreEqual(0, navigation.Failures);
        }

        [Test]
        public void FormationPointBehindWallIsPulledToLeaderSide()
        {
            // 벽 양 끝으로 돌아가면 닿기는 한다. 그대로 두면 봇이 리더 바로 뒤를 두고 20m를 돈다
            BakeNavMesh(Wall(new Vector3(2f, 0f, 0f), new Vector3(WALL_THICKNESS, WALL_HEIGHT, 20f)));
            var     navigation = new BotNavigation(_config);
            Vector3 leader     = At(0.5f, 0f);

            Vector3 behindWall = navigation.ClampToReachable(leader, At(4f, 0f));
            Vector3 sameSide   = navigation.ClampToReachable(leader, At(-2f, 0f));

            Assert.Less(behindWall.x - G_Origin.x, 2f - (WALL_THICKNESS * 0.5f), "벽 너머 자리를 그대로 썼다");
            Assert.Less(Planar(behindWall - leader).magnitude, 1.5f);
            Assert.AreEqual(-2f, sameSide.x - G_Origin.x, 0.01f, "막히지 않은 대형 자리는 그대로 둔다");
        }

        [Test]
        public void BotPushedOffNavMeshHeadsBackOntoIt()
        {
            BakeNavMesh();
            var navigation = new BotNavigation(_config);

            // 판 가장자리(약 14.5m)에서 3.5m 벗어났다. 예전에는 2m 안에서만 판을 찾아 실패로 서 있었다
            Vector2 direction = navigation.Direction(At(18f, 0f), G_Origin, START);

            Assert.Greater(Vector2.Dot(direction, Vector2.left), 0.9f);
            Assert.AreEqual(0, navigation.Failures);
        }

        [Test]
        public void StuckBotSidestepsThenTriesOtherSideThenBacksOff()
        {
            BakeNavMesh();
            var     navigation = new BotNavigation(_config);
            Vector3 goal       = At(10f, 0f);
            var     escapes    = new List<Vector2>();

            // 몸이 무언가에 눌려 전혀 움직이지 못한다. 예전에는 같은 경로를 다시 받아 같은 쪽만 계속 밀었다
            for (int i = 0; (i < MAX_DECISIONS) && (escapes.Count < 3); ++i)
            {
                int     before    = navigation.StuckRecoveries;
                Vector2 direction = navigation.Direction(G_Origin, goal, DecisionTime(i));
                if (navigation.StuckRecoveries != before)
                {
                    escapes.Add(direction);
                }
            }

            Assert.AreEqual(3, escapes.Count, "제자리면 막힘으로 보고 비켜 서야 한다");
            Assert.Less(Mathf.Abs(Vector2.Dot(escapes[0], Vector2.right)), 0.1f, "처음에는 옆으로 비킨다");
            Assert.Less(Vector2.Dot(escapes[1], escapes[0]), -0.9f, "같은 자리에서 또 막히면 반대쪽으로 비킨다");
            Assert.Less(Vector2.Dot(escapes[2], Vector2.right), -0.9f, "양옆으로도 안 되면 뒤로 물러선다");
        }

        [Test]
        public void TargetSwitchesDoNotHideStuck()
        {
            BakeNavMesh();
            var  navigation = new BotNavigation(_config);
            bool recovered  = false;

            // 시야 끝에서 잡귀가 들락거리면 판단이 경로를 자주 버린다(Clear).
            // 예전에는 이때 막힘 시계도 함께 지워져 같은 바위를 밀어도 영영 감지하지 못했다
            for (int i = 0; (i < 30) && (!recovered); ++i)
            {
                if ((i % 3) == 2)
                {
                    navigation.Clear();
                }

                navigation.Direction(G_Origin, At(10f, 0f), DecisionTime(i));
                recovered = (navigation.StuckRecoveries > 0);
            }

            Assert.IsTrue(recovered);
        }

        [Test]
        public void StandingStillWhileNotAskedIsNotStuck()
        {
            BakeNavMesh();
            var     navigation = new BotNavigation(_config);
            Vector3 goal       = At(10f, 0f);

            // 공격이나 관찰로 3초 동안 이동을 묻지 않다가 다시 따라간다. 그 사이 서 있던 시간은 막힘이 아니다
            navigation.Direction(G_Origin, goal, START);
            Vector2 resumed = navigation.Direction(G_Origin, goal, START + 3d);

            Assert.AreEqual(0, navigation.StuckRecoveries);
            Assert.Greater(Vector2.Dot(resumed, Vector2.right), 0.99f);
        }

        [Test]
        public void LowRockThatNavMeshClimbsIsWalkedAround()
        {
            // 판은 0.75m 턱까지 오른다고 보고 이 바위 위로 곧게 지나간다(판에 바위를 넣지 않았다).
            // 플레이어 캡슐은 계단 보정이 없어 0.5m 바위에 걸린다
            BakeNavMesh();
            AddBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE));
            BoxCollider     rock = AddBox("LowRock", new Vector3(1.5f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 1.2f));
            CapsuleCollider body = AddBody();
            Physics.SyncTransforms();

            var        navigation = new BotNavigation(_config, body, ~0);
            WalkResult result     = Walk(navigation, G_Origin, At(6f, 0f), rock.bounds);

            Assert.Less(Vector2.Dot(result.First, Vector2.right), 0.99f, "바위를 정면으로 밀면 안 된다");
            Assert.IsNull(result.Penetration, result.Penetration);
            Assert.IsTrue(result.Arrived, "바위를 돌아 목표에 닿아야 한다");
            Assert.AreEqual(0, navigation.StuckRecoveries, "미리 돌아가면 막힘 복구까지 갈 일이 없다");
        }

        [Test]
        public void NarrowGapTheNavMeshCannotPassIsWalkedThroughWhenBodyFits()
        {
            // 0.8m 틈은 판(에이전트 반경 0.5m)에서는 끊기지만 반경 0.25m 캡슐은 지나간다.
            // 벽이 바닥 끝까지 이어져 판으로는 돌아갈 길도 없다
            const float GAP     = 0.8f;
            float       segment = (FLOOR_SIZE * 0.5f) - (GAP * 0.5f);
            float       offset  = (GAP * 0.5f) + (segment * 0.5f);
            Vector3     size    = new Vector3(WALL_THICKNESS, WALL_HEIGHT, segment);
            Vector3     north   = new Vector3(2f, 0f, offset);
            Vector3     south   = new Vector3(2f, 0f, -offset);
            BakeNavMesh(Wall(north, size), Wall(south, size));
            AddBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE));
            BoxCollider     northWall = AddBox("NorthWall", north + (Vector3.up * (WALL_HEIGHT * 0.5f)), size);
            BoxCollider     southWall = AddBox("SouthWall", south + (Vector3.up * (WALL_HEIGHT * 0.5f)), size);
            CapsuleCollider body      = AddBody();
            Physics.SyncTransforms();

            var        navigation = new BotNavigation(_config, body, ~0);
            WalkResult result     = Walk(navigation, G_Origin, At(4f, 0f), northWall.bounds, southWall.bounds);

            Assert.Greater(navigation.PartialPaths, 0, "판이 틈에서 끊겨야 이 회귀를 재현한다");
            Assert.IsNull(result.Penetration, result.Penetration);
            Assert.IsTrue(result.Arrived, "몸이 지나가는 틈이면 부분 경로 끝에 서 있지 말고 지나가야 한다");
        }

        [Test]
        public void WallBetweenBotAndGoalIsDetouredAlongNavMesh()
        {
            Vector3 center = new Vector3(2f, 0f, 0f);
            Vector3 size   = new Vector3(WALL_THICKNESS, WALL_HEIGHT, 10f);
            BakeNavMesh(Wall(center, size));
            AddBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE));
            BoxCollider     wall = AddBox("Wall", center + (Vector3.up * (WALL_HEIGHT * 0.5f)), size);
            CapsuleCollider body = AddBody();
            Physics.SyncTransforms();

            var        navigation = new BotNavigation(_config, body, ~0);
            WalkResult result     = Walk(navigation, G_Origin, At(5f, 0f), wall.bounds);

            Assert.IsNull(result.Penetration, result.Penetration);
            Assert.IsTrue(result.Arrived, "판을 따라 벽 끝으로 돌아 목표에 닿아야 한다");
            Assert.AreEqual(0, navigation.PartialPaths);
            Assert.AreEqual(0, navigation.StuckRecoveries);
        }

        // 판이 끊긴 까닭이 틈이나 낭떠러지면 앞이 트여 보여도 목표로 곧장 들어가지 않는다.
        // 캡슐 전방 훑기는 앞을 막는 것만 찾으므로 발밑이 비어도 트였다고 본다
        [TestCase(1.5f, 0f,  TestName = "DirectApproachStopsAtChasmEdge")]
        [TestCase(0f,   -2f, TestName = "DirectApproachStopsAtCliffEdge")]
        public void DirectApproachNeverLeavesSupportedGround(float gap, float farTop)
        {
            const float EDGE = 1f;
            BakeSources(
                Platform("Near", -FLOOR_HALF, EDGE, -5f, 5f, 0f),
                Platform("Far", EDGE + gap, FLOOR_HALF, -5f, 5f, farTop));
            CapsuleCollider body = AddBody();
            Physics.SyncTransforms();

            var        navigation = new BotNavigation(_config, body, ~0);
            Vector3    goal       = G_Origin + new Vector3(3f, farTop, 0f);
            WalkResult result     = Walk(navigation, At(-0.5f, 0f), goal);

            Assert.Greater(navigation.PartialPaths, 0, "판이 끊겨야 이 회귀를 재현한다");
            Assert.Less(result.MaxX - G_Origin.x, EDGE - CLEARANCE, "지면이 끊긴 쪽으로 걸어 들어갔다");
            Assert.IsFalse(result.Arrived);
            Assert.AreEqual(Vector2.zero, result.Last, "끊긴 곳 앞에서는 서 있어야 한다");
        }

        [Test]
        public void DeadEndWithNoSafeEscapeWaitsInsteadOfPushing()
        {
            // 판은 트였다고 보지만(벽, 바위를 판에 넣지 않았다) 몸은 앞의 낮은 바위와 좌우, 뒤 벽에 갇혔다.
            // 예전에는 어느 후보도 안전하지 않으면 첫 후보나 원래 방향으로 밀었다
            BakeNavMesh();
            AddBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE));
            AddBox("LeftWall", new Vector3(1f, WALL_HEIGHT * 0.5f, 0.75f), new Vector3(4f, WALL_HEIGHT, WALL_THICKNESS));
            AddBox("RightWall", new Vector3(1f, WALL_HEIGHT * 0.5f, -0.75f), new Vector3(4f, WALL_HEIGHT, WALL_THICKNESS));
            AddBox("BackWall", new Vector3(-0.75f, WALL_HEIGHT * 0.5f, 0f), new Vector3(WALL_THICKNESS, WALL_HEIGHT, 2f));
            AddBox("LowRock", new Vector3(0.9f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 1f));
            CapsuleCollider body = AddBody();
            Physics.SyncTransforms();

            var navigation = new BotNavigation(_config, body, ~0);
            for (int i = 0; i < 40; ++i)
            {
                Vector2 direction = navigation.Direction(G_Origin, At(10f, 0f), DecisionTime(i));
                Assert.AreEqual(Vector2.zero, direction, $"{i}번째 판단에서 막힌 쪽으로 밀었다");
            }

            Assert.GreaterOrEqual(navigation.StuckRecoveries, 2, "서 있는 동안에도 막힘 감지는 비킬 곳을 계속 찾아야 한다");
        }

        // 폭 3.2m 둑길(판 가장자리 1.1m) 또는 좌우 1.25m의 벽. 비켜 서기 시작할 때는 1m 앞이 안전하지만
        // 달리기로 0.6초 동안 같은 방향을 고집하면 3m 넘게 가서 떨어지거나 벽을 민다
        [TestCase(true,  TestName = "SidestepStopsBeforeLedge")]
        [TestCase(false, TestName = "SidestepStopsBeforeNewWall")]
        public void SidestepIsRecheckedEveryDecision(bool ledge)
        {
            const float HALF_WIDTH = 1.6f;
            const float SAFE_LIMIT = 1.05f; // 판 가장자리 1.1m, 벽 면 1.25m에서 CLEARANCE를 뺀 값보다 안쪽
            if (ledge)
            {
                BakeSources(Platform("Causeway", -FLOOR_HALF, FLOOR_HALF, -HALF_WIDTH, HALF_WIDTH, 0f));
            }
            else
            {
                BakeNavMesh();
                AddBox("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE));
                AddBox("NorthWall", new Vector3(0f, WALL_HEIGHT * 0.5f, 1.5f), new Vector3(FLOOR_SIZE, WALL_HEIGHT, WALL_THICKNESS));
                AddBox("SouthWall", new Vector3(0f, WALL_HEIGHT * 0.5f, -1.5f), new Vector3(FLOOR_SIZE, WALL_HEIGHT, WALL_THICKNESS));
            }

            CapsuleCollider body = AddBody();
            Physics.SyncTransforms();

            var     navigation    = new BotNavigation(_config, body, ~0);
            Vector3 position      = G_Origin;
            int     sidestepStart = -1;
            float   maxAbsZ       = 0f;
            bool    stopped       = false;
            for (int i = 0; (i < MAX_DECISIONS) && (!stopped); ++i)
            {
                int     before    = navigation.StuckRecoveries;
                Vector2 direction = navigation.Direction(position, At(10f, 0f), DecisionTime(i));
                if ((sidestepStart < 0) && (navigation.StuckRecoveries != before))
                {
                    sidestepStart = i;
                    Assert.Greater(Mathf.Abs(direction.y), 0.9f, "옆으로 비켜 서야 이 회귀를 재현한다");
                }

                // 비켜 서기 전까지는 무언가에 눌려 움직이지 못한다
                if (sidestepStart < 0)
                {
                    continue;
                }

                position += new Vector3(direction.x, 0f, direction.y) * RUN_STEP;
                maxAbsZ   = Mathf.Max(maxAbsZ, Mathf.Abs(position.z - G_Origin.z));
                stopped   = (i > sidestepStart) && (direction == Vector2.zero);
            }

            Assert.GreaterOrEqual(sidestepStart, 0, "막힘을 감지하지 못했다");
            Assert.IsTrue(stopped, "위험해진 비켜 서기를 멈추지 않았다");
            Assert.Less(maxAbsZ, SAFE_LIMIT, "비켜 서다 판 가장자리나 벽까지 갔다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private struct WalkResult
        {
            public Vector2 First;
            public Vector2 Last;
            public float   MaxX;
            public bool    Arrived;
            public string  Penetration;
        }

        /// <summary> 물리 없이 판단 방향대로 걷는다. 장애물 면에 CLEARANCE보다 가까이 들어가면 뚫은 것으로 기록한다 </summary>
        private WalkResult Walk(BotNavigation navigation, Vector3 start, Vector3 goal, params Bounds[] obstacles)
        {
            var     result   = new WalkResult { MaxX = start.x };
            Vector3 position = start;
            for (int i = 0; (i < MAX_DECISIONS) && (!result.Arrived); ++i)
            {
                Vector2 direction = navigation.Direction(position, goal, DecisionTime(i));
                if (i == 0)
                {
                    result.First = direction;
                }

                result.Last  = direction;
                position    += new Vector3(direction.x, 0f, direction.y) * STEP;
                result.MaxX  = Mathf.Max(result.MaxX, position.x);
                for (int b = 0; (b < obstacles.Length) && (result.Penetration == null); ++b)
                {
                    Bounds footprint = obstacles[b];
                    footprint.Expand(new Vector3(CLEARANCE * 2f, 0f, CLEARANCE * 2f));
                    if ((position.x > footprint.min.x) && (position.x < footprint.max.x) &&
                        (position.z > footprint.min.z) && (position.z < footprint.max.z))
                    {
                        result.Penetration = $"{i}번째 판단에서 장애물 {b}에 들어갔다: {position - G_Origin}";
                    }
                }

                result.Arrived = (Planar(goal - position).magnitude <= (_config.CornerTolerance + 0.1f));
            }

            return result;
        }

        /// <summary> 넓은 바닥 하나와 walls로 판을 굽는다. 바닥 충돌체는 만들지 않는다 </summary>
        private void BakeNavMesh(params NavMeshBuildSource[] walls)
        {
            var sources = new List<NavMeshBuildSource>
            {
                Box(new Vector3(0f, -0.1f, 0f), new Vector3(FLOOR_SIZE, FLOOR_THICK, FLOOR_SIZE)),
            };
            sources.AddRange(walls);
            BakeSources(sources.ToArray());
        }

        private void BakeSources(params NavMeshBuildSource[] sources)
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            Assert.AreEqual(0, settings.agentTypeID, "기본 에이전트 설정이 없다");

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource>(sources),
                new Bounds(G_Origin, new Vector3(FLOOR_SIZE + 10f, 10f, FLOOR_SIZE + 10f)),
                Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(data);
            _navMesh = NavMesh.AddNavMeshData(data);
            Assert.IsTrue(_navMesh.valid);
        }

        /// <summary> 윗면 높이가 top인 바닥판. 판과 충돌체를 같은 모양으로 만들어 지면과 판이 일치한다 </summary>
        private NavMeshBuildSource Platform(string name, float minX, float maxX, float minZ, float maxZ, float top)
        {
            Vector3 center = new Vector3((minX + maxX) * 0.5f, top - (FLOOR_THICK * 0.5f), (minZ + maxZ) * 0.5f);
            Vector3 size   = new Vector3(maxX - minX, FLOOR_THICK, maxZ - minZ);
            AddBox(name, center, size);
            return Box(center, size);
        }

        /// <summary> 바닥에 세운 벽. center는 바닥 기준 발 위치다 </summary>
        private static NavMeshBuildSource Wall(Vector3 center, Vector3 size)
        {
            return Box(center + (Vector3.up * (size.y * 0.5f)), size);
        }

        private static NavMeshBuildSource Box(Vector3 center, Vector3 size)
        {
            return new NavMeshBuildSource
            {
                shape     = NavMeshBuildSourceShape.Box,
                size      = size,
                area      = 0,
                transform = Matrix4x4.TRS(G_Origin + center, Quaternion.identity, Vector3.one),
            };
        }

        private BoxCollider AddBox(string name, Vector3 center, Vector3 size)
        {
            var box = new GameObject(name, typeof(BoxCollider));
            box.transform.position = G_Origin + center;
            box.GetComponent<BoxCollider>().size = size;
            _objects.Add(box);
            return box.GetComponent<BoxCollider>();
        }

        /// <summary> 플레이어 프리팹과 같은 이동 캡슐 </summary>
        private CapsuleCollider AddBody()
        {
            var body = new GameObject("BotBody", typeof(CapsuleCollider));
            body.transform.position = G_Origin;
            CapsuleCollider capsule = body.GetComponent<CapsuleCollider>();
            capsule.radius = 0.25f;
            capsule.height = 1.54f;
            capsule.center = new Vector3(0f, 0.77f, 0f);
            _objects.Add(body);
            return capsule;
        }

        private static Vector3 At(float x, float z) { return G_Origin + new Vector3(x, 0f, z); }

        private static double DecisionTime(int decision) { return START + (decision * (double)DECISION); }

        private static Vector2 Planar(Vector3 vector) { return new Vector2(vector.x, vector.z); }
    }
}
