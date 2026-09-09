// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> Network가 Systems의 런 사실 동결 구현을 찾는 창구 </summary>
    public static class RunResultFactsProviderLocator
    {
        private static IRunResultFactsProvider _current = null;

        public static IRunResultFactsProvider Current => _current;
        public static bool HasProvider => (_current != null);

        public static void Register(IRunResultFactsProvider provider)
        {
            if (provider == null)
            {
                Debug.LogError("[RunResultFactsProviderLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = provider;
        }

        public static void Unregister(IRunResultFactsProvider provider)
        {
            if (_current != provider)
            {
                return;
            }

            _current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _current = null;
        }
    }
}
