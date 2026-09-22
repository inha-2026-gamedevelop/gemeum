namespace Geumeum.AI
{
    /// <summary> 예약은 만료, 세대 불일치에도 한 번 소비한 뒤 폐기한다 </summary>
    public struct BotScheduledAction
    {
        public bool Pending;
        public uint Run;
        public uint Scene;
        public uint Generation;
        public uint Target;
        public double ExecuteAt;
        public double ExpiresAt;

        public bool TryConsume(double now, uint run, uint scene, uint generation,
            uint target, bool valid)
        {
            if (!Pending) { return false; }
            if ((!valid) || (run != Run) || (scene != Scene) ||
                (generation != Generation) || (target != Target) || (now > ExpiresAt))
            {
                Pending = false;
                return false;
            }
            if (now < ExecuteAt) { return false; }
            Pending = false;
            return true;
        }
    }
}
