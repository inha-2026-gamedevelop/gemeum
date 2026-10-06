#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;
using UnityEngine.AI;

// Third party
using Mirror;

// Project
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary>
    /// 봇 추적 검증용 배치. 리더와 봇 사이를 실제 월드 장애물이 가로막아 판을 따라 돌아가야 하는 두 지점을 찾아 세운다.
    /// 위치만 옮기고 이후 추적은 서버 봇 판단이 그대로 한다
    /// </summary>
    public sealed partial class VerificationCommandDriver
    {
        private const float OBSTACLE_SETUP_SEPARATION   = 8f;    // index가 0 이하일 때 리더와 봇 사이 직선 거리(m)
        private const float OBSTACLE_SETUP_RADIUS       = 40f;   // secondaryIndex가 0 이하일 때 리더 주변 탐색 반경(m)
        private const float OBSTACLE_SETUP_GRID         = 3f;    // 후보 격자 간격(m)
        private const float OBSTACLE_SETUP_SAMPLE       = 4f;    // 후보를 판에 붙이는 반경(m), 비탈에서도 붙게 넉넉히 잡는다
        private const float OBSTACLE_SETUP_SNAP         = 1f;    // 붙인 리더 자리가 목표 지점에서 벗어나도 되는 평면 거리(m)
        private const float OBSTACLE_SETUP_LEVEL        = 2f;    // 두 자리의 높이 차 상한(m)
        private const float OBSTACLE_SETUP_DETOUR_RATIO = 1.3f;  // 판 경로가 직선보다 이만큼 길어야 우회로 본다
        private const float OBSTACLE_SETUP_EYE          = 0.8f;  // 가로막힘을 보는 높이(m), 캡슐이 못 넘는 턱도 잡는다
        private const float OBSTACLE_SETUP_WALL_NORMAL  = 0.7f;  // 맞은 면의 법선 y가 이보다 작아야 벽으로 본다
        private const float OBSTACLE_SETUP_BOT_SPACING  = 1f;    // 봇끼리 옆으로 벌리는 간격(m)
        private const int   OBSTACLE_SETUP_DIRECTIONS   = 8;
        private const int   OBSTACLE_SETUP_CORNERS      = 64;

        // index: 직선 거리(m), secondaryIndex: 탐색 반경(m). 둘 다 0 이하면 기본값을 쓴다
        private static bool TrySetupObstacleFollow(VerificationCommand command, out string message)
        {
            if (!NetworkServer.active)
            {
                message = "서버 호스트 전용";
                return false;
            }

            PlayerStatus leader = FindBotLeader();
            if (leader == null)
            {
                message = "살아 있는 사람 리더가 없다";
                return false;
            }

            int worldMask = ((GameDB.IsInitialized) && (GameDB.Arrow != null)) ? GameDB.Arrow.WorldMask : 0;
            if (worldMask == 0)
            {
                message = "월드 지형 마스크를 읽지 못했다";
                return false;
            }

            float separation = (command.index > 0) ? command.index : OBSTACLE_SETUP_SEPARATION;
            float radius     = (command.secondaryIndex > 0) ? command.secondaryIndex : OBSTACLE_SETUP_RADIUS;
            if (!TryFindObstaclePair(leader.transform.position, separation, radius, worldMask,
                out Vector3 botPoint, out Vector3 leaderPoint, out float pathLength, out string blocker))
            {
                message = $"반경 {radius}m 안에서 직선 {separation}m를 가로막는 장애물 배치를 찾지 못했다";
                return false;
            }

            leader.GetComponent<PlayerController>().ServerTeleport(leaderPoint);

            Vector3 toLeader = leaderPoint - botPoint;
            Vector3 side     = Vector3.Cross(Vector3.up, new Vector3(toLeader.x, 0f, toLeader.z).normalized);
            int     placed   = 0;
            foreach (PlayerStatus player in PlayerStatus.ServerPlayers)
            {
                if ((player == null) || (player.Participant.Kind != ParticipantKind.Bot) || player.IsDead || player.IsSwallowed)
                {
                    continue;
                }

                // 봇끼리 겹치지 않게 좌우로 번갈아 벌리고 판 위에 다시 붙인다
                float   offset = (placed == 0) ? 0f : (((placed + 1) / 2) * OBSTACLE_SETUP_BOT_SPACING * (((placed % 2) == 0) ? -1f : 1f));
                Vector3 point  = botPoint + (side * offset);
                if (NavMesh.SamplePosition(point, out NavMeshHit hit, OBSTACLE_SETUP_SAMPLE, NavMesh.AllAreas))
                {
                    point = hit.position;
                }

                player.GetComponent<PlayerController>().ServerTeleport(point);
                ++placed;
            }

            float straight = new Vector2(toLeader.x, toLeader.z).magnitude;
            message = $"leader={leader.netId}@{leaderPoint}, bots={placed}@{botPoint}, straight={straight:F1}, path={pathLength:F1}, blocker={blocker}";
            return placed > 0;
        }

        private static PlayerStatus FindBotLeader()
        {
            // 봇 판단(PlayerBrain)과 같은 규칙: 살아 있는 사람 중 참가자 ID가 가장 앞선 사람
            PlayerStatus leader = null;
            foreach (PlayerStatus player in PlayerStatus.ServerPlayers)
            {
                if ((player == null) || (!player.Participant.IsValid) ||
                    (player.Participant.Kind != ParticipantKind.Human) || player.IsDead || player.IsSwallowed)
                {
                    continue;
                }

                if ((leader == null) || (player.Participant.CompareTo(leader.Participant) < 0))
                {
                    leader = player;
                }
            }

            return leader;
        }

        /// <summary> 가까운 격자부터 봇 자리와 리더 자리를 짝지어, 직선은 벽에 막히고 판 경로는 이어지되 돌아가야 하는 첫 짝을 고른다 </summary>
        private static bool TryFindObstaclePair(Vector3 origin, float separation, float radius, int worldMask,
            out Vector3 botPoint, out Vector3 leaderPoint, out float pathLength, out string blocker)
        {
            botPoint    = origin;
            leaderPoint = origin;
            pathLength  = 0f;
            blocker     = string.Empty;

            NavMeshPath path    = new NavMeshPath();
            Vector3[]   corners = new Vector3[OBSTACLE_SETUP_CORNERS];
            int         cells   = Mathf.CeilToInt(radius / OBSTACLE_SETUP_GRID);
            for (int ring = 0; ring <= cells; ++ring)
            {
                for (int x = -ring; x <= ring; ++x)
                {
                    for (int z = -ring; z <= ring; ++z)
                    {
                        if ((Mathf.Abs(x) != ring) && (Mathf.Abs(z) != ring))
                        {
                            continue;
                        }

                        Vector3 cell = origin + new Vector3(x * OBSTACLE_SETUP_GRID, 0f, z * OBSTACLE_SETUP_GRID);
                        if (!NavMesh.SamplePosition(cell, out NavMeshHit from, OBSTACLE_SETUP_SAMPLE, NavMesh.AllAreas))
                        {
                            continue;
                        }

                        for (int d = 0; d < OBSTACLE_SETUP_DIRECTIONS; ++d)
                        {
                            float   angle  = (360f / OBSTACLE_SETUP_DIRECTIONS) * d * Mathf.Deg2Rad;
                            Vector3 target = from.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * separation);
                            if (!NavMesh.SamplePosition(target, out NavMeshHit to, OBSTACLE_SETUP_SAMPLE, NavMesh.AllAreas))
                            {
                                continue;
                            }

                            Vector3 snap = to.position - target;
                            if ((new Vector2(snap.x, snap.z).magnitude > OBSTACLE_SETUP_SNAP) ||
                                (Mathf.Abs(to.position.y - from.position.y) > OBSTACLE_SETUP_LEVEL))
                            {
                                continue;
                            }

                            Vector3 eye = Vector3.up * OBSTACLE_SETUP_EYE;
                            if ((!Physics.Linecast(from.position + eye, to.position + eye, out RaycastHit wall,
                                    worldMask, QueryTriggerInteraction.Ignore)) || (wall.normal.y >= OBSTACLE_SETUP_WALL_NORMAL))
                            {
                                continue;
                            }

                            if ((!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path)) ||
                                (path.status != NavMeshPathStatus.PathComplete))
                            {
                                continue;
                            }

                            float length   = PathLength(path, corners);
                            float straight = Vector3.Distance(from.position, to.position);
                            if (length < (straight * OBSTACLE_SETUP_DETOUR_RATIO))
                            {
                                continue;
                            }

                            botPoint    = from.position;
                            leaderPoint = to.position;
                            pathLength  = length;
                            blocker     = wall.collider.name;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static float PathLength(NavMeshPath path, Vector3[] corners)
        {
            int   count  = path.GetCornersNonAlloc(corners);
            float length = 0f;
            for (int i = 1; i < count; ++i)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }
    }
}
#endif
