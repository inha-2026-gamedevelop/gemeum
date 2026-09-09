// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Systems;

namespace Geumeum.Systems.Tests
{
    public class EnemyDiedEventTests
    {
        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void EnemyFactoryStoresSpawnSnapshotsAndKind()
        {
            EnemyDiedEvent died = EnemyDiedEvent.ForEnemy(11u, EnemyKind.Changgwi,
                new Vector3(1f, 2f, 3f), 7u, 2, 1.5f);

            Assert.IsTrue(died.IsValid);
            Assert.IsFalse(died.IsBoss);
            Assert.AreEqual(EnemyKind.Changgwi, died.Kind.Value);
            Assert.AreEqual(2, died.FieldLevelAtSpawn);
            Assert.AreEqual(1.5f, died.RewardMultiplier);
        }

        [Test]
        public void BossFactoryUsesFixedRewardSnapshotAndNullKind()
        {
            EnemyDiedEvent died = EnemyDiedEvent.ForBoss(22u, Vector3.zero,
                DamageInfo.NO_ATTACKER);

            Assert.IsTrue(died.IsValid);
            Assert.IsTrue(died.IsBoss);
            Assert.IsFalse(died.Kind.HasValue);
            Assert.AreEqual(DropRules.BOSS_REWARD_FIELD_LEVEL, died.FieldLevelAtSpawn);
            Assert.AreEqual(1f, died.RewardMultiplier);
        }

        [Test]
        public void InvalidEnemyFactoryInputCannotBeConsumedAsReward()
        {
            Assert.IsFalse(EnemyDiedEvent.ForEnemy(0u, EnemyKind.Heokkaebi,
                Vector3.zero, DamageInfo.NO_ATTACKER, 0, 1f).IsValid);
            Assert.IsFalse(EnemyDiedEvent.ForEnemy(11u, EnemyKind.Boss,
                Vector3.zero, DamageInfo.NO_ATTACKER, 0, 1f).IsValid);
            Assert.IsFalse(EnemyDiedEvent.ForEnemy(11u, EnemyKind.Heokkaebi,
                Vector3.zero, DamageInfo.NO_ATTACKER, -1, 1f).IsValid);
            Assert.IsFalse(EnemyDiedEvent.ForEnemy(11u, EnemyKind.Heokkaebi,
                Vector3.zero, DamageInfo.NO_ATTACKER, 0, float.NaN).IsValid);
            Assert.IsFalse(EnemyDiedEvent.ForEnemy(11u, EnemyKind.Heokkaebi,
                new Vector3(float.PositiveInfinity, 0f, 0f), DamageInfo.NO_ATTACKER, 0, 1f).IsValid);
        }

        [Test]
        public void DefaultEventIsInvalid()
        {
            Assert.IsFalse(default(EnemyDiedEvent).IsValid);
        }
    }
}
