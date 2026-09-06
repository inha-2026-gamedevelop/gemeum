namespace Geumeum.Common
{
    /// <summary>
    /// 런 상태를 소리로 옮기는 창구. 어느 구간에 어떤 곡이 흐르는지를 여기서만 정한다.
    /// RunManager가 판정을 소유하고 이 클래스는 그 결과를 받기만 한다, 반대 방향 호출은 없다
    /// </summary>
    public static class RunSoundDirector
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 런 구간이 바뀌었다. RunManager의 SyncVar 훅이 모든 클라이언트에서 부른다 </summary>
        public static void OnPhaseChanged(RunPhase phase)
        {
            BgmTrack track = ResolveTrack(phase);

            if (track == BgmTrack.None)
            {
                if (BgmPlayer.HasInstance)
                {
                    BgmPlayer.Instance.Stop();
                }

                return;
            }

            BgmPlayer player = BgmPlayer.Ensure();

            // 개활은 세 번의 축소 내내 같은 곡이다. 축소마다 다시 틀면 이음매에서 곡이 처음으로 돌아간다
            if (player.CurrentTrack == track)
            {
                return;
            }

            player.Play(track);
        }

        /// <summary> 이탈자를 기다리며 세계가 얼었거나, 다시 풀렸다 </summary>
        public static void OnPausedChanged(bool isPaused)
        {
            if (isPaused)
            {
                SfxPlayer.PauseAll();

                if (BgmPlayer.HasInstance)
                {
                    BgmPlayer.Instance.Pause();
                }

                return;
            }

            SfxPlayer.ResumeAll();

            if (BgmPlayer.HasInstance)
            {
                BgmPlayer.Instance.UnPause();
            }
        }

        /// <summary>
        /// 구간에 걸린 트랙.
        /// 보스 2페이즈는 RunPhase가 아니라 보스 내부 상태라 여기가 아니라 보스 쪽에서 건다
        /// </summary>
        private static BgmTrack ResolveTrack(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Farming:
                case RunPhase.Shrink1:
                case RunPhase.Shrink2:
                case RunPhase.Shrink3:
                    return BgmTrack.Field;

                case RunPhase.Boss:
                    return BgmTrack.Boss1;

                // 로비와 정산에는 아직 곡이 없다. 곡이 생기면 트랙을 늘리고 여기만 고친다
                default:
                    return BgmTrack.None;
            }
        }
    }
}
