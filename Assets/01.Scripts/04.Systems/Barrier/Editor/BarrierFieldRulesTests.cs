// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Systems;

public sealed class BarrierFieldRulesTests
{
    [Test]
    public void WarningSnapshotKeepsCurrentCircleUntilGameplayStart()
    {
        var state = new BarrierTransitionState
        {
            FromCenter = new Vector3(1f, 0f, 2f),
            TargetCenter = new Vector3(5f, 0f, 6f),
            FromRadius = 20f,
            TargetRadius = 10f,
            WarningGameplayTime = 90d,
            StartGameplayTime = 100d,
            EndGameplayTime = 130d,
            Revision = 4u,
        };

        Assert.AreEqual(state.FromCenter, BarrierJudge.GetCenter(state, 99d));
        Assert.AreEqual(state.FromRadius, BarrierJudge.GetRadius(state, 99d));
        Assert.AreEqual(new Vector3(3f, 0f, 4f), BarrierJudge.GetCenter(state, 115d));
        Assert.AreEqual(15f, BarrierJudge.GetRadius(state, 115d));
        Assert.AreEqual(state.TargetCenter, BarrierJudge.GetCenter(state, 130d));
        Assert.AreNotEqual(state.FromCenter, state.TargetCenter);
    }

    [Test]
    public void UnchangedGameplayClockDoesNotAdvanceTransition()
    {
        var state = new BarrierTransitionState
        {
            FromCenter = Vector3.zero,
            TargetCenter = Vector3.right * 8f,
            FromRadius = 20f,
            TargetRadius = 12f,
            StartGameplayTime = 10d,
            EndGameplayTime = 20d,
        };

        Vector3 beforePause = BarrierJudge.GetCenter(state, 14d);
        float radiusBeforePause = BarrierJudge.GetRadius(state, 14d);

        Assert.AreEqual(beforePause, BarrierJudge.GetCenter(state, 14d));
        Assert.AreEqual(radiusBeforePause, BarrierJudge.GetRadius(state, 14d));
    }

    [Test]
    public void SelectorRejectsOutsidePrimaryAndUsesValidatedFallback()
    {
        var primary = new List<Vector3> { new Vector3(6f, 0f, 0f) };
        var fallbacks = new List<Vector3> { new Vector3(4f, 0f, 0f) };
        var landmarks = new List<Vector3> { new Vector3(2f, 0f, 0f) };

        bool selected = BarrierTargetSelector.TrySelect(
            Vector3.zero, 10f, 6f, primary, fallbacks, landmarks,
            8, _ => true, out Vector3 center, out bool usedFallback);

        Assert.IsTrue(selected);
        Assert.IsTrue(usedFallback);
        Assert.AreEqual(fallbacks[0], center);
    }

    [Test]
    public void SelectorFailsWhenLandmarksCannotFitInTargetCircle()
    {
        var candidates = new List<Vector3> { Vector3.zero };
        var landmarks = new List<Vector3>
        {
            new Vector3(-8f, 0f, 0f),
            new Vector3(8f, 0f, 0f),
        };

        bool selected = BarrierTargetSelector.TrySelect(
            Vector3.zero, 20f, 6f, candidates, candidates, landmarks,
            8, _ => true, out _, out _);

        Assert.IsFalse(selected);
    }

    [Test]
    public void SelectorRequiresCompletePreviousCircleContainmentAndNavigation()
    {
        Assert.IsFalse(BarrierTargetSelector.IsValidCandidate(
            new Vector3(6f, 0f, 0f), Vector3.zero, 10f, 5f,
            new List<Vector3>(), _ => true));
        Assert.IsFalse(BarrierTargetSelector.IsValidCandidate(
            Vector3.zero, Vector3.zero, 10f, 5f,
            new List<Vector3>(), _ => false));
    }
}
