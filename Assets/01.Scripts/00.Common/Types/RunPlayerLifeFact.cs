namespace Geumeum.Common
{
    /// <summary> 결과 통계에서 구분하는 생명 전이 </summary>
    public enum RunPlayerLifeFactKind
    {
        Death            = 0,
        Rescue           = 1,
        SwallowedRescue  = 2,
    }

    /// <summary> 사망 세대로 중복을 막는 서버 생명 사실 </summary>
    public readonly struct RunPlayerLifeFact
    {
        public RunId                Run             { get; }
        public RunPlayerLifeFactKind Kind           { get; }
        public ParticipantId         Actor          { get; }
        public ParticipantId         Target         { get; }
        public uint                  DeathGeneration { get; }

        public bool IsValid
        {
            get
            {
                if ((!Run.IsValid) || (!Target.IsValid) || (DeathGeneration == 0u))
                {
                    return false;
                }

                switch (Kind)
                {
                    case RunPlayerLifeFactKind.Death:
                        return true;
                    case RunPlayerLifeFactKind.Rescue:
                    case RunPlayerLifeFactKind.SwallowedRescue:
                        return (Actor.IsValid && (Actor != Target));
                    default:
                        return false;
                }
            }
        }

        public RunPlayerLifeFact(RunId run, RunPlayerLifeFactKind kind, ParticipantId actor,
            ParticipantId target, uint deathGeneration)
        {
            Run             = run;
            Kind            = kind;
            Actor           = actor;
            Target          = target;
            DeathGeneration = deathGeneration;
        }

        public static RunPlayerLifeFact Death(RunId run, ParticipantId target,
            uint deathGeneration)
        {
            return new RunPlayerLifeFact(run, RunPlayerLifeFactKind.Death,
                ParticipantId.None, target, deathGeneration);
        }

        public static RunPlayerLifeFact Rescue(RunId run, ParticipantId actor,
            ParticipantId target, uint deathGeneration)
        {
            return new RunPlayerLifeFact(run, RunPlayerLifeFactKind.Rescue,
                actor, target, deathGeneration);
        }

        public static RunPlayerLifeFact SwallowedRescue(RunId run, ParticipantId actor,
            ParticipantId target, uint deathGeneration)
        {
            return new RunPlayerLifeFact(run, RunPlayerLifeFactKind.SwallowedRescue,
                actor, target, deathGeneration);
        }
    }
}
