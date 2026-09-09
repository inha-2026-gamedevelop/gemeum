// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Systems
{
    /// <summary> 이전 원 포함, 랜드마크 포함, 이동 가능성 조건으로 다음 결계 중심을 고르는 순수 규칙 </summary>
    public static class BarrierTargetSelector
    {
        private const float DISTANCE_EPSILON = 0.01f;

        public static bool TrySelect(
            Vector3 previousCenter,
            float previousRadius,
            float targetRadius,
            IReadOnlyList<Vector3> primaryCandidates,
            IReadOnlyList<Vector3> configuredFallbacks,
            IReadOnlyList<Vector3> requiredLandmarks,
            int fallbackSegmentSamples,
            Func<Vector3, bool> navigationValidator,
            out Vector3 selectedCenter,
            out bool usedFallback)
        {
            selectedCenter = previousCenter;
            usedFallback   = false;

            if (!IsFinite(previousCenter) || !IsFinite(previousRadius) ||
                !IsFinite(targetRadius) || (previousRadius <= 0f) ||
                (targetRadius <= 0f) || (targetRadius > previousRadius) ||
                (primaryCandidates == null) || (configuredFallbacks == null) ||
                (requiredLandmarks == null) || (navigationValidator == null) ||
                (fallbackSegmentSamples <= 0))
            {
                return false;
            }

            if (TryCandidates(primaryCandidates, previousCenter, previousRadius,
                    targetRadius, requiredLandmarks, navigationValidator,
                    out selectedCenter))
            {
                return true;
            }

            usedFallback = true;
            if (TryCandidates(configuredFallbacks, previousCenter, previousRadius,
                    targetRadius, requiredLandmarks, navigationValidator,
                    out selectedCenter))
            {
                return true;
            }

            Vector3 landmarkCenter = GetLandmarkCenter(requiredLandmarks, previousCenter);
            for (int i = 0; i <= fallbackSegmentSamples; ++i)
            {
                float   t         = (float) i / fallbackSegmentSamples;
                Vector3 candidate = Vector3.Lerp(previousCenter, landmarkCenter, t);
                if (!IsValidCandidate(candidate, previousCenter, previousRadius,
                        targetRadius, requiredLandmarks, navigationValidator))
                {
                    continue;
                }

                selectedCenter = candidate;
                return true;
            }

            return false;
        }

        public static bool IsValidCandidate(Vector3 candidate, Vector3 previousCenter,
            float previousRadius, float targetRadius,
            IReadOnlyList<Vector3> requiredLandmarks,
            Func<Vector3, bool> navigationValidator)
        {
            if (!IsFinite(candidate) || !IsFinite(previousCenter) ||
                !IsFinite(previousRadius) || !IsFinite(targetRadius) ||
                (previousRadius <= 0f) || (targetRadius <= 0f) ||
                (targetRadius > previousRadius) || (requiredLandmarks == null) ||
                (navigationValidator == null))
            {
                return false;
            }

            float centerDistance = DistanceXZ(candidate, previousCenter);
            if ((centerDistance + targetRadius) > (previousRadius + DISTANCE_EPSILON))
            {
                return false;
            }

            for (int i = 0; i < requiredLandmarks.Count; ++i)
            {
                Vector3 landmark = requiredLandmarks[i];
                if (!IsFinite(landmark) ||
                    (DistanceXZ(candidate, landmark) > (targetRadius + DISTANCE_EPSILON)))
                {
                    return false;
                }
            }

            return navigationValidator(candidate);
        }

        public static bool IsInside(Vector3 position, Vector3 center, float radius)
        {
            if (!IsFinite(position) || !IsFinite(center) || !IsFinite(radius) ||
                (radius < 0f))
            {
                return false;
            }

            return DistanceXZ(position, center) <= (radius + DISTANCE_EPSILON);
        }

        public static Vector3 GetHerdingDestination(Vector3 position, Vector3 center,
            float radius, float inset)
        {
            Vector3 fromCenter = position - center;
            fromCenter.y = 0f;

            float destinationRadius = Mathf.Max(radius - Mathf.Max(inset, 0f), 0f);
            if (fromCenter.sqrMagnitude <= (DISTANCE_EPSILON * DISTANCE_EPSILON))
            {
                return center;
            }

            return center + (fromCenter.normalized * destinationRadius);
        }

        private static bool TryCandidates(IReadOnlyList<Vector3> candidates,
            Vector3 previousCenter, float previousRadius, float targetRadius,
            IReadOnlyList<Vector3> requiredLandmarks,
            Func<Vector3, bool> navigationValidator, out Vector3 selectedCenter)
        {
            for (int i = 0; i < candidates.Count; ++i)
            {
                if (!IsValidCandidate(candidates[i], previousCenter, previousRadius,
                        targetRadius, requiredLandmarks, navigationValidator))
                {
                    continue;
                }

                selectedCenter = candidates[i];
                return true;
            }

            selectedCenter = previousCenter;
            return false;
        }

        private static Vector3 GetLandmarkCenter(IReadOnlyList<Vector3> landmarks,
            Vector3 fallback)
        {
            if (landmarks.Count == 0)
            {
                return fallback;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < landmarks.Count; ++i)
            {
                sum += landmarks[i];
            }

            return sum / landmarks.Count;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
