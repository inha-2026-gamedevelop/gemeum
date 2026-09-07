using NUnit.Framework;

using Geumeum.Common;

/// <summary> 표식 슬롯 규칙. 2슬롯, 8초 만료, 소유자별 독립 </summary>
public class MarkRegistryTests
{
    private const double DURATION   = 8d;
    private const int    SLOT_COUNT = 2;

    private static readonly ParticipantId MUNYEO_A = new ParticipantId(100UL);
    private static readonly ParticipantId MUNYEO_B = new ParticipantId(200UL);

    private static TargetRef Target(uint netId)
    {
        return TargetRef.Create(default, default, netId, TargetRef.BODY_PART_ID);
    }

    private static bool Apply(ParticipantId owner, uint netId, double now)
    {
        return MarkRegistry.TryApply(owner, Target(netId), now, DURATION, SLOT_COUNT, out MarkRejection _);
    }

    [SetUp]
    public void Reset()
    {
        MarkRegistry.ClearAll();
    }

    /// <summary> 두 대상까지는 그대로 쌓인다 </summary>
    [Test]
    public void TwoSlotsHoldTwoTargets()
    {
        Assert.IsTrue(Apply(MUNYEO_A, 1u, 0d));
        Assert.IsTrue(Apply(MUNYEO_A, 2u, 1d));

        Assert.AreEqual(2, MarkRegistry.CountActive(MUNYEO_A, 2d));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), 2d));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(2u), 2d));
    }

    /// <summary> 세 번째 대상은 가장 오래된 슬롯을 밀어낸다 </summary>
    [Test]
    public void ThirdTargetReplacesOldest()
    {
        Apply(MUNYEO_A, 1u, 0d);
        Apply(MUNYEO_A, 2u, 1d);
        Assert.IsTrue(Apply(MUNYEO_A, 3u, 2d));

        Assert.IsFalse(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), 3d), "가장 오래된 표식이 남아 있다");
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(2u), 3d));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(3u), 3d));
        Assert.AreEqual(2, MarkRegistry.CountActive(MUNYEO_A, 3d), "슬롯이 2를 넘었다");
    }

    /// <summary> 지속 시간이 지나면 스스로 풀린다 </summary>
    [Test]
    public void MarkExpiresAfterDuration()
    {
        Apply(MUNYEO_A, 1u, 0d);

        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), DURATION - 0.001d), "만료 직전인데 풀렸다");
        Assert.IsFalse(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), DURATION), "만료 시각에 살아 있다");
        Assert.AreEqual(0, MarkRegistry.CountActive(MUNYEO_A, DURATION));
    }

    /// <summary> 같은 대상 재부여는 거부한다. 매 타격마다 갱신되면 부여 시각 조건이 무너진다 </summary>
    [Test]
    public void ReapplyOnSameTargetIsRejected()
    {
        Apply(MUNYEO_A, 1u, 0d);

        bool ok = MarkRegistry.TryApply(
            MUNYEO_A, Target(1u), 3d, DURATION, SLOT_COUNT, out MarkRejection rejection);

        Assert.IsFalse(ok);
        Assert.AreEqual(MarkRejection.AlreadyMarked, rejection);

        // 처음 부여 시각이 유지되어야 한다. 3초에 갱신됐다면 11초까지 살아 있을 것이다
        Assert.IsFalse(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), DURATION + 0.5d), "부여 시각이 갱신됐다");
    }

    /// <summary> 만료된 뒤에는 같은 대상에 다시 걸 수 있다 </summary>
    [Test]
    public void ReapplyAfterExpiryIsAllowed()
    {
        Apply(MUNYEO_A, 1u, 0d);
        Assert.IsTrue(Apply(MUNYEO_A, 1u, DURATION));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), DURATION + 1d));
    }

    /// <summary> 무녀가 둘이면 같은 대상에 각자 슬롯을 가진다 </summary>
    [Test]
    public void TwoOwnersMarkSameTargetIndependently()
    {
        Assert.IsTrue(Apply(MUNYEO_A, 1u, 0d));
        Assert.IsTrue(Apply(MUNYEO_B, 1u, 0d), "남이 걸었다고 거부되면 안 된다");

        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), 1d));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_B, Target(1u), 1d));
        Assert.AreEqual(1, MarkRegistry.CountActive(MUNYEO_A, 1d));
        Assert.AreEqual(1, MarkRegistry.CountActive(MUNYEO_B, 1d));
    }

    /// <summary> 한 사람의 슬롯이 다른 사람의 슬롯을 밀어내지 않는다 </summary>
    [Test]
    public void OneOwnerOverflowDoesNotTouchAnother()
    {
        Apply(MUNYEO_B, 9u, 0d);

        Apply(MUNYEO_A, 1u, 0d);
        Apply(MUNYEO_A, 2u, 1d);
        Apply(MUNYEO_A, 3u, 2d);

        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_B, Target(9u), 3d), "남의 표식이 밀려났다");
    }

    /// <summary> 아무나 걸어 두었는지 묻는 조회 </summary>
    [Test]
    public void IsMarkedSeesAnyOwner()
    {
        Apply(MUNYEO_B, 5u, 0d);

        Assert.IsTrue(MarkRegistry.IsMarked(Target(5u), 1d));
        Assert.IsFalse(MarkRegistry.IsMarked(Target(6u), 1d));
        Assert.IsFalse(MarkRegistry.IsMarked(Target(5u), DURATION), "만료된 표식이 조회된다");
    }

    /// <summary> 이탈한 사람의 표식만 거둔다 </summary>
    [Test]
    public void ClearOwnerRemovesOnlyThatOwner()
    {
        Apply(MUNYEO_A, 1u, 0d);
        Apply(MUNYEO_B, 2u, 0d);

        MarkRegistry.ClearOwner(MUNYEO_A);

        Assert.AreEqual(0, MarkRegistry.CountActive(MUNYEO_A, 1d));
        Assert.AreEqual(1, MarkRegistry.CountActive(MUNYEO_B, 1d));
    }

    /// <summary> 씬을 넘기면 netId가 의미를 잃으므로 전부 지운다 </summary>
    [Test]
    public void ClearAllRemovesEveryOwner()
    {
        Apply(MUNYEO_A, 1u, 0d);
        Apply(MUNYEO_B, 2u, 0d);

        MarkRegistry.ClearAll();

        Assert.AreEqual(0, MarkRegistry.CountActive(MUNYEO_A, 1d));
        Assert.AreEqual(0, MarkRegistry.CountActive(MUNYEO_B, 1d));
        Assert.IsFalse(MarkRegistry.IsMarked(Target(1u), 1d));
    }

    /// <summary> 잘못된 입력은 거부 사유를 남긴다 </summary>
    [Test]
    public void InvalidInputsAreRejectedWithReason()
    {
        MarkRegistry.TryApply(ParticipantId.None, Target(1u), 0d, DURATION, SLOT_COUNT, out MarkRejection noOwner);
        Assert.AreEqual(MarkRejection.InvalidOwner, noOwner);

        MarkRegistry.TryApply(MUNYEO_A, Target(0u), 0d, DURATION, SLOT_COUNT, out MarkRejection noTarget);
        Assert.AreEqual(MarkRejection.InvalidTarget, noTarget);

        MarkRegistry.TryApply(MUNYEO_A, Target(1u), 0d, DURATION, 0, out MarkRejection noSlots);
        Assert.AreEqual(MarkRejection.InvalidSlots, noSlots);
    }

    /// <summary> 슬롯 수를 늘려도 유지 중인 표식이 사라지지 않는다 </summary>
    [Test]
    public void GrowingSlotCountKeepsExistingMarks()
    {
        Apply(MUNYEO_A, 1u, 0d);
        Apply(MUNYEO_A, 2u, 0d);

        MarkRegistry.TryApply(MUNYEO_A, Target(3u), 1d, DURATION, 3, out MarkRejection _);

        Assert.AreEqual(3, MarkRegistry.CountActive(MUNYEO_A, 2d));
        Assert.IsTrue(MarkRegistry.IsMarkedBy(MUNYEO_A, Target(1u), 2d));
    }
}
