using NUnit.Framework;
using Geumeum.Common;
using Geumeum.Network;

/// <summary> 이탈 상태 보관소의 경계 규칙 </summary>
public class RunParticipantRegistryTests
{
    private static RunParticipantSnapshot MakeSnapshot(uint runIdValue, ulong participantValue, float health)
    {
        return new RunParticipantSnapshot
        {
            RunId       = new RunId(runIdValue),
            Participant = new ParticipantId(participantValue),
            Health      = health,
        };
    }

    [SetUp]
    public void Clear()
    {
        RunParticipantRegistry.ServerClear();
    }

    /// <summary> 맡긴 값을 그대로 돌려준다 </summary>
    [Test]
    public void StoredSnapshotComesBack()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));

        bool taken = RunParticipantRegistry.TryTake(new RunId(1u), new ParticipantId(100UL), out RunParticipantSnapshot snapshot);

        Assert.IsTrue(taken);
        Assert.AreEqual(17f, snapshot.Health);
    }

    /// <summary> 한 번 꺼내면 사라진다 </summary>
    [Test]
    public void TakingConsumesTheSnapshot()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));

        RunParticipantRegistry.TryTake(new RunId(1u), new ParticipantId(100UL), out RunParticipantSnapshot _);
        bool second = RunParticipantRegistry.TryTake(new RunId(1u), new ParticipantId(100UL), out RunParticipantSnapshot _);

        Assert.IsFalse(second);
        Assert.AreEqual(0, RunParticipantRegistry.Count);
    }

    /// <summary> 이전 판의 값은 새 판에 부어 주지 않는다 </summary>
    [Test]
    public void SnapshotFromPreviousRunIsRejected()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));

        bool taken = RunParticipantRegistry.TryTake(new RunId(2u), new ParticipantId(100UL), out RunParticipantSnapshot _);

        Assert.IsFalse(taken);
        Assert.AreEqual(0, RunParticipantRegistry.Count);
    }

    /// <summary> 남의 신원으로는 가져갈 수 없다 </summary>
    [Test]
    public void OtherParticipantCannotTake()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));

        bool taken = RunParticipantRegistry.TryTake(new RunId(1u), new ParticipantId(200UL), out RunParticipantSnapshot _);

        Assert.IsFalse(taken);
        Assert.IsTrue(RunParticipantRegistry.Has(new ParticipantId(100UL)));
    }

    /// <summary> 신원이 없으면 맡을 수 없다 </summary>
    [Test]
    public void SnapshotWithoutIdentityIsNotStored()
    {
        bool stored = RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 0UL, 17f));

        Assert.IsFalse(stored);
        Assert.AreEqual(0, RunParticipantRegistry.Count);
    }

    /// <summary> 같은 사람이 두 번 맡기면 나중 값이 남는다 </summary>
    [Test]
    public void LaterStoreOverwritesEarlier()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 3f));

        RunParticipantRegistry.TryTake(new RunId(1u), new ParticipantId(100UL), out RunParticipantSnapshot snapshot);

        Assert.AreEqual(3f, snapshot.Health);
    }

    /// <summary> 런이 끝나면 전부 버린다 </summary>
    [Test]
    public void ClearDropsEverything()
    {
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 100UL, 17f));
        RunParticipantRegistry.ServerStore(MakeSnapshot(1u, 200UL, 42f));

        RunParticipantRegistry.ServerClear();

        Assert.AreEqual(0, RunParticipantRegistry.Count);
    }
}
