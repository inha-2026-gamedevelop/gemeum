// System
using System;

namespace Geumeum.Common
{
    /// <summary> 확정된 런 결과. 씬이 파괴되기 전에 떠 두고, 정산 화면과 리더보드는 이것만 읽는다 </summary>
    [Serializable]
    public struct RunResultSnapshot : IEquatable<RunResultSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        public ulong      SessionIdValue;
        public uint       RunIdValue;
        public RunMode    Mode;
        public RunOutcome Outcome;
        public RunPartyKind PartyKind;

        public float FieldElapsedSeconds;
        public float BossElapsedSeconds;
        public float ScoreTimeLimitSeconds;

        public bool BossKilled;
        public int  EnemyKills;
        public int  DeathCount;

        public int   RemainingCurrency;
        public float RemainingHealthSum;

        public RunParticipantResultSnapshot[] Participants;

        public uint                ResultRevision;
        public RunResultFactsState FactsState;

        public int                Score;
        public RunEvaluationGrade EvaluationGrade;
        public string             EvaluationMessage;

        /// <summary> 점수를 계산한 규칙의 판. 나중에 배점이 바뀌어도 옛 기록을 다시 계산하지 않는다 </summary>
        public int ScoreRuleVersion;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 두 시계의 합. 전실, 컷신, 이탈 대기는 들어 있지 않다 </summary>
        public float ScoreElapsedSeconds => (FieldElapsedSeconds + BossElapsedSeconds);

        /// <summary> 리더보드에 올릴 수 있는 결과인지. 트라이와 개발 런은 승리해도 올리지 않는다 </summary>
        public bool IsLeaderboardEligible => ((Mode == RunMode.Standard) && (Outcome == RunOutcome.Victory));

        /// <summary> 점수와 개인 기여를 결과 화면에 확정값으로 표시할 수 있는가 </summary>
        public bool HasAuthoritativeFacts => (FactsState == RunResultFactsState.Authoritative);

        /// <summary> 설정 오류로 만든 0점과 실제 0점 D를 구분할 수 있는가 </summary>
        public bool HasAuthoritativeScore => ((ScoreRuleVersion > 0) &&
            (Outcome != RunOutcome.None) &&
            Enum.IsDefined(typeof(RunEvaluationGrade), EvaluationGrade) &&
            (((Outcome == RunOutcome.Victory) &&
              (EvaluationGrade != RunEvaluationGrade.None)) ||
             ((Outcome != RunOutcome.Victory) &&
              (EvaluationGrade == RunEvaluationGrade.None))));

        /// <summary> 같은 로컬 기록을 다시 쓰지 않게 하는 서버 확정 키가 있는가 </summary>
        public bool HasRecordKey => ((SessionIdValue != 0UL) && (RunIdValue != 0u) &&
            (ResultRevision != 0u));

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(RunResultSnapshot other)
        {
            return ((RunIdValue == other.RunIdValue) &&
                    (Mode == other.Mode) &&
                    (Outcome == other.Outcome) &&
                    (Score == other.Score));
        }

        public override bool Equals(object obj) => ((obj is RunResultSnapshot other) && (Equals(other)));

        public override int GetHashCode() => (RunIdValue.GetHashCode() ^ Score.GetHashCode());
    }
}
