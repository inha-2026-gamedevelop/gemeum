using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat.Tests
{
    public sealed partial class WeaponAbilityTests
    {
        private Target SkillTargetAt(float z)
        {
            Target target = TargetAt(z);
            target.gameObject.AddComponent<SphereCollider>().radius = .2f;
            Physics.SyncTransforms();
            return target;
        }

        private void AdvanceSkill(PlayerAttack owner, double seconds)
        { _clock.GameplayElapsedSeconds += seconds; owner.ServerUpdateState(NetworkTime.time); }

        [Test]
        public void BowSlideCapsuleStopsAtWallAndPauseStopsVelocity()
        {
            var bow = Owner(WeaponKind.Bow);
            var body = bow.gameObject.AddComponent<Rigidbody>(); body.useGravity = false;
            var capsule = bow.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up; capsule.height = 2; capsule.radius = .3f;
            var controller = bow.gameObject.AddComponent<PlayerController>();
            InitializeIdentity(bow.gameObject);
            typeof(PlayerController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            var tick = typeof(PlayerController).GetMethod("ServerProcessMovementTick", BindingFlags.Instance | BindingFlags.NonPublic);
            Set(GameDB.Arrow, "_worldMask", (LayerMask)(1 << 8));
            var wall = new GameObject("Slide wall", typeof(BoxCollider)); _objects.Add(wall);
            wall.layer = 8; wall.transform.position = new Vector3(0, 1, .8f);
            wall.GetComponent<BoxCollider>().size = new Vector3(3, 3, .1f);
            Physics.SyncTransforms();
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up); AdvanceSkill(bow, .21);
            Set(_run, "_pauseReasons", RunPauseReason.BossCinematic);
            tick.Invoke(controller, new object[] { .02f }); Assert.AreEqual(0, body.linearVelocity.z);
            Set(_run, "_pauseReasons", RunPauseReason.None);
            for (int i = 0; i < 18; ++i)
            {
                tick.Invoke(controller, new object[] { .02f });
                body.position += body.linearVelocity * .02f;
                bow.transform.position = body.position;
                Physics.SyncTransforms();
                _clock.GameplayElapsedSeconds += .02;
            }
            Assert.That(body.position.z, Is.GreaterThan(.3f).And.LessThan(.48f));
            Assert.AreEqual(0, body.linearVelocity.z);
            Assert.Greater(bow.WeaponSkillCooldownRemaining, 7);
        }

        [Test]
        public void BowSlideTravelsOnceAndOnlyHitsOneTargetAcrossTicks()
        {
            var bow = Owner(WeaponKind.Bow); var target = SkillTargetAt(2.5f); var second = SkillTargetAt(3.3f);
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up);
            Assert.IsFalse(bow.TryGetWeaponSkillMotion(.02f, out _, out _));
            AdvanceSkill(bow, .20);
            float travelled = 0;
            for (int i = 0; i < 16; ++i)
            {
                if (bow.TryGetWeaponSkillMotion(.02f, out Vector2 direction, out float speed))
                {
                    Assert.AreEqual(Vector2.up, direction);
                    travelled += speed * .02f;
                    bow.transform.position += new Vector3(direction.x, 0, direction.y) * speed * .02f;
                    Physics.SyncTransforms();
                }
                AdvanceSkill(bow, .02);
            }
            Assert.That(travelled, Is.EqualTo(GameDB.Combat.BowSkillSlideDistance).Within(.001f));
            Assert.AreEqual(988, target.Health); Assert.AreEqual(1000, second.Health);
            Assert.IsFalse(bow.TryGetWeaponSkillMotion(.02f, out _, out _));
            Assert.AreEqual(85, bow.GetComponent<PlayerStatus>().Stamina);
        }

        [Test]
        public void BowSlideStopsOnHitAndBlock()
        {
            var bow = Owner(WeaponKind.Bow);
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up); AdvanceSkill(bow, .21);
            Assert.IsTrue(bow.TryGetWeaponSkillMotion(.02f, out _, out _));
            bow.ServerBlockWeaponSkillSlide();
            Assert.IsFalse(bow.TryGetWeaponSkillMotion(.02f, out _, out _));
            bow.ServerInterruptForHit();
            Assert.IsFalse(bow.TryGetWeaponSkillMotion(.02f, out _, out _));
            Assert.Greater(bow.WeaponSkillCooldownRemaining, 7);
        }

        [Test]
        public void MeleeRInstallsOnceAndPrecisionBurstsWithoutReinstalling()
        {
            var bow = Owner(WeaponKind.Bow); var target = SkillTargetAt(1.5f);
            Assert.AreEqual(ActionRejection.None, bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            Assert.AreEqual(100, bow.GetComponent<PlayerStatus>().Stamina);
            Assert.AreEqual(15, bow.ReservedSkillStamina);
            AdvanceSkill(bow, .21);
            Assert.AreEqual(988, target.Health); Assert.AreNotEqual(0u, bow.Wedge.Generation);
            Assert.AreEqual(85, bow.GetComponent<PlayerStatus>().Stamina);
            Assert.AreEqual(0, bow.Wedge.Count);
            Hit(bow, target, 20, true);
            Assert.AreEqual(948, target.Health); Assert.AreEqual(0u, bow.Wedge.Generation);
        }

        [Test]
        public void BowMissPaysAndCannotPlaceAtRange()
        {
            var bow = Owner(WeaponKind.Bow); var target = SkillTargetAt(3);
            Assert.AreEqual(ActionRejection.None, bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            AdvanceSkill(bow, .21);
            Assert.AreEqual(1000, target.Health); Assert.AreEqual(0u, bow.Wedge.Generation);
            Assert.AreEqual(85, bow.GetComponent<PlayerStatus>().Stamina); Assert.Greater(bow.WeaponSkillCooldownRemaining, 7);
        }

        [Test]
        public void HitBeforeCommitReleasesReservationAndAfterCommitKeepsCooldown()
        {
            var bow = Owner(WeaponKind.Bow);
            Assert.AreEqual(ActionRejection.None, bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            Assert.IsFalse(bow.GetComponent<PlayerStatus>().TrySpendStamina(90));
            bow.ServerInterruptForHit();
            Assert.AreEqual(0, bow.ReservedSkillStamina); Assert.AreEqual(100, bow.GetComponent<PlayerStatus>().Stamina);
            Assert.AreEqual(0, bow.WeaponSkillCooldownRemaining);
            Assert.AreEqual(ActionRejection.None, bow.ServerRequestWeaponSkill(2, 7, 2, Vector2.up));
            AdvanceSkill(bow, .21); bow.ServerInterruptForHit();
            Assert.AreEqual(85, bow.GetComponent<PlayerStatus>().Stamina); Assert.Greater(bow.WeaponSkillCooldownRemaining, 7);
        }

        [Test]
        public void BowRollWindowOpensAfterImpactDelayAndCancellationPreservesCooldown()
        {
            var bow = Owner(WeaponKind.Bow);
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up);
            Assert.IsTrue(bow.CanRollCancelWeaponSkill);
            AdvanceSkill(bow, .21); Assert.IsFalse(bow.CanRollCancelWeaponSkill);
            AdvanceSkill(bow, .41); Assert.IsTrue(bow.CanRollCancelWeaponSkill);
            bow.ServerCancelWeaponSkillForRoll();
            Assert.IsFalse(bow.IsWeaponSkillActive); Assert.Greater(bow.WeaponSkillCooldownRemaining, 7);
        }

        [Test]
        public void SkillRejectsBusyStaleDuplicateAndNonfiniteRequests()
        {
            var sword = Owner(WeaponKind.Sword);
            Assert.AreEqual(ActionRejection.State, sword.ServerRequestWeaponSkill(1, 6, 2, Vector2.up));
            Assert.AreEqual(ActionRejection.InvalidInput, sword.ServerRequestWeaponSkill(2, 7, 2, new Vector2(float.NaN, 0)));
            Assert.AreEqual(ActionRejection.None, sword.ServerRequestWeaponSkill(3, 7, 2, Vector2.up));
            Assert.AreEqual(ActionRejection.Duplicate, sword.ServerRequestWeaponSkill(3, 7, 2, Vector2.up));
            Assert.AreEqual(ActionRejection.Busy, sword.ServerRequestWeaponSkill(4, 7, 2, Vector2.up));
            Assert.AreEqual(100, sword.GetComponent<PlayerStatus>().Stamina);
        }

        [Test]
        public void SwordSweepDeduplicatesCollidersAndRejectsBehind()
        {
            var sword = Owner(WeaponKind.Sword); var front = SkillTargetAt(2); var back = SkillTargetAt(-2);
            front.gameObject.AddComponent<BoxCollider>(); Physics.SyncTransforms();
            sword.ServerRequestWeaponSkill(1, 7, 2, Vector2.up); AdvanceSkill(sword, .35);
            Assert.AreEqual(972, front.Health); Assert.AreEqual(1000, back.Health);
        }

        [Test]
        public void WeaponChangeAndDeathKeepCooldownButRunResetClearsIt()
        {
            var bow = Owner(WeaponKind.Bow);
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up); AdvanceSkill(bow, .21);
            typeof(PlayerAttack).GetMethod("HandleWeaponChanged", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(bow, new object[] { WeaponKind.Sword });
            Assert.Greater(bow.SkillCooldownRemaining(WeaponKind.Bow), 7);
            Set(bow.GetComponent<PlayerStatus>(), "_lifeState", PlayerLifeState.Downed);
            AdvanceSkill(bow, .1); Assert.Greater(bow.SkillCooldownRemaining(WeaponKind.Bow), 7);
            bow.ResetForNewRun(); Assert.AreEqual(0, bow.SkillCooldownRemaining(WeaponKind.Bow));
        }

        [Test]
        public void SkillPauseDefersCommitAndWallBlocksMeleeInstallation()
        {
            var bow = Owner(WeaponKind.Bow); var target = SkillTargetAt(1.5f);
            bow.ServerRequestWeaponSkill(1, 7, 2, Vector2.up);
            Set(_run, "_pauseReasons", RunPauseReason.BossCinematic);
            AdvanceSkill(bow, .21); Assert.AreEqual(1000, target.Health); Assert.AreEqual(100, bow.GetComponent<PlayerStatus>().Stamina);
            Set(_run, "_pauseReasons", RunPauseReason.None);
            Set(GameDB.Arrow, "_worldMask", (LayerMask)(1 << 8));
            var wall = new GameObject("Skill occlusion", typeof(BoxCollider)); _objects.Add(wall);
            wall.layer = 8; wall.transform.position = new Vector3(0, 1.2f, .7f);
            wall.GetComponent<BoxCollider>().size = new Vector3(3, 3, .1f); Physics.SyncTransforms();
            AdvanceSkill(bow, .01);
            Assert.AreEqual(1000, target.Health); Assert.AreEqual(0u, bow.Wedge.Generation);
            Assert.AreEqual(85, bow.GetComponent<PlayerStatus>().Stamina);
        }

        [Test]
        public void NormalEnemySkillStaggerInterruptsWindupAndPreservesKnockback()
        {
            var status = Create<Geumeum.AI.EnemyStatus>();
            Set(status, "_staggerDuration", .35f);
            var brain = new Geumeum.AI.HeokkaebiBrain(status.transform, status);
            Set(brain, "_state", Geumeum.AI.HeokkaebiBrain.BrainState.Windup);
            Assert.IsTrue(brain.TryEnterSkillStagger(.35f));
            Assert.IsFalse(brain.IsAttackCommitted); Assert.IsFalse(brain.ShouldAttackNow);
            Assert.AreEqual(EnemyVisualState.Stagger, brain.VisualState);
            brain.EnterKnockback();
            Assert.IsFalse(brain.TryEnterSkillStagger(.35f));
            Assert.AreEqual(EnemyVisualState.Knockback, brain.VisualState);
        }

        private void SkillGround()
        {
            Set(GameDB.Arrow, "_worldMask", (LayerMask)(1 << 8));
            var ground = new GameObject("Skill ground", typeof(BoxCollider)); _objects.Add(ground);
            ground.layer = 8; ground.transform.position = new Vector3(0, -.5f, 0);
            ground.GetComponent<BoxCollider>().size = new Vector3(30, 1, 30);
            Physics.SyncTransforms();
        }

        /// <summary> 설치 좌표는 사람도 봇도 같은 서버 검증을 지난다 </summary>
        private ActionRejection PlaceField(PlayerAttack bell, uint request, float z = 4f)
        {
            Vector3 point = new Vector3(0, 0, z);
            return bell.ServerRequestWeaponSkill(request, 7, 2, Vector2.up, point);
        }

        [Test]
        public void BellPlacesOneWeakHitAndNoSecondPulseOrEcho()
        {
            SkillGround(); var bell = Owner(WeaponKind.Bell); var inside = SkillTargetAt(4);
            Assert.AreEqual(ActionRejection.None, PlaceField(bell, 1));
            Assert.AreEqual(15, bell.ReservedSkillSpirit, "생성 전에는 예약만 잡는다");

            AdvanceSkill(bell, .26);
            // 20 공격력 x 설치 타격 0.3
            Assert.AreEqual(994, inside.Health);
            Assert.AreEqual(85, bell.GetComponent<PlayerStatus>().Spirit);
            Assert.AreEqual(0u, bell.EchoFirst.Generation, "R 설치 타격은 잔향을 만들지 않는다");
            Assert.AreEqual(0u, bell.EchoSecond.Generation);
            Assert.Greater(bell.BellFieldEnd, 0);

            bell.ServerInterruptForHit(); // 일반 피격은 이미 깔린 진을 없애지 않는다
            Assert.Greater(bell.BellFieldEnd, 0);

            // 두 번째 울림이 없어야 한다. 진이 살아 있는 동안 추가 피해가 들어오지 않는다
            AdvanceSkill(bell, 1.0);
            Assert.AreEqual(994, inside.Health);
            Assert.Greater(bell.WeaponSkillCooldownRemaining, 10);
        }

        [Test]
        public void BellRejectsFarAndBlockedGroundWithoutCost()
        {
            var bell = Owner(WeaponKind.Bell);
            // 지면 자체가 없으면 설치할 수 없다
            Assert.AreEqual(ActionRejection.InvalidInput, PlaceField(bell, 1));
            Assert.AreEqual(100, bell.GetComponent<PlayerStatus>().Spirit);

            SkillGround();
            // 설치 거리 5m를 넘는 좌표는 거부한다
            Assert.AreEqual(ActionRejection.InvalidInput, PlaceField(bell, 2, 7f));
            Assert.AreEqual(100, bell.GetComponent<PlayerStatus>().Spirit);
            Assert.AreEqual(0, bell.WeaponSkillCooldownRemaining);

            // 사이를 막으면 시야가 끊겨 같은 좌표도 거부된다
            var wall = new GameObject("Field occluder", typeof(BoxCollider)); _objects.Add(wall);
            wall.layer = 8; wall.transform.position = new Vector3(0, 1.2f, 2f);
            wall.GetComponent<BoxCollider>().size = new Vector3(6, 3, .1f); Physics.SyncTransforms();
            Assert.AreEqual(ActionRejection.InvalidInput, PlaceField(bell, 3));
            Assert.AreEqual(100, bell.GetComponent<PlayerStatus>().Spirit);
        }

        [Test]
        public void FieldBuffsStayersRefreshesInsideAndKeepsTailAfterLeaving()
        {
            SkillGround(); var bell = Owner(WeaponKind.Bell); var ally = Owner(WeaponKind.Sword);
            ally.transform.position = new Vector3(0, 0, 4); Physics.SyncTransforms();
            var allyBuff = ally.GetComponent<SupportBuffController>();

            Assert.AreEqual(ActionRejection.None, PlaceField(bell, 1));
            AdvanceSkill(bell, .26);
            Assert.AreEqual(1.1f, allyBuff.ActiveDamageMultiplier, .0001f, "생성 즉시 판정한다");

            // 진 안에 머무는 동안은 만료가 계속 밀린다
            for (int i = 0; i < 20; ++i) { AdvanceSkill(bell, .11); }
            Assert.AreEqual(1.1f, allyBuff.ActiveDamageMultiplier, .0001f);

            // 나가면 마지막 유효 체류부터 1.5초만 남는다
            ally.transform.position = new Vector3(0, 0, 40); Physics.SyncTransforms();
            AdvanceSkill(bell, .11);
            double expiry = allyBuff.ActiveExpiryGameplayTime;
            AdvanceSkill(bell, 1.0);
            PumpBuff(allyBuff);
            Assert.AreEqual(1.1f, allyBuff.ActiveDamageMultiplier, .0001f, "이탈 직후에는 아직 남는다");
            _clock.GameplayElapsedSeconds = expiry + .01;
            PumpBuff(allyBuff);
            Assert.AreEqual(1f, allyBuff.ActiveDamageMultiplier, .0001f);
        }

        [Test]
        public void OverlappingFieldsDoNotStackAndForcedDiscardTakesTailBack()
        {
            SkillGround();
            var first = Owner(WeaponKind.Bell); var second = Owner(WeaponKind.Bell);
            var ally = Owner(WeaponKind.Sword);
            ally.transform.position = new Vector3(0, 0, 4); Physics.SyncTransforms();
            var allyBuff = ally.GetComponent<SupportBuffController>();

            Assert.AreEqual(ActionRejection.None, PlaceField(first, 1));
            Assert.AreEqual(ActionRejection.None, PlaceField(second, 1));
            AdvanceSkill(first, .26); AdvanceSkill(second, .26);
            Assert.AreEqual(1.1f, allyBuff.ActiveDamageMultiplier, .0001f,
                "겹친 진으로 계수를 누적하지 않는다");

            // 시전자 사망은 그 원천의 남은 버프까지 즉시 회수한다
            Set(first.GetComponent<PlayerStatus>(), "_lifeState", PlayerLifeState.Downed);
            AdvanceSkill(first, .01);
            Assert.AreEqual(1.1f, allyBuff.ActiveDamageMultiplier, .0001f,
                "다른 진이 아직 유효하면 계속된다");

            Set(second.GetComponent<PlayerStatus>(), "_lifeState", PlayerLifeState.Downed);
            AdvanceSkill(second, .01);
            PumpBuff(allyBuff);
            Assert.AreEqual(1f, allyBuff.ActiveDamageMultiplier, .0001f);
        }

        [Test]
        public void ResonanceIsSnapshotAtCostCommitAndNotReappliedLater()
        {
            SkillGround();
            var bell = Owner(WeaponKind.Bell); var sword = Owner(WeaponKind.Sword);
            // 설치 타격 반경 밖에 세워야 검 R의 피해만 따로 읽을 수 있다
            sword.transform.position = new Vector3(0, 0, 6.5f);
            var target = SkillTargetAt(8);
            Physics.SyncTransforms();

            Assert.AreEqual(ActionRejection.None, PlaceField(bell, 1));
            AdvanceSkill(bell, .26);
            Assert.AreEqual(1.1f, sword.GetComponent<SupportBuffController>().ActiveDamageMultiplier, .0001f);

            // 검 R: 20 x 1.4 x 공명진 1.1 = 30.8
            Assert.AreEqual(ActionRejection.None, sword.ServerRequestWeaponSkill(1, 7, 2, Vector2.up));
            AdvanceSkill(sword, .35);
            Assert.AreEqual(1000f - 30.8f, target.Health, .01f);
        }

        [Test]
        public void NaturalExpiryKeepsTailButCasterDeathStillRevokesIt()
        {
            SkillGround(); var bell = Owner(WeaponKind.Bell); var ally = Owner(WeaponKind.Sword);
            ally.transform.position = new Vector3(0, 0, 4);
            Assert.AreEqual(ActionRejection.None, PlaceField(bell, 1));
            AdvanceSkill(bell, .26);
            double end = bell.BellFieldEnd;
            _clock.GameplayElapsedSeconds = end - .05;
            bell.ServerUpdateState(NetworkTime.time);
            _clock.GameplayElapsedSeconds = end + .01;
            bell.ServerUpdateState(NetworkTime.time);
            Assert.IsFalse(bell.HasActiveField);
            Assert.IsTrue(bell.HasGrantedFieldEffects);
            Assert.IsTrue(bell.BlocksTalentMutation);
            var buff = ally.GetComponent<SupportBuffController>();
            Assert.AreEqual(1.1f, buff.ActiveDamageMultiplier, .0001f);
            Set(bell.GetComponent<PlayerStatus>(), "_lifeState", PlayerLifeState.Downed);
            bell.ServerUpdateState(NetworkTime.time);
            Assert.AreEqual(1f, buff.ActiveDamageMultiplier);
            Assert.IsFalse(bell.HasGrantedFieldEffects);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BasicAttackSamplesFieldAtImpactInsteadOfStartup(bool expiresDuringStartup)
        {
            var bell = Owner(WeaponKind.Bell); var target = SkillTargetAt(1);
            var presentation = bell.gameObject.AddComponent<PlayerPresentation>();
            InitializeIdentity(bell.gameObject);
            Set(bell, "_presentation", presentation);
            var buff = bell.GetComponent<SupportBuffController>();
            var source = new ResonanceFieldSource { Caster = bell.GetComponent<PlayerStatus>().Participant, FieldId = 123 };
            if (expiresDuringStartup) { buff.ServerApplyResonance(source, 1.1f, 10.1); }
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestAttack(1, Vector2.up, default));
            if (!expiresDuringStartup) { buff.ServerApplyResonance(source, 1.1f, 12); }
            _clock.GameplayElapsedSeconds = 10.3;
            bell.ServerUpdateState(NetworkTime.time + .5);
            Assert.AreEqual(1000f - (expiresDuringStartup ? 20f : 22f), target.Health, .01f);
        }

        [Test]
        public void PlacementModalRestoresOriginalCameraStateAndConsumesCancelFrame()
        {
            bool previous = CameraFrame.InputEnabled;
            var cursor = Cursor.lockState; bool visible = Cursor.visible;
            try
            {
                CameraFrame.InputEnabled = true;
                LocalPlacementAim.Begin();
                Assert.IsFalse(CameraFrame.InputEnabled);
                using (LocalGameplayInputGate.Acquire())
                {
                    Assert.IsFalse(LocalPlacementAim.IsActive);
                    Assert.IsTrue(LocalPlacementAim.SuppressesCombatInput);
                    Assert.IsFalse(CameraFrame.InputEnabled);
                }
                Assert.IsTrue(CameraFrame.InputEnabled);
                Assert.IsTrue(LocalPlacementAim.ConsumeSettleFrame());
                Assert.IsFalse(LocalPlacementAim.ConsumeSettleFrame());
            }
            finally
            {
                LocalPlacementAim.End(); CameraFrame.InputEnabled = previous;
                Cursor.lockState = cursor; Cursor.visible = visible;
            }
        }
    }
}
