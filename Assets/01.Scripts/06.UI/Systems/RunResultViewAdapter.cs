// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary> 결과 화면이 서버 snapshot을 다시 계산하지 않고 읽는 복사본 </summary>
    public sealed class RunResultViewData
    {
        private readonly RunParticipantResultSnapshot[] _participants;

        public ulong SessionIdValue { get; }
        public uint RunIdValue { get; }
        public uint ResultRevision { get; }
        public RunOutcome Outcome { get; }
        public RunPartyKind PartyKind { get; }
        public float FieldElapsedSeconds { get; }
        public float BossElapsedSeconds { get; }
        public bool BossKilled { get; }
        public int EnemyKills { get; }
        public int DeathCount { get; }
        public int RemainingCurrency { get; }
        public float RemainingHealthSum { get; }
        public bool HasAuthoritativeScore { get; }
        public int Score { get; }
        public RunEvaluationGrade EvaluationGrade { get; }
        public string EvaluationMessage { get; }
        public bool IsLeaderboardEligible { get; }
        public RunParticipantResultSnapshot[] Participants =>
            (RunParticipantResultSnapshot[]) _participants.Clone();

        public RunResultViewData(in RunResultSnapshot result)
        {
            SessionIdValue         = result.SessionIdValue;
            RunIdValue             = result.RunIdValue;
            ResultRevision         = result.ResultRevision;
            Outcome               = result.Outcome;
            PartyKind             = result.PartyKind;
            FieldElapsedSeconds   = result.FieldElapsedSeconds;
            BossElapsedSeconds    = result.BossElapsedSeconds;
            BossKilled            = result.BossKilled;
            EnemyKills            = result.EnemyKills;
            DeathCount            = result.DeathCount;
            RemainingCurrency     = result.RemainingCurrency;
            RemainingHealthSum    = result.RemainingHealthSum;
            HasAuthoritativeScore = result.HasAuthoritativeScore;
            Score                 = result.Score;
            EvaluationGrade       = result.EvaluationGrade;
            EvaluationMessage     = result.EvaluationMessage ?? string.Empty;
            IsLeaderboardEligible = result.IsLeaderboardEligible;
            _participants = (result.Participants == null)
                ? Array.Empty<RunParticipantResultSnapshot>()
                : (RunParticipantResultSnapshot[]) result.Participants.Clone();
        }
    }

    /// <summary> UI가 권위 snapshot만 표시하도록 막는 읽기 전용 어댑터 </summary>
    public static class RunResultViewAdapter
    {
        public static bool TryCreate(in RunResultSnapshot result, out RunResultViewData view)
        {
            if ((!result.HasAuthoritativeFacts) || (result.Outcome == RunOutcome.None) ||
                (result.Participants == null))
            {
                view = null;
                return false;
            }

            view = new RunResultViewData(result);
            return true;
        }
    }
}
