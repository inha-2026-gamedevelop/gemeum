// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 파트가 Systems 구현을 참조하지 않고 서버 결과 사실을 기록하는 창구 </summary>
    public static class RunStatisticsSinkLocator
    {
        private static readonly NullRunStatisticsSink _fallback = new NullRunStatisticsSink();

        private static IRunStatisticsSink _current = _fallback;

        public static IRunStatisticsSink Current => _current;
        public static bool HasSink => (_current != _fallback);

        public static void Register(IRunStatisticsSink sink)
        {
            if (sink == null)
            {
                Debug.LogError("[RunStatisticsSinkLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = sink;
        }

        public static void Unregister(IRunStatisticsSink sink)
        {
            if (_current != sink)
            {
                return;
            }

            _current = _fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _current = _fallback;
        }

        private sealed class NullRunStatisticsSink : IRunStatisticsSink
        {
            public bool RecordDamage(in RunAppliedDamageFact fact) => false;
            public bool RecordHealing(in RunAppliedHealingFact fact) => false;
            public bool RecordLife(in RunPlayerLifeFact fact) => false;
            public bool RecordCounter(in RunCounterFact fact) => false;
            public bool RecordParticipantState(in RunParticipantStateFact fact) => false;
        }
    }
}
