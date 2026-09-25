// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art.Tests
{
    /// <summary> 결과 주도 타격 표현의 순수 규칙(COMBAT_STYLE_DESIGN 5.1, S1, S3, S5) </summary>
    public sealed class ImpactRulesTests
    {
        private const float TOLERANCE = 0.0001f;

        /****************************************
        *        Execution Merge (5.1)
        ****************************************/

        [Test]
        public void SameExecutionKeepsHighestTierOnce()
        {
            var merger = new ImpactExecutionMerger(8);

            Assert.IsTrue(merger.Add(Result(7u, 1u, 100u, DamageSourceKind.Melee), ImpactTier.Light));
            Assert.IsTrue(merger.Add(Result(7u, 1u, 101u, DamageSourceKind.Melee), ImpactTier.Heavy));
            Assert.IsTrue(merger.Add(Result(7u, 1u, 102u, DamageSourceKind.Melee), ImpactTier.Medium));

            Assert.AreEqual(1, merger.Count);
            Assert.AreEqual(ImpactTier.Heavy, merger.Get(0).Tier);
            Assert.AreEqual(101u, merger.Get(0).Result.TargetNetId);
        }

        [Test]
        public void DifferentExecutionsOrAttackersStaySeparate()
        {
            var merger = new ImpactExecutionMerger(8);

            merger.Add(Result(7u, 1u, 100u, DamageSourceKind.Melee), ImpactTier.Light);
            merger.Add(Result(7u, 2u, 100u, DamageSourceKind.Melee), ImpactTier.Light);
            merger.Add(Result(8u, 1u, 100u, DamageSourceKind.Melee), ImpactTier.Light);

            Assert.AreEqual(3, merger.Count);
        }

        [Test]
        public void ZeroExecutionIdIsOneExecutionPerResult()
        {
            var merger = new ImpactExecutionMerger(8);

            merger.Add(Result(7u, 0u, 100u, DamageSourceKind.Derived), ImpactTier.Light);
            merger.Add(Result(7u, 0u, 101u, DamageSourceKind.Derived), ImpactTier.Light);

            Assert.AreEqual(2, merger.Count);
        }

        [Test]
        public void FullBufferRejectsNewExecutionButStillMergesExisting()
        {
            var merger = new ImpactExecutionMerger(1);

            Assert.IsTrue(merger.Add(Result(7u, 1u, 100u, DamageSourceKind.Melee), ImpactTier.Light));
            Assert.IsFalse(merger.Add(Result(7u, 2u, 100u, DamageSourceKind.Melee), ImpactTier.Light));
            Assert.IsTrue(merger.Add(Result(7u, 1u, 101u, DamageSourceKind.Melee), ImpactTier.Kill));
            Assert.AreEqual(ImpactTier.Kill, merger.Get(0).Tier);

            merger.Clear();
            Assert.AreEqual(0, merger.Count);
        }

        /****************************************
        *          Kill Chain (S3)
        ****************************************/

        [Test]
        public void SecondKillInsideWindowDropsToMedium()
        {
            var damper = new KillChainDamper();

            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(0d, 10d), 0.5f));
            Assert.AreEqual(ImpactTier.Medium, damper.Apply(ImpactTier.Kill, Kill(0d, 10.3d), 0.5f));
            // 창은 마지막 처치부터 다시 잰다
            Assert.AreEqual(ImpactTier.Medium, damper.Apply(ImpactTier.Kill, Kill(0d, 10.7d), 0.5f));
            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(0d, 11.5d), 0.5f));
        }

        [Test]
        public void NonKillTiersPassThroughAndResetForgetsChain()
        {
            var damper = new KillChainDamper();

            Assert.AreEqual(ImpactTier.Heavy, damper.Apply(ImpactTier.Heavy, Kill(0d, 10d), 0.5f));
            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(0d, 10d), 0.5f));

            damper.Reset();
            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(0d, 10.1d), 0.5f));
        }

        /// <summary> 게임 시각이 멈춘 구간(Dev 씬, 필드 시간 상한, 보스 시계 시작 전)에도 서버 시각이 흐르면 T4가 다시 나온다 </summary>
        [Test]
        public void FrozenGameplayClockDoesNotLockKillChain()
        {
            var damper = new KillChainDamper();
            const double FROZEN = 0d;

            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(FROZEN, 50d), 0.5f));
            Assert.AreEqual(ImpactTier.Medium, damper.Apply(ImpactTier.Kill, Kill(FROZEN, 50.2d), 0.5f));
            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(FROZEN, 52d), 0.5f));
            Assert.AreEqual(ImpactTier.Kill, damper.Apply(ImpactTier.Kill, Kill(FROZEN, 60d), 0.5f));
        }

        /****************************************
        *       Target Hit Stop Limiter (S1)
        ****************************************/

        [Test]
        public void TargetStopNeverExceedsMaxFromFirstRequest()
        {
            var limiter = new TargetHitStopLimiter();

            Assert.IsTrue(limiter.Request(0d, 0.06f, 0.10f));
            Assert.That(limiter.EndTime, Is.EqualTo(0.06d).Within(TOLERANCE));

            // 이어진 요청은 새로 시작하지 않고 끝을 늘리되 첫 요청 + 상한을 넘지 않는다
            Assert.IsFalse(limiter.Request(0.05d, 0.08f, 0.10f));
            Assert.That(limiter.EndTime, Is.EqualTo(0.10d).Within(TOLERANCE));

            Assert.IsFalse(limiter.Request(0.09d, 0.08f, 0.10f));
            Assert.That(limiter.EndTime, Is.EqualTo(0.10d).Within(TOLERANCE));
        }

        [Test]
        public void TargetStopEndsThenCooldownBlocksRetrigger()
        {
            var limiter = new TargetHitStopLimiter();

            limiter.Request(0d, 0.04f, 0.10f);
            Assert.IsFalse(limiter.Tick(0.03d, 0.10f));
            Assert.IsTrue(limiter.Tick(0.05d, 0.10f));
            Assert.IsFalse(limiter.IsActive);

            // 끝난 시각(0.04)부터 유예 0.10초
            Assert.IsFalse(limiter.Request(0.10d, 0.04f, 0.10f));
            Assert.IsTrue(limiter.Request(0.15d, 0.04f, 0.10f));
        }

        [Test]
        public void TargetStopCancelRestoresAndStartsCooldown()
        {
            var limiter = new TargetHitStopLimiter();

            limiter.Request(0d, 0.08f, 0.10f);
            Assert.IsTrue(limiter.Cancel(0.02d, 0.10f));
            Assert.IsFalse(limiter.Cancel(0.02d, 0.10f));
            Assert.IsFalse(limiter.Request(0.05d, 0.04f, 0.10f));
            Assert.IsTrue(limiter.Request(0.13d, 0.04f, 0.10f));
        }

        [Test]
        public void TargetStopIgnoresZeroLengthOrZeroMax()
        {
            var limiter = new TargetHitStopLimiter();

            Assert.IsFalse(limiter.Request(0d, 0f, 0.10f));
            Assert.IsFalse(limiter.Request(0d, 0.04f, 0f));
            Assert.IsFalse(limiter.IsActive);
        }

        /****************************************
        *          Late Result (8.1)
        ****************************************/

        [Test]
        public void LateResultIsDiscardedOnlyPastThreshold()
        {
            Assert.IsFalse(ImpactRules.IsLate(10.4d, 10d, 0.5f));
            Assert.IsFalse(ImpactRules.IsLate(10.5d, 10d, 0.5f));
            Assert.IsTrue(ImpactRules.IsLate(10.6d, 10d, 0.5f));

            // 클라이언트 NetworkTime.time은 보간 버퍼만큼 뒤에 있어 결과가 앞서 보일 수 있다. 늦은 것이 아니다
            Assert.IsFalse(ImpactRules.IsLate(10d, 10.2d, 0.5f));

            Assert.IsTrue(ImpactRules.IsLate(10d, double.NaN, 0.5f));
        }

        /// <summary>
        /// 늦음은 결과의 ServerTime으로 잰다. 복제된 게임 시각은 결과와 같이 늦게 오거나 멈춰 있어 지연을 보여 주지 못한다
        /// </summary>
        [Test]
        public void LateResultUsesServerTimeNotGameplayTime()
        {
            // 게임 시각은 같아 보이지만 서버 시각으로는 0.8초 늦게 도착했다
            Assert.IsTrue(ImpactRules.IsLateResult(Kill(5d, 100d), 100.8d, 0.5f));

            // 게임 시각이 멈춰(0) 한참 뒤처져 보여도 서버 시각으로 제때 왔으면 버리지 않는다
            Assert.IsFalse(ImpactRules.IsLateResult(Kill(0d, 100d), 100.1d, 0.5f));
        }

        /****************************************
        *         Weapon Weight (S5)
        ****************************************/

        [Test]
        public void SourceDecidesWeaponAndUnknownFallsBack()
        {
            Assert.AreEqual(WeaponKind.Sword, ImpactRules.ResolveWeapon(DamageSourceKind.CounterSlash, WeaponKind.Bell));
            Assert.AreEqual(WeaponKind.Bow, ImpactRules.ResolveWeapon(DamageSourceKind.WedgeBurst, WeaponKind.Sword));
            Assert.AreEqual(WeaponKind.Bell, ImpactRules.ResolveWeapon(DamageSourceKind.EchoBurst, WeaponKind.Sword));
            Assert.AreEqual(WeaponKind.Bow, ImpactRules.ResolveWeapon(DamageSourceKind.Derived, WeaponKind.Bow));
        }

        [Test]
        public void AttackerStopUsesWeaponScale()
        {
            const float SWORD = 1f;
            const float BOW   = 0f;
            const float BELL  = 0.5f;
            const float STAB  = 0.03f;

            Assert.That(ImpactRules.ResolveAttackerHitStop(DamageSourceKind.Melee, WeaponKind.Sword, 0.06f, SWORD, BOW, BELL, STAB),
                Is.EqualTo(0.06f).Within(TOLERANCE));
            Assert.That(ImpactRules.ResolveAttackerHitStop(DamageSourceKind.PrecisionShot, WeaponKind.Bow, 0.08f, SWORD, BOW, BELL, STAB),
                Is.EqualTo(0f).Within(TOLERANCE));
            Assert.That(ImpactRules.ResolveAttackerHitStop(DamageSourceKind.EchoBurst, WeaponKind.Bell, 0.08f, SWORD, BOW, BELL, STAB),
                Is.EqualTo(0.04f).Within(TOLERANCE));
        }

        [Test]
        public void BowStabKeepsItsOwnMeleeStop()
        {
            Assert.That(ImpactRules.ResolveAttackerHitStop(DamageSourceKind.BowStab, WeaponKind.Bow, 0.06f, 1f, 0f, 0.5f, 0.03f),
                Is.EqualTo(0.03f).Within(TOLERANCE));
        }

        /****************************************
        *            Shake Mapping
        ****************************************/

        [Test]
        public void ShakeFollowsTierAndSource()
        {
            Assert.AreEqual(ImpactShakeKind.None, ImpactRules.ResolveShake(ImpactTier.Whiff, DamageSourceKind.Melee));
            Assert.AreEqual(ImpactShakeKind.HitLight, ImpactRules.ResolveShake(ImpactTier.Light, DamageSourceKind.Arrow));
            Assert.AreEqual(ImpactShakeKind.SwordSkill, ImpactRules.ResolveShake(ImpactTier.Medium, DamageSourceKind.MeleeFinisher));
            Assert.AreEqual(ImpactShakeKind.BowStab, ImpactRules.ResolveShake(ImpactTier.Medium, DamageSourceKind.BowStab));
            Assert.AreEqual(ImpactShakeKind.Critical, ImpactRules.ResolveShake(ImpactTier.Heavy, DamageSourceKind.PrecisionShot));
            Assert.AreEqual(ImpactShakeKind.EchoBurst, ImpactRules.ResolveShake(ImpactTier.Heavy, DamageSourceKind.EchoBurst));
            Assert.AreEqual(ImpactShakeKind.Critical, ImpactRules.ResolveShake(ImpactTier.Kill, DamageSourceKind.Melee));
            Assert.AreEqual(ImpactShakeKind.None, ImpactRules.ResolveShake(ImpactTier.Party, DamageSourceKind.Melee));
        }

        /****************************************
        *              Flinch (S2)
        ****************************************/

        [Test]
        public void FlinchTiltsTowardHitDirectionAndFades()
        {
            Vector3 axis   = ImpactRules.FlinchAxis(Vector3.forward);
            Vector3 tilted = Quaternion.AngleAxis(10f, axis) * Vector3.up;
            Assert.That(tilted.z, Is.GreaterThan(0f));

            Assert.AreEqual(Vector3.zero, ImpactRules.FlinchAxis(Vector3.zero));

            Assert.That(ImpactRules.FlinchWeight(0f, 0.15f), Is.EqualTo(1f).Within(TOLERANCE));
            Assert.That(ImpactRules.FlinchWeight(0.15f, 0.15f), Is.EqualTo(0f).Within(TOLERANCE));
            Assert.That(ImpactRules.FlinchWeight(0.1f, 0f), Is.EqualTo(0f).Within(TOLERANCE));
        }

        [Test]
        public void BusyEnemyStatesBlockTargetPresentation()
        {
            Assert.IsTrue(ImpactRules.IsEnemyBusy(EnemyVisualState.Telegraph));
            Assert.IsTrue(ImpactRules.IsEnemyBusy(EnemyVisualState.Charge));
            Assert.IsTrue(ImpactRules.IsEnemyBusy(EnemyVisualState.Attack));
            Assert.IsFalse(ImpactRules.IsEnemyBusy(EnemyVisualState.Move));
            Assert.IsFalse(ImpactRules.IsEnemyBusy(EnemyVisualState.Stagger));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static DamageResult Kill(double gameplayTime, double serverTime)
        {
            return new DamageResult
            {
                AttackerNetId = 7u,
                TargetNetId   = 100u,
                PartIndex     = DamageResult.NO_PART,
                Amount        = 1f,
                IsKill        = true,
                GameplayTime  = gameplayTime,
                ServerTime    = serverTime,
            };
        }

        private static DamageResult Result(uint attacker, uint execution, uint target, DamageSourceKind source)
        {
            return new DamageResult
            {
                AttackerNetId = attacker,
                ExecutionId   = execution,
                TargetNetId   = target,
                Source        = source,
                PartIndex     = DamageResult.NO_PART,
                Amount        = 1f,
            };
        }
    }
}
