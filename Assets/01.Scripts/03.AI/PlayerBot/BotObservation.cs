using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary> 대상 최초 관측부터 반응 시간을 잰다. 놓친 대상은 제한된 간격으로 다시 관측한다 </summary>
    public sealed class BotObservation
    {
        private readonly BotSkillProfileSO _profile;
        private readonly System.Random _random;
        public uint Target { get; private set; }
        public double ObservedAt { get; private set; }
        public double ReadyAt { get; private set; }
        public bool Missed { get; private set; }

        public BotObservation(BotSkillProfileSO profile, System.Random random)
        { _profile = profile; _random = random; }

        public void Observe(uint target, double now)
        {
            if (target == 0u) { Clear(); return; }
            if ((target == Target) && ((!Missed) ||
                (now < ReadyAt + System.Math.Max(0.1f, _profile.ObservationRetrySeconds)))) { return; }
            Target = target;
            ObservedAt = now;
            ReadyAt = now + _profile.SampleReaction(_random);
            Missed = _random.NextDouble() < _profile.MissChance;
        }

        public void Clear() { Target = 0u; ObservedAt = 0d; ReadyAt = 0d; Missed = false; }
    }
}
