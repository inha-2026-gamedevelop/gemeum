using UnityEngine;

namespace Geumeum.Data
{
    [CreateAssetMenu(menuName = "Geumeum/Bot Config")]
    public sealed class BotConfigSO : ScriptableObject
    {
        [Min(0.01f)] public float DecisionInterval = 0.1f;
        [Min(0.01f)] public float PathInterval = 0.25f;
        [Min(0.01f)] public float ScheduledLifetime = 0.2f;
        [Min(0.01f)] public float AttackRetryInterval = 0.1f;
        [Min(0.1f)] public float FollowDistance = 3f;
        [Min(0.1f)] public float FollowHysteresis = 1f;
        [Min(0.1f)] public float NavSampleRadius = 2f;
        [Min(0.01f)] public float CornerTolerance = 0.4f;
        [Min(0.1f)] public float EyeHeight = 1.2f;
        [Min(0.1f)] public float BowDistance = 10f;
        [Range(0.1f, 1f)] public float MeleeRangeRatio = 0.8f;
        [Min(0.1f)] public float StuckSeconds = 2f;
        [Min(0.01f)] public float StuckDistance = 0.15f;
        public LayerMask ObstacleMask = ~0;
        [Min(0.1f)] public float RetreatDistance = 3f;
        [Min(0.1f)] public float BowMinimumDistance = 3f;
        [Min(0.1f)] public float SupportRetrySeconds = 2f;
        [Min(0.1f)] public float RescueRange = 2.5f;
        [Min(0.1f)] public float RescueSafetyRadius = 4f;
        [Min(0.1f)] public float HealRadius = 6f;
        [Range(0f, 1f)] public float HealCriticalRatio = 0.4f;
        [Range(0f, 1f)] public float HealGroupRatio = 0.6f;
        [Min(0.1f)] public float PrecisionTimeout = 5f;
        [Min(0.1f)] public float FollowSprintDistance = 6f;
        [Min(0.1f)] public float FollowSprintStopDistance = 4f;
        [Min(0.1f)] public float RegroupStopDistance = 5f;
        [Min(0.1f)] public float AssistDistance = 6f;
        [Min(0.1f)] public float RegroupDistance = 10f;
        [Min(0.1f)] public float BossWakeDistance = 4f;
        [Min(0.1f)] public float OutOfCombatSeconds = 5f;
        [Range(0f, 1f)] public float PotionHealthRatio = 0.35f;
        [Min(0f)] public float PotionSpiritThreshold = 25f;
        [Min(0f)] public float SwordFormationDistance = 1.5f;
        [Min(0f)] public float BowFormationDistance = 3f;
        [Min(0f)] public float BellFormationDistance = 2f;
        [Min(0f)] public float FormationTolerance = 0.8f;
        [Min(0f)] public float HealCommitSeconds = 1.1f;
        [Min(0f)] public float SwordRangedChaseLimit = 8f;
        [Min(0f)] public float ParryReserveCost = 15f;
        public int[] SwordTalentOrder = { 1001, 1002, 1003, 1004, 1005, 1011, 1012, 1013, 1014, 1015 };
        public int[] BowTalentOrder = { 2001, 2002, 2003, 2004, 2005, 2011, 2012, 2013, 2014, 2015 };
        // 뿌리 -> 퍼뜨리는 길 -> 지키는 길 순. 지원 진을 먼저 키우고 치유 연계를 잇는다
        public int[] BellTalentOrder =
            { 3101, 3110, 3111, 3112, 3113, 3106, 3107, 3108, 3109 };

        /// <summary> 봇이 예고된 보스 장판 위에 진을 깔지 않도록 피하는 반경(m) </summary>
        [Min(0f)] public float BellFieldHazardRadius = 4f;
    }
}
