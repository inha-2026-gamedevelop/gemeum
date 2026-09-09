// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

public sealed class FieldDataDefinitionTests
{
    [Test]
    public void BarrierDefinitionRequiresAllProductionLandmarks()
    {
        BarrierPhaseSO data = ScriptableObject.CreateInstance<BarrierPhaseSO>();
        try
        {
            BarrierPhaseSO.PhaseEntry[] phases = CreateBarrierPhases();
            Set(data, "_phases", phases);
            Assert.IsTrue(data.ValidateDefinition(out string failure), failure);

            phases[2].RequiredLandmarks = BarrierLandmarkKind.Start;
            Set(data, "_phases", phases);
            Assert.IsFalse(data.ValidateDefinition(out failure));
            StringAssert.Contains(RunPhase.Shrink2.ToString(), failure);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void BarrierDefinitionRejectsUnboundedCandidateSettings()
    {
        BarrierPhaseSO data = ScriptableObject.CreateInstance<BarrierPhaseSO>();
        try
        {
            Set(data, "_phases", CreateBarrierPhases());
            Set(data, "_targetCandidateAttempts", 0);
            Assert.IsFalse(data.ValidateDefinition(out _));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void SpawnDefinitionRequiresGraceCapAndBoundedPathRetries()
    {
        SpawnConfigSO data = ScriptableObject.CreateInstance<SpawnConfigSO>();
        try
        {
            Set(data, "_phases", CreateSpawnPhases());
            Assert.IsTrue(data.ValidateDefinition(out string failure), failure);

            Set(data, "_herdingPathRequestsPerTick", 0);
            Assert.IsFalse(data.ValidateDefinition(out failure));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    private static BarrierPhaseSO.PhaseEntry[] CreateBarrierPhases()
    {
        RunPhase[] phases =
        {
            RunPhase.Farming,
            RunPhase.Shrink1,
            RunPhase.Shrink2,
            RunPhase.Shrink3,
        };
        var result = new BarrierPhaseSO.PhaseEntry[phases.Length];
        for (int i = 0; i < result.Length; ++i)
        {
            result[i] = new BarrierPhaseSO.PhaseEntry
            {
                Phase = phases[i],
                StartSeconds = i * 100f,
                EndSeconds = (i + 1) * 100f,
                FieldLevel = i,
                RadiusRatio = 1f - (i * 0.2f),
                DamagePerSecond = i,
                RequiredLandmarks = BarrierPhaseSO.ProductionLandmarks,
            };
        }

        return result;
    }

    private static SpawnConfigSO.PhaseEntry[] CreateSpawnPhases()
    {
        RunPhase[] phases =
        {
            RunPhase.Farming,
            RunPhase.Shrink1,
            RunPhase.Shrink2,
        };
        var result = new SpawnConfigSO.PhaseEntry[phases.Length];
        for (int i = 0; i < result.Length; ++i)
        {
            result[i] = new SpawnConfigSO.PhaseEntry
            {
                Phase = phases[i],
                ConcurrentMin = 3,
                ConcurrentMax = 5,
                RespawnInterval = (i == 2) ? 0f : 10f,
                RatioHeokkaebi = 1,
                RatioChanggwi = 1,
                RatioAekgwi = 1,
                TargetKillMin = 1,
                TargetKillMax = 3,
            };
        }

        return result;
    }

    private static void Set(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(target, value);
    }
}
