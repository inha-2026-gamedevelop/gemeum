// System
using System;

namespace Geumeum.Common
{
    public enum PlayerActionCueKind : byte
    {
        None   = 0,
        Attack = 1,
        Heal   = 2,
        Parry  = 3,
        EchoRecall = 4,
        WeaponSkill = 5,
    }

    /// <summary>서버가 수락한 플레이어 행동의 표현 시간표</summary>
    [Serializable]
    public struct PlayerActionCue
    {
        public uint                ExecutionId;
        public PlayerActionCueKind Kind;
        public WeaponKind          Weapon;
        public byte                ComboStage;
        public double              StartGameplayTime;
        public double              ImpactGameplayTime;
        public double              EndGameplayTime;
    }

    public enum PlayerActionCueDecision
    {
        Ignore  = 0,
        Start   = 1,
    }

    public static class PlayerActionCueRules
    {
        public static PlayerActionCueDecision ResolveStart(in PlayerActionCue cue,
            double gameplayTime, PlayerActionCueKind currentKind, uint currentExecutionId,
            out float normalizedProgress)
        {
            normalizedProgress = 0f;
            if ((!IsValid(in cue)) || (!IsFinite(gameplayTime)) ||
                (gameplayTime >= cue.EndGameplayTime))
            {
                return PlayerActionCueDecision.Ignore;
            }

            if ((currentKind == cue.Kind) && (currentExecutionId != 0u) &&
                (!IsNewer(cue.ExecutionId, currentExecutionId)))
            {
                return PlayerActionCueDecision.Ignore;
            }

            double duration = cue.EndGameplayTime - cue.StartGameplayTime;
            normalizedProgress = (float) Math.Max(0d,
                Math.Min(1d, (gameplayTime - cue.StartGameplayTime) / duration));
            return PlayerActionCueDecision.Start;
        }

        public static bool CanCancel(PlayerActionCueKind requestedKind,
            uint requestedExecutionId, PlayerActionCueKind currentKind,
            uint currentExecutionId)
        {
            return (requestedKind != PlayerActionCueKind.None) &&
                (requestedKind == currentKind) &&
                (requestedExecutionId != 0u) &&
                (requestedExecutionId == currentExecutionId);
        }

        public static bool IsLateOneShot(in PlayerActionCue cue,
            double gameplayTime, float toleranceSeconds)
        {
            if (!IsValid(in cue) || !IsFinite(gameplayTime))
            {
                return true;
            }

            return (gameplayTime - cue.StartGameplayTime) >
                Math.Max(0f, toleranceSeconds);
        }

        public static bool IsLateOneShot(double eventGameplayTime,
            double gameplayTime, float toleranceSeconds)
        {
            if (!IsFinite(eventGameplayTime) || !IsFinite(gameplayTime))
            {
                return true;
            }

            double elapsed = gameplayTime - eventGameplayTime;
            return (elapsed < 0d) || (elapsed > Math.Max(0f, toleranceSeconds));
        }

        public static bool IsValid(in PlayerActionCue cue)
        {
            return (cue.ExecutionId != 0u) &&
                (cue.Kind != PlayerActionCueKind.None) &&
                IsFinite(cue.StartGameplayTime) &&
                IsFinite(cue.ImpactGameplayTime) &&
                IsFinite(cue.EndGameplayTime) &&
                (cue.StartGameplayTime <= cue.ImpactGameplayTime) &&
                (cue.ImpactGameplayTime <= cue.EndGameplayTime) &&
                (cue.StartGameplayTime < cue.EndGameplayTime);
        }

        private static bool IsNewer(uint candidate, uint baseline)
        {
            return unchecked((int) (candidate - baseline)) > 0;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
