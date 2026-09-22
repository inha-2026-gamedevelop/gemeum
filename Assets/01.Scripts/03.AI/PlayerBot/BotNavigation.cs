using Geumeum.Data;
using UnityEngine;
using UnityEngine.AI;

namespace Geumeum.AI
{
    /// <summary> 경로의 방향만 생성한다. 위치, 속도 적용은 기존 플레이어 실행기가 담당한다 </summary>
    public sealed class BotNavigation
    {
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly Vector3[] _corners = new Vector3[64];
        private readonly BotConfigSO _config;
        private int _count;
        private int _corner;
        private double _nextPath;
        private double _progressAt;
        private Vector3 _progressPosition;
        public int Failures { get; private set; }

        public BotNavigation(BotConfigSO config) => _config = config;

        public Vector2 Direction(Vector3 position, Vector3 goal, double now)
        {
            if (now >= _nextPath)
            {
                _nextPath = now + _config.PathInterval;
                _count = 0; _corner = 0;
                if ((!NavMesh.SamplePosition(position, out NavMeshHit start, _config.NavSampleRadius, NavMesh.AllAreas)) ||
                    (!NavMesh.SamplePosition(goal, out NavMeshHit end, _config.NavSampleRadius, NavMesh.AllAreas)) ||
                    (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path)) ||
                    (_path.status != NavMeshPathStatus.PathComplete))
                { ++Failures; return Vector2.zero; }
                _count = _path.GetCornersNonAlloc(_corners);
                if (_count == _corners.Length) { _count = 0; ++Failures; }
            }

            if ((_progressAt == 0d) || ((position - _progressPosition).sqrMagnitude >=
                _config.StuckDistance * _config.StuckDistance))
            { _progressPosition = position; _progressAt = now; }
            else if (now - _progressAt >= _config.StuckSeconds)
            {
                ++Failures; Clear(); _nextPath = now + _config.PathInterval;
                return Vector2.zero;
            }

            while (_corner < _count)
            {
                Vector3 offset = _corners[_corner] - position;
                Vector2 direction = new Vector2(offset.x, offset.z);
                if (direction.sqrMagnitude > _config.CornerTolerance * _config.CornerTolerance)
                { return direction.normalized; }
                ++_corner;
            }
            return Vector2.zero;
        }

        public void Clear() { _count = 0; _corner = 0; _nextPath = 0d; _progressAt = 0d; }
    }
}
