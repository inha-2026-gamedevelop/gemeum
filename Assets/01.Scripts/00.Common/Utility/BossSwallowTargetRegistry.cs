// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// Server-lifetime index of player life owners that can enter Swallowed.
    /// Boss AI reads this stable list in its hot tick and never scans spawned
    /// identities or resolves components there.
    /// </summary>
    public static class BossSwallowTargetRegistry
    {
        private static readonly List<IBossSwallowTarget> G_TARGETS =
            new List<IBossSwallowTarget>(Constants.Network.MAX_PLAYERS);

        public static IReadOnlyList<IBossSwallowTarget> Targets => G_TARGETS;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            G_TARGETS.Clear();
        }

        public static void Register(IBossSwallowTarget target)
        {
            if ((target != null) && (!G_TARGETS.Contains(target)))
            {
                G_TARGETS.Add(target);
            }
        }

        public static void Unregister(IBossSwallowTarget target)
        {
            if (target != null)
            {
                G_TARGETS.Remove(target);
            }
        }
    }
}
