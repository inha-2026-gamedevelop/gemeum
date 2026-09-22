// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>Resolves a serialized fixed PartId without depending on child array order</summary>
    public static class AimAssistTargetResolver
    {
        public static bool TryResolve(Transform identityRoot, byte partId,
            out IAimAssistTarget target)
        {
            target = null;
            if (identityRoot == null)
            {
                return false;
            }

            IAimAssistTarget[] candidates =
                identityRoot.GetComponentsInChildren<IAimAssistTarget>(true);
            for (int i = 0; i < candidates.Length; ++i)
            {
                IAimAssistTarget candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                ITargetPartIdSource part = candidate as ITargetPartIdSource;
                if ((part == null) && (candidate is Component candidateComponent))
                {
                    // A legacy AimAssistPoint can share a GameObject with a stable boss hitbox
                    // In that case the hitbox identity wins over the child-array/body fallback
                    part = candidateComponent.GetComponent<ITargetPartIdSource>();
                }

                if (part != null)
                {
                    if ((part.TargetPartId == partId) && part.IsTargetable)
                    {
                        target = part as IAimAssistTarget ?? candidate;
                        return true;
                    }

                    continue;
                }

                if (partId == TargetRef.BODY_PART_ID)
                {
                    target = candidate;
                    return true;
                }
            }

            return false;
        }

        public static byte ResolvePartId(IAimAssistTarget target)
        {
            if (target is ITargetPartIdSource part)
            {
                return part.TargetPartId;
            }

            if ((target is Component component) &&
                (component.GetComponent<ITargetPartIdSource>() is ITargetPartIdSource siblingPart))
            {
                return siblingPart.TargetPartId;
            }

            return TargetRef.BODY_PART_ID;
        }
    }
}
