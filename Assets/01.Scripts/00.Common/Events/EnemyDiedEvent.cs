// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 잡귀와 보스가 죽었을 때 서버가 발행한다
    /// 드랍, 경험치, 점수가 이것을 구독하므로 몬스터 파트가 게임플레이 시스템 파트를 직접 부르지 않는다
    /// </summary>
    public readonly struct EnemyDiedEvent
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int BOSS_REWARD_FIELD_LEVEL = Constants.Combat.FIELD_LEVEL_COUNT - 1;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 죽은 대상의 netId </summary>
        public uint EnemyNetId { get; }

        /// <summary> 일반 잡귀 종류. 보스 사망 이벤트에서는 null이다 </summary>
        public EnemyKind? Kind { get; }

        /// <summary> 사망 위치, 드랍 스폰 지점이 된다 </summary>
        public Vector3 Position { get; }

        /// <summary> 마지막 타격을 넣은 플레이어의 netId, 없으면 DamageInfo.NO_ATTACKER </summary>
        public uint KillerNetId { get; }

        /// <summary> 보스인지, 드랍 테이블과 점수 배점이 갈린다 </summary>
        public bool IsBoss { get; }

        /// <summary> 스폰 시점에 확정된 결계 강화 단계 </summary>
        public int FieldLevelAtSpawn { get; }

        /// <summary> 스폰 시점에 확정된 처치 보상 배수 </summary>
        public float RewardMultiplier { get; }

        /// <summary> 보상 소비자가 처리해도 되는 이벤트인지 확인한다 </summary>
        public bool IsValid
        {
            get
            {
                if ((EnemyNetId == 0u) || !IsFinite(Position) ||
                    (FieldLevelAtSpawn < 0) || (FieldLevelAtSpawn >= Constants.Combat.FIELD_LEVEL_COUNT) ||
                    !IsFinite(RewardMultiplier) || (RewardMultiplier < 0f))
                {
                    return false;
                }

                if (IsBoss)
                {
                    return ((!Kind.HasValue) && (FieldLevelAtSpawn == BOSS_REWARD_FIELD_LEVEL) &&
                            (RewardMultiplier == 1f));
                }

                return (Kind.HasValue && ((int) Kind.Value >= 0) &&
                        ((int) Kind.Value < Constants.Combat.ENEMY_KIND_COUNT));
            }
        }

        /****************************************
        *            Constructors
        ****************************************/

        private EnemyDiedEvent(uint enemyNetId, EnemyKind? kind, Vector3 position, uint killerNetId,
            bool isBoss, int fieldLevelAtSpawn, float rewardMultiplier)
        {
            EnemyNetId        = enemyNetId;
            Kind              = kind;
            Position          = position;
            KillerNetId       = killerNetId;
            IsBoss            = isBoss;
            FieldLevelAtSpawn = fieldLevelAtSpawn;
            RewardMultiplier  = rewardMultiplier;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 일반 잡귀의 사망 시점 snapshot을 만든다 </summary>
        public static EnemyDiedEvent ForEnemy(uint enemyNetId, EnemyKind kind, Vector3 position,
            uint killerNetId, int fieldLevelAtSpawn, float rewardMultiplier)
        {
            return new EnemyDiedEvent(enemyNetId, kind, position, killerNetId, false,
                fieldLevelAtSpawn, rewardMultiplier);
        }

        /// <summary> 보스 사망 snapshot을 만든다. 보스 보상은 항상 최종 필드 구간과 배수 1을 쓴다 </summary>
        public static EnemyDiedEvent ForBoss(uint bossNetId, Vector3 position, uint killerNetId)
        {
            return new EnemyDiedEvent(bossNetId, null, position, killerNetId, true,
                BOSS_REWARD_FIELD_LEVEL, 1f);
        }

        private static bool IsFinite(Vector3 value)
        {
            return (IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z));
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
