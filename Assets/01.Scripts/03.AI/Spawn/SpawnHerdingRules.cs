// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>축소 뒤 스폰 유예와 몰이 중 임시 동시 상한을 계산하는 순수 규칙.</summary>
    public static class SpawnHerdingRules
    {
        public static double GetFirstSpawnGameplayTime(RunPhase phase,
            double currentGameplayTime, float graceSeconds)
        {
            if (double.IsNaN(currentGameplayTime) ||
                double.IsInfinity(currentGameplayTime) ||
                float.IsNaN(graceSeconds) || float.IsInfinity(graceSeconds))
            {
                return double.PositiveInfinity;
            }

            bool needsGrace = (phase == RunPhase.Shrink1) ||
                (phase == RunPhase.Shrink2);
            return needsGrace
                ? currentGameplayTime + Math.Max(graceSeconds, 0f)
                : currentGameplayTime;
        }

        public static int GetDesiredConcurrent(int targetConcurrent,
            int configuredMaximum, float capMultiplier, int herdingCount)
        {
            int target = Mathf.Max(targetConcurrent, 0);
            if ((herdingCount <= 0) || float.IsNaN(capMultiplier) ||
                float.IsInfinity(capMultiplier))
            {
                return target;
            }

            int temporaryCap = Mathf.CeilToInt(
                Mathf.Max(configuredMaximum, 0) * Mathf.Max(capMultiplier, 1f));
            return Mathf.Max(target, temporaryCap);
        }
    }
}
