// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 필드 입구가 보스 아레나 준비를 끝낼 때까지의 서버 상태
    /// RunPhase와 별개로 동기화하므로 전실에서도 필드 phase를 억지로 바꾸지 않는다
    /// </summary>
    public enum BossEntranceState
    {
        Idle                = 0, // 아직 입구 카운트다운이 시작되지 않았다
        CountingDown        = 1, // 확정된 필드 시각까지 남은 시간을 센다
        TransitionRequested = 2, // 전실 전환을 요청할 차례다
        Entering            = 3, // 전실 또는 아레나 전환과 준비를 수행 중이다
        Ready               = 4, // 아레나 준비가 끝나 보스 phase를 시작할 수 있다
    }

    /// <summary> 보스 입구의 시간 경계와 요청 자격을 계산하는 순수 규칙 </summary>
    public static class BossEntranceRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 첫 요청의 만료 시각을 확정한다. 이미 시작했거나 시간 값이 잘못되면 false다 </summary>
        public static bool TryGetDeadline(BossEntranceState state, float currentFieldSeconds,
            float countdownSeconds, float fieldLimitSeconds, out float deadlineFieldSeconds)
        {
            deadlineFieldSeconds = 0f;

            if ((state != BossEntranceState.Idle) || (!IsFinite(currentFieldSeconds)) ||
                (!IsFinite(countdownSeconds)) || (!IsFinite(fieldLimitSeconds)) ||
                (currentFieldSeconds < 0f) || (countdownSeconds <= 0f) || (fieldLimitSeconds <= 0f))
            {
                return false;
            }

            float clampedCurrent = Math.Min(currentFieldSeconds, fieldLimitSeconds);
            deadlineFieldSeconds = Math.Min(clampedCurrent + countdownSeconds, fieldLimitSeconds);
            return true;
        }

        /// <summary> 자동 카운트다운을 시작할 필드 시각 </summary>
        public static float GetAutomaticStart(float countdownSeconds, float fieldLimitSeconds)
        {
            if ((!IsFinite(countdownSeconds)) || (!IsFinite(fieldLimitSeconds)) ||
                (countdownSeconds <= 0f) || (fieldLimitSeconds <= 0f))
            {
                return 0f;
            }

            return Math.Max(0f, fieldLimitSeconds - countdownSeconds);
        }

        public static bool HasExpired(BossEntranceState state, float currentFieldSeconds,
            float deadlineFieldSeconds)
        {
            return ((state == BossEntranceState.CountingDown) &&
                    (currentFieldSeconds >= deadlineFieldSeconds));
        }

        public static float GetRemaining(BossEntranceState state, float currentFieldSeconds,
            float deadlineFieldSeconds)
        {
            if (state != BossEntranceState.CountingDown)
            {
                return 0f;
            }

            return Math.Max(0f, deadlineFieldSeconds - currentFieldSeconds);
        }

        /// <summary> 살아서 필드를 진행 중인 참가자만 입구를 열 수 있다 </summary>
        public static bool CanEnter(RunPhase phase, PlayerLifeSnapshotState lifeState, float health)
        {
            bool isFieldPhase = ((phase == RunPhase.Farming) || (phase == RunPhase.Shrink1) ||
                                 (phase == RunPhase.Shrink2) || (phase == RunPhase.Shrink3));
            bool isAlive = ((lifeState == PlayerLifeSnapshotState.Alive) ||
                            (lifeState == PlayerLifeSnapshotState.ReviveInvulnerable)) &&
                           IsFinite(health) && (health > 0f);
            return (isFieldPhase && isAlive);
        }

        /// <summary> 호스트가 참가자 이탈 또는 씬 전환 실패로 멈춘 런을 끝낼 수 있는가 </summary>
        public static bool CanAbandonParticipantWait(bool isHostConnection, RunPhase phase,
            RunPauseReason pauseReasons, bool hasTransitionFailure)
        {
            bool isActiveRun = ((phase != RunPhase.Lobby) && (phase != RunPhase.Result));
            bool isWaiting   = ((pauseReasons & RunPauseReason.ParticipantMissing) != 0);
            bool canResolveFailure = hasTransitionFailure;
            return (isHostConnection && ((isActiveRun && isWaiting) || canResolveFailure));
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
