using NUnit.Framework;
using Geumeum.Common;

/// <summary>
/// 공용 런 계약의 경계 동작. getter를 다시 적는 테스트가 아니라
/// 틀리면 3인 접속에서만 증상이 보이는 규칙만 고정한다
/// </summary>
public class RunContractTests
{
    /// <summary> 멈춘 시계는 시간이 흘러도 값이 늘지 않는다 </summary>
    [Test]
    public void StoppedClockKeepsElapsed()
    {
        RunClockState clock = RunClockState.Zero.Started(100.0);

        clock = clock.Stopped(130.0);

        Assert.AreEqual(30.0, clock.ElapsedAt(500.0), 0.0001);
    }

    /// <summary> 멈췄다 다시 돌리면 멈춰 있던 시간은 경과에 들어가지 않는다 </summary>
    [Test]
    public void ResumedClockExcludesPausedSpan()
    {
        RunClockState clock = RunClockState.Zero.Started(0.0);

        clock = clock.Stopped(10.0);   // 10초 진행
        clock = clock.Started(70.0);   // 60초 멈춰 있었다

        Assert.AreEqual(15.0, clock.ElapsedAt(75.0), 0.0001);
    }

    /// <summary> 이미 돌고 있는 시계를 다시 돌려도 기준 시각을 다시 잡지 않는다 </summary>
    [Test]
    public void StartingRunningClockDoesNotResetBaseline()
    {
        RunClockState clock = RunClockState.Zero.Started(0.0);

        clock = clock.Started(50.0);

        Assert.AreEqual(50.0, clock.ElapsedAt(50.0), 0.0001);
    }

    /// <summary> 정지 사유가 둘일 때 하나만 풀면 여전히 멈춰 있다 </summary>
    [Test]
    public void ClearingOnePauseReasonKeepsRunPaused()
    {
        RunPauseReason reasons = (RunPauseReason.ParticipantMissing | RunPauseReason.SceneTransition);

        reasons &= ~RunPauseReason.ParticipantMissing;

        Assert.AreNotEqual(RunPauseReason.None, reasons);
        Assert.AreEqual(RunPauseReason.SceneTransition, reasons);
    }

    /// <summary> 씬 세대가 다른 대상 참조는 지금 씬의 것이 아니다 </summary>
    [Test]
    public void TargetRefRejectsOtherSceneRevision()
    {
        RunId         run     = new RunId(7u);
        SceneRevision current = new SceneRevision(3u);
        TargetRef     stale   = TargetRef.Create(run, new SceneRevision(2u), 42u, TargetRef.BODY_PART_ID);

        Assert.IsFalse(stale.IsCurrent(run, current));
    }

    /// <summary> 트라이 모드는 승리해도 리더보드 대상이 아니다 </summary>
    [Test]
    public void BossTrialVictoryIsNotLeaderboardEligible()
    {
        RunResultSnapshot trial = new RunResultSnapshot
        {
            Mode    = RunMode.BossTrial,
            Outcome = RunOutcome.Victory,
        };

        Assert.IsFalse(trial.IsLeaderboardEligible);
    }

    /// <summary> 다른 런의 그릇은 되부을 수 없다 </summary>
    [Test]
    public void SnapshotFromOtherRunDoesNotMatch()
    {
        ParticipantId participant = new ParticipantId(1234UL);
        RunParticipantSnapshot snapshot = new RunParticipantSnapshot
        {
            RunId       = new RunId(1u),
            Participant = participant,
        };

        Assert.IsFalse(snapshot.Matches(new RunId(2u), participant));
        Assert.IsTrue(snapshot.Matches(new RunId(1u), participant));
    }
}
