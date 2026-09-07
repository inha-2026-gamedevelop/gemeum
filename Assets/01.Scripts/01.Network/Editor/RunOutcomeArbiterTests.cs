using NUnit.Framework;
using Geumeum.Common;
using Geumeum.Network;

/// <summary> 같은 틱에 종료 사유가 겹쳤을 때의 선택 규칙 </summary>
public class RunOutcomeArbiterTests
{
    /// <summary> 보스를 죽인 순간 마지막 생존자가 함께 죽어도 승리다 </summary>
    [Test]
    public void VictoryBeatsPartyWipe()
    {
        Assert.AreEqual(RunOutcome.Victory, RunOutcomeArbiter.Select(RunOutcome.PartyWipe, RunOutcome.Victory));
        Assert.AreEqual(RunOutcome.Victory, RunOutcomeArbiter.Select(RunOutcome.Victory, RunOutcome.PartyWipe));
    }

    /// <summary> 전멸과 기한 만료가 겹치면 전멸이다 </summary>
    [Test]
    public void PartyWipeBeatsTimeExpired()
    {
        Assert.AreEqual(RunOutcome.PartyWipe, RunOutcomeArbiter.Select(RunOutcome.TimeExpired, RunOutcome.PartyWipe));
        Assert.AreEqual(RunOutcome.PartyWipe, RunOutcomeArbiter.Select(RunOutcome.PartyWipe, RunOutcome.TimeExpired));
    }

    /// <summary> 호스트가 대기를 끊는 이탈은 이미 이룬 승리를 뒤집지 못한다 </summary>
    [Test]
    public void PlayerLeftNeverOverturnsOthers()
    {
        Assert.AreEqual(RunOutcome.Victory,     RunOutcomeArbiter.Select(RunOutcome.Victory, RunOutcome.PlayerLeft));
        Assert.AreEqual(RunOutcome.PartyWipe,   RunOutcomeArbiter.Select(RunOutcome.PartyWipe, RunOutcome.PlayerLeft));
        Assert.AreEqual(RunOutcome.TimeExpired, RunOutcomeArbiter.Select(RunOutcome.TimeExpired, RunOutcome.PlayerLeft));
    }

    /// <summary> 후보가 없으면 무엇이든 채택된다 </summary>
    [Test]
    public void AnyOutcomeReplacesNone()
    {
        Assert.IsTrue(RunOutcomeArbiter.ShouldReplace(RunOutcome.None, RunOutcome.PlayerLeft));
        Assert.AreEqual(RunOutcome.PlayerLeft, RunOutcomeArbiter.Select(RunOutcome.None, RunOutcome.PlayerLeft));
    }

    /// <summary> 같은 사유가 두 번 와도 먼저 온 것을 남긴다 </summary>
    [Test]
    public void SameOutcomeDoesNotReplace()
    {
        Assert.IsFalse(RunOutcomeArbiter.ShouldReplace(RunOutcome.PartyWipe, RunOutcome.PartyWipe));
    }

    /// <summary> None은 후보가 될 수 없다 </summary>
    [Test]
    public void NoneIsNeverACandidate()
    {
        Assert.IsFalse(RunOutcomeArbiter.ShouldReplace(RunOutcome.None, RunOutcome.None));
        Assert.AreEqual(RunOutcome.TimeExpired, RunOutcomeArbiter.Select(RunOutcome.TimeExpired, RunOutcome.None));
    }

    /// <summary> 도착 순서를 뒤섞어도 결과는 하나다 </summary>
    [Test]
    public void ResultIsIndependentOfArrivalOrder()
    {
        RunOutcome[] arrival = { RunOutcome.TimeExpired, RunOutcome.Victory, RunOutcome.PartyWipe };
        RunOutcome[] reverse = { RunOutcome.PartyWipe, RunOutcome.Victory, RunOutcome.TimeExpired };

        RunOutcome first  = RunOutcome.None;
        RunOutcome second = RunOutcome.None;

        for (int i = 0; i < arrival.Length; ++i)
        {
            first  = RunOutcomeArbiter.Select(first, arrival[i]);
            second = RunOutcomeArbiter.Select(second, reverse[i]);
        }

        Assert.AreEqual(RunOutcome.Victory, first);
        Assert.AreEqual(first, second);
    }
}
