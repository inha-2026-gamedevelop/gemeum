// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>Client-lifetime source used by arena presentation without scene polling.</summary>
    public static class BossPhaseSourceLocator
    {
        private static IBossPhaseSource _current;

        public static IBossPhaseSource Current => _current;
        public static event Action<IBossPhaseSource> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _current = null;
            Changed = null;
        }

        public static void Register(IBossPhaseSource source)
        {
            if ((source == null) || ReferenceEquals(_current, source))
            {
                return;
            }

            _current = source;
            ObserverDispatch.Invoke(Changed, _current, null,
                nameof(BossPhaseSourceLocator), nameof(Changed));
        }

        public static void Unregister(IBossPhaseSource source)
        {
            if ((source == null) || (!ReferenceEquals(_current, source)))
            {
                return;
            }

            _current = null;
            ObserverDispatch.Invoke(Changed, null, null,
                nameof(BossPhaseSourceLocator), nameof(Changed));
        }
    }
}
