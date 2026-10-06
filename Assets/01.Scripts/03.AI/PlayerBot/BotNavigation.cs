// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 경로의 방향만 만든다. 위치, 속도 적용은 기존 플레이어 실행기가 담당한다.
    /// NavMesh 경로를 따르되 판이 지나가도 몸이 못 넘는 턱은 캡슐로 미리 훑어 돌아가고,
    /// 그래도 제자리면 옆이나 뒤로 비켜 선 뒤 새 자리에서 경로를 다시 찾는다
    /// </summary>
    public sealed class BotNavigation
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   CORNER_CAPACITY         = 64;
        private const int   PROBE_HIT_CAPACITY      = 16;
        private const int   ESCAPE_OPTION_COUNT     = 3;     // 트던 쪽, 반대쪽, 뒤
        private const float PROBE_RADIUS_SCALE      = 0.9f;  // 몸에 붙은 벽이 시작 겹침으로 빠지지 않게 몸보다 조금 가늘게 훑는다
        private const float WALKABLE_SLOPE_FALLBACK = 45f;   // 기본 에이전트 설정을 못 읽을 때 걸을 수 있다고 보는 경사(도)
        private const float SIDESTEP_ANGLE          = 90f;   // 막힘 복구로 비켜 서는 방향(도)
        private const float STUCK_REPEAT_RADIUS     = 1f;    // 이 안에서 다시 막히면 같은 장애물로 보고 다른 쪽으로 비킨다(m)
        private const float STALE_CALL_SECONDS      = 0.5f;  // 이보다 오래 방향을 묻지 않았으면 그 사이 서 있던 시간은 막힘이 아니다
        private const float GROUND_PROBE_SPACING    = 0.25f; // 발밑 지면이 이어지는지 내려 찍어 보는 간격(m)
        private const float GROUND_PROBE_MARGIN     = 0.05f; // 지면 탐지 광선을 허용 높이보다 조금 위에서 쏜다(m)
        private const float DIRECTION_EPSILON_SQR   = 0.0001f;

        private readonly NavMeshPath  _path    = new NavMeshPath();
        private readonly Vector3[]    _corners = new Vector3[CORNER_CAPACITY];
        private readonly RaycastHit[] _hits    = new RaycastHit[PROBE_HIT_CAPACITY];
        private readonly BotConfigSO  _config;
        private readonly Transform    _body;
        private readonly int          _obstacleMask;
        private readonly float        _probeRadius;
        private readonly float        _probeBottom;     // 발에서 탐지 캡슐 아래 구 중심까지(m)
        private readonly float        _probeTop;        // 발에서 탐지 캡슐 위 구 중심까지(m)
        private readonly float        _walkableNormalY;
        private readonly float        _walkableSlopeTan;

        private int     _count;
        private int     _corner;
        private bool    _partial;
        private double  _nextPath;
        private int     _avoidSide;
        private bool    _hasProgress;
        private double  _progressAt;
        private Vector3 _progressPosition;
        private double  _lastCallAt = double.NegativeInfinity;
        private double  _unstickUntil;
        private Vector2 _unstickDirection;
        private int     _stuckStreak;
        private Vector3 _lastStuckPosition;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 경로를 못 찾았거나 막힌 횟수 </summary>
        public int Failures { get; private set; }

        /// <summary> 목표까지 이어지지 않아 가장 가까운 곳까지만 가는 경로를 계산한 횟수 </summary>
        public int PartialPaths { get; private set; }

        /// <summary> 막힘을 감지해 비켜 선 횟수 </summary>
        public int StuckRecoveries { get; private set; }

        public bool IsPartialPath => _partial;

        private bool CanProbe => (_body != null) && (_obstacleMask != 0);

        /****************************************
        *              Constructors
        ****************************************/

        public BotNavigation(BotConfigSO config) : this(config, null, 0) { }

        /// <summary> body가 없거나 obstacleMask가 0이면 캡슐로 훑지 않고 판 경로만 따른다 </summary>
        public BotNavigation(BotConfigSO config, CapsuleCollider body, int obstacleMask)
        {
            _config       = config;
            _obstacleMask = obstacleMask;

            // 판을 구운 기본 에이전트의 경사 한계를 그대로 쓴다. 판이 걷는다는 비탈을 벽으로 보면 오르막에서 멈춘다
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            float slope = (settings.agentTypeID == 0) ? settings.agentSlope : WALKABLE_SLOPE_FALLBACK;
            _walkableNormalY  = Mathf.Cos(slope * Mathf.Deg2Rad);
            _walkableSlopeTan = Mathf.Tan(slope * Mathf.Deg2Rad);

            if (body == null)
            {
                return;
            }

            // 세로 캡슐만 다룬다. 플레이어 이동 캡슐이 그렇다
            Vector3 scale  = body.transform.lossyScale;
            float   radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float   height = Mathf.Max(body.height * Mathf.Abs(scale.y), radius * 2f);
            float   center = body.center.y * scale.y;

            _body        = body.transform;
            _probeRadius = radius * PROBE_RADIUS_SCALE;
            _probeBottom = (center - (height * 0.5f)) + _config.StepClearance + _probeRadius;
            _probeTop    = Mathf.Max(_probeBottom, (center + (height * 0.5f)) - _probeRadius);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> goal로 가는 이번 판단의 이동 방향. 판단마다 부른다 </summary>
        public Vector2 Direction(Vector3 position, Vector3 goal, double now)
        {
            // 공격이나 관찰로 한동안 방향을 묻지 않았으면 그 사이 서 있던 시간은 막힘으로 세지 않는다
            if ((now - _lastCallAt) > Mathf.Max(STALE_CALL_SECONDS, _config.DecisionInterval * 2f))
            {
                _hasProgress = false;
            }

            _lastCallAt = now;

            if (now < _unstickUntil)
            {
                return ContinueUnstick(position);
            }

            if (now >= _nextPath)
            {
                Replan(position, goal, now);
            }

            Vector2 desired = SteerAlongPath(position, goal);
            if (desired.sqrMagnitude < DIRECTION_EPSILON_SQR)
            {
                // 도착했거나 갈 곳이 없다. 서 있는 시간을 막힘으로 세지 않는다
                _hasProgress = false;
                return Vector2.zero;
            }

            if (IsStuck(position, now))
            {
                return BeginUnstick(position, desired, now);
            }

            return Avoid(position, desired);
        }

        /// <summary>
        /// anchor에서 desired까지 판 위로 곧게 갈 수 없으면 가로막힌 지점을 돌려준다.
        /// 대형 자리가 바위나 벽 너머로 잡히면 그 너머로 멀리 돌아가지 않고 anchor 쪽 가장자리에 선다
        /// </summary>
        public Vector3 ClampToReachable(Vector3 anchor, Vector3 desired)
        {
            if (!NavMesh.SamplePosition(anchor, out NavMeshHit from, _config.NavSampleRadius, NavMesh.AllAreas))
            {
                return desired;
            }

            if (!NavMesh.Raycast(from.position, desired, out NavMeshHit blocked, NavMesh.AllAreas))
            {
                return desired;
            }

            return blocked.position;
        }

        /// <summary>
        /// 경로와 비켜 서기를 버린다. 분기나 목표가 바뀔 때 부른다.
        /// 막힘 판정용 이동 기록은 남긴다 - 목표가 자주 바뀌어도 같은 자리에 선 시간은 계속 센다
        /// </summary>
        public void Clear()
        {
            _count        = 0;
            _corner       = 0;
            _partial      = false;
            _nextPath     = 0d;
            _avoidSide    = 0;
            _unstickUntil = 0d;
        }

        private void Replan(Vector3 position, Vector3 goal, double now)
        {
            _nextPath = now + _config.PathInterval;
            _count    = 0;
            _corner   = 0;
            _partial  = false;

            // 밀려나 판 밖에 선 봇이나 바위 위로 잡힌 목표도 조금 더 넓게 찾아 판으로 돌아온다
            if ((!TrySample(position, out Vector3 start)) || (!TrySample(goal, out Vector3 end)) ||
                (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _path)) ||
                (_path.status == NavMeshPathStatus.PathInvalid))
            {
                ++Failures;
                return;
            }

            // 버퍼보다 긴 경로는 앞부분만 따른다. 곧 다시 찾으므로 끝까지 필요 없다
            _count = _path.GetCornersNonAlloc(_corners);

            // 목표 쪽 판이 끊겨 있어도 가장 가까운 곳까지는 간다. 제자리에 서 있으면 리더가 돌아와도 한참 뒤처진다
            if (_path.status == NavMeshPathStatus.PathPartial)
            {
                _partial = true;
                ++PartialPaths;
            }
        }

        private bool TrySample(Vector3 point, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, _config.NavSampleRadius, NavMesh.AllAreas) ||
                ((_config.NavRecoverRadius > _config.NavSampleRadius) &&
                 NavMesh.SamplePosition(point, out hit, _config.NavRecoverRadius, NavMesh.AllAreas)))
            {
                sampled = hit.position;
                return true;
            }

            sampled = point;
            return false;
        }

        private Vector2 SteerAlongPath(Vector3 position, Vector3 goal)
        {
            float   tolerance = _config.CornerTolerance;
            Vector2 toGoal    = Planar(goal - position);
            float   distance  = toGoal.magnitude;
            if (distance <= tolerance)
            {
                return Vector2.zero;
            }

            // 판은 끊겼어도 몸이 지나갈 좁은 틈이면 목표로 곧장 간다. 부분 경로의 끝은 틈 앞이고,
            // 틈 안에서 다시 찾으면 판이 뒤쪽에 잡혀 되돌아가므로 경로 모서리보다 먼저 본다.
            // 판이 끊긴 이유가 틈이나 낭떠러지일 수 있으므로 앞이 트인 것만으로 가지 않고,
            // 발밑 지면이 목표 높이까지 끊기지 않고 이어질 때만 간다
            if (_partial && CanProbe && (distance <= _config.DirectApproachDistance))
            {
                Vector2 direct = toGoal / distance;
                if (IsClear(position, direct, distance) &&
                    HasGroundSupport(position, direct, distance, out float endGround) &&
                    (Mathf.Abs(endGround - goal.y) <= GroundStepLimit()))
                {
                    return direct;
                }
            }

            while (_corner < _count)
            {
                Vector2 offset = Planar(_corners[_corner] - position);
                if (offset.sqrMagnitude > (tolerance * tolerance))
                {
                    return offset.normalized;
                }

                ++_corner;
            }

            return Vector2.zero;
        }

        private bool IsStuck(Vector3 position, double now)
        {
            float moved = Planar(position - _progressPosition).sqrMagnitude;
            if ((!_hasProgress) || (moved >= (_config.StuckDistance * _config.StuckDistance)))
            {
                _hasProgress      = true;
                _progressAt       = now;
                _progressPosition = position;
                return false;
            }

            return ((now - _progressAt) >= _config.StuckSeconds);
        }

        /// <summary>
        /// 처음에는 트던 쪽(없으면 트인 쪽)으로 비키고, 같은 자리에서 또 막히면 반대쪽, 그다음 뒤로 물러선다.
        /// 예전처럼 같은 경로를 다시 받기만 하면 같은 바위를 계속 민다
        /// </summary>
        private Vector2 BeginUnstick(Vector3 position, Vector2 desired, double now)
        {
            ++Failures;
            ++StuckRecoveries;

            bool isSameSpot = (StuckRecoveries > 1) &&
                (Planar(position - _lastStuckPosition).sqrMagnitude <= (STUCK_REPEAT_RADIUS * STUCK_REPEAT_RADIUS));
            _stuckStreak       = isSameSpot ? (_stuckStreak + 1) : 0;
            _lastStuckPosition = position;

            int     side       = (_avoidSide != 0) ? _avoidSide : PickSide(position, desired);
            bool    onMesh     = TrySampleNearby(position, out Vector3 meshStart);
            int     first      = _stuckStreak % ESCAPE_OPTION_COUNT;
            Vector2 escape     = Vector2.zero;
            int     escapeSide = side;
            for (int i = 0; i < ESCAPE_OPTION_COUNT; ++i)
            {
                Vector2 candidate = EscapeOption((first + i) % ESCAPE_OPTION_COUNT, side, desired, out int candidateSide);
                if (IsOpen(position, candidate, onMesh, meshStart))
                {
                    escape     = candidate;
                    escapeSide = candidateSide;
                    break;
                }
            }

            // 안전하게 비킬 곳이 없으면 막힌 쪽이나 판 밖으로 밀지 않고 그 시간만큼 서서 기다린다
            _avoidSide        = (escapeSide != 0) ? escapeSide : side;
            _unstickDirection = escape;
            _unstickUntil     = now + _config.UnstickSeconds;

            // 비키거나 기다린 뒤에는 새 자리에서 경로를 다시 찾는다
            _nextPath    = 0d;
            _count       = 0;
            _corner      = 0;
            _hasProgress = false;
            return escape;
        }

        /// <summary>
        /// 비켜 서는 동안에도 판단마다 지금 자리에서 앞을 다시 본다. 시작할 때 본 거리 너머에서
        /// 낭떠러지나 새 장애물을 만날 수 있다. 막히면 비키기를 멈추고 새 자리에서 경로를 다시 찾는다
        /// </summary>
        private Vector2 ContinueUnstick(Vector3 position)
        {
            // 안전하게 비킬 곳이 없어 기다리는 중
            if (_unstickDirection.sqrMagnitude < DIRECTION_EPSILON_SQR)
            {
                return Vector2.zero;
            }

            bool onMesh = TrySampleNearby(position, out Vector3 meshStart);
            if (IsOpen(position, _unstickDirection, onMesh, meshStart))
            {
                return _unstickDirection;
            }

            _unstickUntil = 0d;
            _nextPath     = 0d;
            _count        = 0;
            _corner       = 0;
            _hasProgress  = false;
            return Vector2.zero;
        }

        private static Vector2 EscapeOption(int option, int side, Vector2 desired, out int optionSide)
        {
            switch (option)
            {
                case 0:
                    optionSide = side;
                    return Rotate(desired, SIDESTEP_ANGLE * side);
                case 1:
                    optionSide = -side;
                    return Rotate(desired, -SIDESTEP_ANGLE * side);
                default:
                    optionSide = 0;
                    return -desired;
            }
        }

        private Vector2 Avoid(Vector3 position, Vector2 desired)
        {
            if ((!CanProbe) || IsClear(position, desired, _config.AvoidanceProbeDistance))
            {
                _avoidSide = 0;
                return desired;
            }

            if (_avoidSide == 0)
            {
                _avoidSide = PickSide(position, desired);
            }

            bool  onMesh = TrySampleNearby(position, out Vector3 meshStart);
            float step   = Mathf.Max(1f, _config.AvoidanceAngleStep);
            int   steps  = Mathf.Max(1, Mathf.FloorToInt(_config.AvoidanceMaxAngle / step));
            for (int i = 1; i <= steps; ++i)
            {
                // 같은 각도면 트던 쪽을 먼저 본다. 매번 새로 고르면 장애물 앞에서 좌우로 떤다
                for (int turn = 0; turn < 2; ++turn)
                {
                    int     side      = (turn == 0) ? _avoidSide : -_avoidSide;
                    Vector2 candidate = Rotate(desired, step * i * side);
                    if (IsOpen(position, candidate, onMesh, meshStart))
                    {
                        _avoidSide = side;
                        return candidate;
                    }
                }
            }

            // 앞이 다 막혔다. 막힌 쪽으로 밀지 않고 서 있으면 막힘 감지가 비켜 설 곳을 찾는다
            return Vector2.zero;
        }

        /// <summary> 한 칸 틀었을 때 더 멀리 트인 쪽. 같으면 왼쪽(+1)이다 </summary>
        private int PickSide(Vector3 position, Vector2 desired)
        {
            if (!CanProbe)
            {
                return 1;
            }

            float step  = Mathf.Max(1f, _config.AvoidanceAngleStep);
            float left  = ClearDistance(position, Rotate(desired, step), _config.AvoidanceProbeDistance);
            float right = ClearDistance(position, Rotate(desired, -step), _config.AvoidanceProbeDistance);
            return (right > left) ? -1 : 1;
        }

        /// <summary>
        /// 봇이 판단 사이에 걷는 거리보다 AvoidanceProbeDistance가 길어야 다음 판단 전에 위험을 다시 본다.
        /// 판단 0.1초, 달리기 기준 약 0.55m라 1m면 충분하다
        /// </summary>
        private bool IsOpen(Vector3 position, Vector2 direction, bool onMesh, Vector3 meshStart)
        {
            // 캡슐 전방 훑기는 앞을 막는 것만 찾는다. 발밑 지면이 이어지는지는 따로 본다
            float distance = _config.AvoidanceProbeDistance;
            if (CanProbe && ((!IsClear(position, direction, distance)) ||
                (!HasGroundSupport(position, direction, distance, out float _))))
            {
                return false;
            }

            // 판 가장자리 밖(낭떠러지, 경계 벽 너머)으로는 틀지 않는다.
            // 판 밖에 서 있는데 몸으로도 확인하지 못했으면 안전하다는 근거가 없다
            if (!onMesh)
            {
                return CanProbe;
            }

            Vector3 end = meshStart + (new Vector3(direction.x, 0f, direction.y) * distance);
            return !NavMesh.Raycast(meshStart, end, out NavMeshHit _, NavMesh.AllAreas);
        }

        /// <summary>
        /// direction으로 distance만큼 가는 동안 발밑에 걸을 수 있는 지면이 끊기지 않고 이어지는지 본다.
        /// 캡슐 전방 훑기는 앞을 막는 것만 찾으므로 틈이나 낭떠러지를 트여 있다고 본다
        /// </summary>
        private bool HasGroundSupport(Vector3 position, Vector2 direction, float distance, out float groundY)
        {
            groundY = position.y;

            float   limit = GroundStepLimit();
            int     steps = Mathf.Max(1, Mathf.CeilToInt(distance / GROUND_PROBE_SPACING));
            Vector3 sweep = new Vector3(direction.x, 0f, direction.y);
            for (int i = 1; i <= steps; ++i)
            {
                Vector3 point = position + (sweep * Mathf.Min(i * GROUND_PROBE_SPACING, distance));
                if (!TryFindGround(point, groundY, limit, out groundY))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> reference 높이에서 limit 안쪽으로 오르내린 가장 위 지면이 걸을 수 있는 경사인지 본다 </summary>
        private bool TryFindGround(Vector3 point, float reference, float limit, out float groundY)
        {
            groundY = reference;

            Vector3 origin = new Vector3(point.x, reference + limit + GROUND_PROBE_MARGIN, point.z);
            int     count  = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, (limit * 2f) + GROUND_PROBE_MARGIN,
                _obstacleMask, QueryTriggerInteraction.Ignore);

            float nearest  = float.PositiveInfinity;
            bool  walkable = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _hits[i];
                if ((hit.distance >= nearest) || hit.transform.IsChildOf(_body))
                {
                    continue;
                }

                nearest  = hit.distance;
                walkable = (hit.normal.y >= _walkableNormalY);
                groundY  = hit.point.y;
            }

            return walkable;
        }

        /// <summary> 지면 탐지 한 칸 사이에 몸이 오르내릴 수 있는 높이. 턱 높이에 걸을 수 있는 경사만큼 더한다 </summary>
        private float GroundStepLimit()
        {
            return _config.StepClearance + (GROUND_PROBE_SPACING * _walkableSlopeTan);
        }

        private bool TrySampleNearby(Vector3 position, out Vector3 meshPoint)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, _config.NavSampleRadius, NavMesh.AllAreas))
            {
                meshPoint = hit.position;
                return true;
            }

            meshPoint = position;
            return false;
        }

        private bool IsClear(Vector3 position, Vector2 direction, float distance)
        {
            return (ClearDistance(position, direction, distance) >= distance);
        }

        /// <summary>
        /// 몸 캡슐을 턱 높이만큼 들어 direction으로 훑어 가장 가까운 장애물까지 거리를 준다.
        /// 걸을 수 있는 경사의 지면과 자기 몸은 장애물로 보지 않는다
        /// </summary>
        private float ClearDistance(Vector3 position, Vector2 direction, float distance)
        {
            Vector3 bottom = position + (Vector3.up * _probeBottom);
            Vector3 top    = position + (Vector3.up * _probeTop);
            Vector3 sweep  = new Vector3(direction.x, 0f, direction.y);
            int     count  = Physics.CapsuleCastNonAlloc(bottom, top, _probeRadius, sweep, _hits, distance,
                _obstacleMask, QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _hits[i];

                // 시작부터 겹친 충돌체는 방향을 알 수 없어 뺀다. 몸에 닿은 벽은 탐지 캡슐이 가늘어 여기 오지 않는다
                if ((hit.distance <= 0f) || (hit.normal.y >= _walkableNormalY) || hit.transform.IsChildOf(_body))
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, hit.distance);
            }

            return nearest;
        }

        private static Vector2 Planar(Vector3 vector) { return new Vector2(vector.x, vector.z); }

        /// <summary> 위에서 볼 때 반시계(왼쪽)가 양수다 </summary>
        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos     = Mathf.Cos(radians);
            float sin     = Mathf.Sin(radians);
            return new Vector2((direction.x * cos) - (direction.y * sin), (direction.x * sin) + (direction.y * cos));
        }
    }
}
