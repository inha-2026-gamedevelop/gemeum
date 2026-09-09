// Unity
using UnityEngine;

// Project
using Geumeum.Combat;

namespace Geumeum.Art
{
    /// <summary>Cosmetic receiver for retained third-party character clip events.</summary>
    [DisallowMultipleComponent]
    public sealed class LegacyPlayerAnimationEventReceiver : MonoBehaviour
    {
        // S_Idle contains this legacy event. Weapon authority never depends on it;
        // Refresh is idempotent and only repairs the current visual hand attachment.
        public void SwitchSocket()
        {
            PlayerWeaponVisual visual = GetComponentInParent<PlayerWeaponVisual>();
            if (visual != null)
            {
                visual.Refresh();
            }
        }
    }
}
