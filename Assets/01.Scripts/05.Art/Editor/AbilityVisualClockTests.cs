using NUnit.Framework;

namespace Geumeum.Art.Tests
{
    public sealed class AbilityVisualClockTests
    {
        [Test]
        public void AdvancesBetweenSnapshotsButDoesNotExtrapolateWithoutBound()
        {
            var clock = new AbilityVisualClock();
            Assert.AreEqual(10, clock.Sample(10, 0, false, false));
            Assert.That(clock.Sample(10, .2, false, false), Is.EqualTo(10.2).Within(.001));
            Assert.That(clock.Sample(10, 5, false, false), Is.EqualTo(10.5).Within(.001));
        }

        [Test]
        public void PauseCorrectsOvershootAndResumeDoesNotIncludePausedWallTime()
        {
            var clock = new AbilityVisualClock();
            clock.Sample(10, 0, false, false); clock.Sample(10, .4, false, false);
            Assert.AreEqual(10.25, clock.Sample(10.25, .5, true, false));
            Assert.AreEqual(10.25, clock.Sample(10.25, 8, true, false));
            Assert.AreEqual(10.25, clock.Sample(10.25, 9, false, false));
            Assert.That(clock.Sample(10.25, 9.1, false, false), Is.EqualTo(10.35).Within(.001));
        }

        [Test]
        public void FreshCueCatchesUpDelayedClockAndHostUsesConfirmedTime()
        {
            var clock = new AbilityVisualClock();
            clock.Sample(10, 0, false, false); clock.ObserveCue(10.3);
            Assert.That(clock.Sample(10, .05, false, false), Is.EqualTo(10.35).Within(.001));
            Assert.AreEqual(10.1, clock.Sample(10.1, .2, false, true));
            Assert.AreEqual(0, clock.Sample(0, .3, false, false));
        }
    }
}
