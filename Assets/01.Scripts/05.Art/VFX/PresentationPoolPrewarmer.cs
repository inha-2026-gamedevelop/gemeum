// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>Prewarms configured local presentation pools before the first combat cue</summary>
    [DisallowMultipleComponent]
    public sealed class PresentationPoolPrewarmer : MonoBehaviour
    {
        private bool _hasResolvedNetworkRole;

        private void Update()
        {
            if (_hasResolvedNetworkRole ||
                !PresentationPoolRules.TryResolvePrewarm(
                    NetworkServer.active, NetworkClient.active,
                    out bool shouldPrewarm))
            {
                return;
            }

            _hasResolvedNetworkRole = true;
            enabled = false;
            if (!shouldPrewarm)
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Presentation == null))
            {
                Debug.LogError("[PresentationPoolPrewarmer] PresentationConfigSO is unavailable", this);
                return;
            }

            LocalObjectPoolService pool = LocalObjectPoolService.Instance;
            if (pool == null)
            {
                Debug.LogError("[PresentationPoolPrewarmer] LocalObjectPoolService is unavailable", this);
                return;
            }

            PresentationConfigSO config = GameDB.Presentation;
            pool.Prewarm(config.MeleeSlashPrefab, 3, 18);
            pool.Prewarm(config.BellPulsePrefab, 3, 9);
            pool.Prewarm(config.HealCastPrefab, 3, 9);
            pool.Prewarm(config.ParryPrefab, 3, 9);
            pool.Prewarm(config.MarkPrefab, 6, 18);
        }
    }

    public static class PresentationPoolRules
    {
        public static bool TryResolvePrewarm(bool serverActive,
            bool clientActive, out bool shouldPrewarm)
        {
            shouldPrewarm = clientActive;
            return serverActive || clientActive;
        }
    }
}
