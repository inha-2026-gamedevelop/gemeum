// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

/// <summary> 동료 화면 잡귀 움찔 전송의 순수 규칙(COMBAT_STYLE_DESIGN 8.1, S2) </summary>
public class ObservedHitTests
{
    private const uint  TARGET       = 42u;
    private const uint  OTHER_TARGET = 43u;
    private const float INTERVAL     = 0.1f;

    // yaw 한 칸의 반(360 / 256 / 2도)보다 조금 넉넉하게 본다
    private const float YAW_TOLERANCE_DEGREES = 0.75f;

    /****************************************
    *              Throttle
    ****************************************/

    /// <summary> 같은 대상은 간격 안에 한 번만 보낸다. 다른 대상은 서로 막지 않는다 </summary>
    [Test]
    public void ThrottleAllowsOncePerIntervalPerTarget()
    {
        ObservedHitThrottle throttle = new ObservedHitThrottle();

        Assert.IsTrue(throttle.TryAccept(TARGET, 10d, INTERVAL));
        Assert.IsFalse(throttle.TryAccept(TARGET, 10.05d, INTERVAL));
        Assert.IsTrue(throttle.TryAccept(OTHER_TARGET, 10.05d, INTERVAL));

        // 막힌 요청은 간격을 늘리지 않는다. 첫 전송부터 잰다
        Assert.IsFalse(throttle.TryAccept(TARGET, 10.09d, INTERVAL));
        Assert.IsTrue(throttle.TryAccept(TARGET, 10.15d, INTERVAL));
    }

    /// <summary> 대상이 사라지면 기록을 지우고, 시계가 되돌아가면(새 세션) 막지 않는다 </summary>
    [Test]
    public void ThrottleForgetsTargetAndToleratesClockReset()
    {
        ObservedHitThrottle throttle = new ObservedHitThrottle();

        Assert.IsTrue(throttle.TryAccept(TARGET, 10d, INTERVAL));
        throttle.Forget(TARGET);
        Assert.AreEqual(0, throttle.TrackedCount);
        Assert.IsTrue(throttle.TryAccept(TARGET, 10.01d, INTERVAL));

        Assert.IsTrue(throttle.TryAccept(TARGET, 1d, INTERVAL));

        Assert.IsFalse(throttle.TryAccept(OTHER_TARGET, double.NaN, INTERVAL));

        throttle.Clear();
        Assert.AreEqual(0, throttle.TrackedCount);
    }

    /// <summary> 간격이 0이면 매번 보낸다 </summary>
    [Test]
    public void ZeroIntervalNeverThrottles()
    {
        ObservedHitThrottle throttle = new ObservedHitThrottle();

        Assert.IsTrue(throttle.TryAccept(TARGET, 10d, 0f));
        Assert.IsTrue(throttle.TryAccept(TARGET, 10d, 0f));
    }

    /****************************************
    *            Yaw Quantize
    ****************************************/

    /// <summary> 수평 방향을 한 바이트로 줄였다 되돌리면 한 칸 반 이내로 돌아온다 </summary>
    [Test]
    public void YawRoundTripStaysWithinHalfStep()
    {
        for (int degrees = 0; degrees < 360; degrees += 7)
        {
            float   radians   = degrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

            Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(direction * 3f, out byte yaw));
            Vector3 decoded = LocalObservedHitFeed.DecodeYaw(yaw);

            Assert.AreEqual(0f, decoded.y, 0.0001f);
            Assert.AreEqual(1f, decoded.magnitude, 0.0001f);
            Assert.That(Vector3.Angle(direction, decoded), Is.LessThanOrEqualTo(YAW_TOLERANCE_DEGREES),
                $"{degrees}도");
        }
    }

    /// <summary> 축 방향은 정확한 칸에 떨어지고, 한 바퀴 끝은 0으로 접힌다 </summary>
    [Test]
    public void YawAxesMapToExactSteps()
    {
        Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(Vector3.forward, out byte forward));
        Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(Vector3.right, out byte right));
        Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(Vector3.back, out byte back));
        Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(Vector3.left, out byte left));

        Assert.AreEqual((byte)0, forward);
        Assert.AreEqual((byte)64, right);
        Assert.AreEqual((byte)128, back);
        Assert.AreEqual((byte)192, left);

        // 359.9도는 반올림하면 256칸이 되므로 0으로 접는다
        float almostTurn = 359.9f * Mathf.Deg2Rad;
        Assert.IsTrue(LocalObservedHitFeed.TryEncodeYaw(new Vector3(Mathf.Sin(almostTurn), 0f, Mathf.Cos(almostTurn)),
            out byte wrapped));
        Assert.AreEqual((byte)0, wrapped);
    }

    /// <summary> 방향이 없거나(수직, 0) 비정상이면 보내지 않는다 </summary>
    [Test]
    public void YawRejectsMissingDirection()
    {
        Assert.IsFalse(LocalObservedHitFeed.TryEncodeYaw(Vector3.zero, out _));
        Assert.IsFalse(LocalObservedHitFeed.TryEncodeYaw(Vector3.up, out _));
        Assert.IsFalse(LocalObservedHitFeed.TryEncodeYaw(new Vector3(float.NaN, 0f, 1f), out _));
    }

    /****************************************
    *          Observer Filter
    ****************************************/

    /// <summary> 공격한 본인은 결과 경로로 이미 움찔하므로 관찰하지 않는다. 내 캐릭터가 없으면 모두 관찰한다 </summary>
    [Test]
    public void LocalAttackerIsNotObserved()
    {
        Assert.IsFalse(LocalObservedHitFeed.ShouldObserve(7u, 7u));
        Assert.IsTrue(LocalObservedHitFeed.ShouldObserve(8u, 7u));
        Assert.IsTrue(LocalObservedHitFeed.ShouldObserve(8u, 0u));
    }

    /// <summary> 피드는 구독자 모두에게 한 번씩 흘리고, 한 구독자가 던져도 나머지는 받는다 </summary>
    [Test]
    public void FeedPublishesToEverySubscriber()
    {
        LocalObservedHitFeed.ResetStatic();
        try
        {
            int received = 0;
            LocalObservedHitFeed.ObservedHit last = default;

            LocalObservedHitFeed.Received += _ => throw new InvalidOperationException("test");
            LocalObservedHitFeed.Received += hit => { ++received; last = hit; };

            // 던진 구독자는 삼키지 않고 오류로 남는다
            LogAssert.Expect(LogType.Error, new Regex("LocalObservedHitFeed"));
            LocalObservedHitFeed.Publish(new LocalObservedHitFeed.ObservedHit(TARGET, 9u, Vector3.forward, ImpactTier.Medium));

            Assert.AreEqual(1, received);
            Assert.AreEqual(TARGET, last.TargetNetId);
            Assert.AreEqual(9u, last.AttackerNetId);
            Assert.AreEqual(ImpactTier.Medium, last.Tier);
        }
        finally
        {
            LocalObservedHitFeed.ResetStatic();
        }
    }

    /****************************************
    *                Hub
    ****************************************/

    /// <summary> 서버가 돌지 않으면 허브는 관찰자 전송을 허락하지 않는다 </summary>
    [Test]
    public void HubRejectsObservedHitWithoutActiveServer()
    {
        ServerDamageResultHub.ResetStatic();
        try
        {
            DamageInfo damage = new DamageInfo(10f, 7u, new ParticipantId(70UL), HitStrength.Light);

            Assert.IsFalse(ServerDamageResultHub.ServerTryAcceptObservedHit(damage, TARGET, Vector3.forward,
                out _, out _));
        }
        finally
        {
            ServerDamageResultHub.ResetStatic();
        }
    }
}
