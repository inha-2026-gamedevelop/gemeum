namespace Geumeum.Common
{
    /// <summary> 씬의 성장 관리자와 다른 파트 사이를 잇는 공용 창구 </summary>
    public static class GrowthProviderLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static IGrowthProvider _current;

        /****************************************
        *              Properties
        ****************************************/

        public static IGrowthProvider Current => _current;
        public static bool HasProvider => (_current != null);

        /****************************************
        *                Methods
        ****************************************/

        public static void Register(IGrowthProvider provider)
        {
            if (provider != null)
            {
                _current = provider;
            }
        }

        public static void Unregister(IGrowthProvider provider)
        {
            if (object.ReferenceEquals(_current, provider))
            {
                _current = null;
            }
        }

        public static void Clear()
        {
            _current = null;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }
    }
}
