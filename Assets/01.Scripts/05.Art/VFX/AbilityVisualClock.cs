using System;
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>Client presentation only. Sparse run-clock snapshots never drive hit/resource decisions here</summary>
    public struct AbilityVisualClock
    {
        private bool _initialized, _paused;
        private double _raw, _wall, _now;

        public double Sample(double raw, double wall, bool paused, bool authoritative)
        {
            if (!_initialized || paused || authoritative || _paused || raw < _raw - 1)
            {
                _now = raw;
            }
            else
            {
                _now = Math.Min(Math.Max(raw, _now + Math.Max(0, wall - _wall)), raw + Constants.Network.RUN_TIME_SYNC_INTERVAL);
            }

            _raw = raw; _wall = wall; _paused = paused; _initialized = true;
            return _now;
        }

        public void ObserveCue(double time)
        {
            if (_initialized && !_paused && !double.IsNaN(time) && !double.IsInfinity(time))
            {
                _now = Math.Max(_now, time);
            }
        }
    }
}
