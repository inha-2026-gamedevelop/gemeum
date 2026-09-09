// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 적 하나가 죽었을 때 장비를 떨어뜨릴지, 어떤 무기로 굴릴지 정한다.
    /// Mirror와 UnityEngine에 의존하지 않는 순수 규칙이라 고정 seed로 검증할 수 있다.
    /// 실제 생성은 EquipmentGenerator가, 월드 스폰은 서버 실행부가 맡는다
    /// </summary>
    public sealed class DropRules
    {
        /****************************************
        *               Constants
        ****************************************/

        // Constants.Combat.FIELD_LEVEL_COUNT는 4이고 유효 레벨은 0~3이다.
        // 게임 디자인 6.4의 3차 가중치는 보스 처치 보상에만 쓴다
        public const int BOSS_REWARD_FIELD_LEVEL = Constants.Combat.FIELD_LEVEL_COUNT - 1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly struct DeathKey : IEquatable<DeathKey>
        {
            public readonly uint RunIdValue;
            public readonly uint EnemyNetId;

            public DeathKey(RunId runId, uint enemyNetId)
            {
                RunIdValue = runId.Value;
                EnemyNetId = enemyNetId;
            }

            public bool Equals(DeathKey other)
            {
                return ((RunIdValue == other.RunIdValue) && (EnemyNetId == other.EnemyNetId));
            }

            public override bool Equals(object obj)
            {
                return ((obj is DeathKey other) && Equals(other));
            }

            public override int GetHashCode()
            {
                return (RunIdValue.GetHashCode() ^ EnemyNetId.GetHashCode());
            }
        }

        private readonly float  _dropChance;
        private readonly Random _random;
        private readonly HashSet<DeathKey> _processedDeaths = new HashSet<DeathKey>();

        /****************************************
        *                Methods
        ****************************************/

        // 확률과 난수는 서버 런 소유자가 주입한다. 씬마다 새로 만들면 난수열이 끊긴다
        public DropRules(float dropChance, Random random)
        {
            if ((float.IsNaN(dropChance)) || (dropChance < 0f) || (dropChance > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(dropChance), "드랍 확률은 0과 1 사이여야 한다");
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            _dropChance = dropChance;
            _random     = random;
        }

        /// <summary>
        /// 이번 사망에 장비를 떨어뜨릴지 정한다.
        /// 보스는 확률을 타지 않고 팀 보상으로 항상 하나 준다
        /// </summary>
        public bool ShouldDrop(bool isBoss)
        {
            if (isBoss)
            {
                return true;
            }

            // 확률이 0이면 난수를 소비하지 않는다. 드랍을 꺼도 이후 난수열이 흔들리면 안 된다
            if (_dropChance <= 0f)
            {
                return false;
            }

            return (_random.NextDouble() < _dropChance);
        }

        /// <summary> 같은 런의 같은 적 사망 이벤트를 한 번만 판정한다 </summary>
        public bool TryClaimDeath(RunId runId, uint enemyNetId)
        {
            if ((!runId.IsValid) || (enemyNetId == 0u))
            {
                return false;
            }

            return _processedDeaths.Add(new DeathKey(runId, enemyNetId));
        }

        /// <summary> 떨어질 장비의 무기 종류를 고른다. 세 종류가 같은 확률이다 </summary>
        public WeaponKind RollWeaponKind()
        {
            return (WeaponKind) _random.Next(0, Constants.Combat.WEAPON_KIND_COUNT);
        }

        /// <summary> 등급 가중치를 고를 때 쓸 필드 레벨. 보스 보상만 3차 가중치를 쓴다 </summary>
        public static int ResolveFieldLevel(bool isBoss, int fieldLevelAtSpawn)
        {
            return (isBoss ? BOSS_REWARD_FIELD_LEVEL : fieldLevelAtSpawn);
        }

        /// <summary> 이벤트 snapshot으로 장비 생성 구간을 확정한다 </summary>
        public static bool TryResolveFieldLevel(bool isBoss, int fieldLevelAtSpawn, out int fieldLevel)
        {
            fieldLevel = BOSS_REWARD_FIELD_LEVEL;
            if (isBoss)
            {
                if (fieldLevelAtSpawn != BOSS_REWARD_FIELD_LEVEL)
                {
                    fieldLevel = 0;
                    return false;
                }

                return true;
            }

            if ((fieldLevelAtSpawn < 0) || (fieldLevelAtSpawn >= Constants.Combat.FIELD_LEVEL_COUNT))
            {
                fieldLevel = 0;
                return false;
            }

            fieldLevel = fieldLevelAtSpawn;
            return true;
        }
    }
}
