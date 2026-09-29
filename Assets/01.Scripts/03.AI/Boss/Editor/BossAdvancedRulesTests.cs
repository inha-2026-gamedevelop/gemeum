// System
using System;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary> Multi-impact, tail gate, and final-soul invariants that must survive prefab wiring </summary>
    public sealed class BossAdvancedRulesTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;

        private GameDatabaseSO _database;
        private BossPatternSO  _databaseBoss;
        private CombatConfigSO _databaseCombat;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            // Phase2.Tick은 공격 상태 기계와 별개로 여우불 진(패턴 15)을 매 틱 확인하고,
            // 그 값만은 주입한 SO가 아니라 GameDB에서 읽는다. 이 픽스처는 가짜 데이터를
            // 직접 넣어 쓰므로 GameDB가 비어 Tick이 예외로 끊겼다.
            // 빈 BossPatternSO를 걸어 두면 TryGetPattern(15)가 false라 지면 장판은 돌지 않는다
            // - 각 테스트가 주입한 가짜 데이터로 검사하던 판정은 그대로다
            _databaseBoss = ScriptableObject.CreateInstance<BossPatternSO>();
            _database     = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_boss", _databaseBoss);
            // 보스 근접 패링 창 길이는 CombatConfigSO 기본값을 읽는다
            _databaseCombat = ScriptableObject.CreateInstance<CombatConfigSO>();
            Set(_database, "_combat", _databaseCombat);
            GameDB.Initialize(_database);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);

            // 초기화된 GameDB를 다른 픽스처로 흘리지 않는다
            GameDB.ResetStatic();
            if (_database != null) { UnityEngine.Object.DestroyImmediate(_database); }
            if (_databaseBoss != null) { UnityEngine.Object.DestroyImmediate(_databaseBoss); }
            if (_databaseCombat != null) { UnityEngine.Object.DestroyImmediate(_databaseCombat); }
        }

        [Test]
        public void ExecutionSpecClampsEightImpactsAndParryRepeat()
        {
            var pattern = new BossPatternSO.PatternEntry
            {
                PatternId = 9,
                Template = BossPatternSO.PatternTemplate.ConeBurst,
                TelegraphDuration = 1f,
                ImpactOffsets = new[] { 1f, 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.2f, 2.4f, 2.6f },
                RepeatTimingJitterMin = 0.1f,
                RepeatTimingJitterMax = 0.2f,
                IsParryable = true,
                ParryableRepeatIndex = 2,
            };

            Type specType = typeof(BossController).Assembly.GetType(
                "Geumeum.AI.BossPatternExecutionSpec", true);
            ConstructorInfo constructor = specType.GetConstructors(INSTANCE)[0];
            object spec = constructor.Invoke(new object[] { pattern });

            Assert.AreEqual(8, specType.GetProperty("RepeatCount").GetValue(spec));
            Assert.IsFalse((bool) Invoke(spec, "IsRepeatParryable", pattern, 1));
            Assert.IsTrue((bool) Invoke(spec, "IsRepeatParryable", pattern, 2));
            Assert.AreEqual(2.4f, (float) Invoke(spec, "GetImpactOffset", pattern, 7));

            float[] schedule = new float[8];
            Invoke(spec, "BuildImpactSchedule", pattern, new System.Random(7), schedule);
            Assert.AreEqual(1f, schedule[0], "the first telegraph duration never jitters");
            for (int i = 1; i < schedule.Length; ++i)
            {
                Assert.That(schedule[i] - schedule[i - 1], Is.InRange(0.3f, 0.4f));
            }
        }

        [Test]
        public void TelegraphsUseOneAttackIdAndIndependentRepeatKeys()
        {
            GameObject go = new GameObject("BossTelegraphRules", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController));
            try
            {
                InitializeNetworkBehaviours(go);
                BossController controller = go.GetComponent<BossController>();

                for (byte repeat = 0; repeat < 8; ++repeat)
                {
                    Invoke(controller, "ServerBeginTelegraph", new BossTelegraphCue
                    {
                        AttackInstanceId = 41u,
                        RepeatIndex = repeat,
                        PatternId = 9,
                        TargetNetId = (uint) (100 + repeat),
                        StartServerTime = 10d + repeat,
                        ImpactServerTime = 11d + repeat,
                    });
                }

                Assert.AreEqual(8, controller.TelegraphCues.Count);
                for (int i = 0; i < controller.TelegraphCues.Count; ++i)
                {
                    Assert.AreEqual(41u, controller.TelegraphCues[i].AttackInstanceId);
                    Assert.AreEqual((byte) i, controller.TelegraphCues[i].RepeatIndex);
                }

                Invoke(controller, "ServerCancelTelegraph", 41u, (byte) 3, 103u);
                Assert.AreEqual(7, controller.TelegraphCues.Count);
                Invoke(controller, "ServerCancelTelegraph", 41u);
                Assert.AreEqual(0, controller.TelegraphCues.Count);
                Assert.IsFalse(controller.TelegraphCue.IsActive);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BodyHealthFloorUsesOnlyActuallyAppliedDamage()
        {
            GameObject go = new GameObject("BossHealthFloor", typeof(NetworkIdentity),
                typeof(BossStatus));
            try
            {
                InitializeNetworkBehaviours(go);
                BossStatus status = go.GetComponent<BossStatus>();
                Set(status, "_maxHealth", 100f);
                Set(status, "_health", 100f);
                Invoke(status, "ServerSetHealthFloorRatio", 0.25f);

                status.TakeDamage(new DamageInfo(90f, 1u, default));
                Assert.AreEqual(25f, status.Health);
                status.TakeDamage(new DamageInfo(10f, 1u, default));
                Assert.AreEqual(25f, status.Health);

                Invoke(status, "ServerSetHealthFloorRatio", 0f);
                status.TakeDamage(new DamageInfo(30f, 1u, default));
                Assert.AreEqual(0f, status.Health);
                Assert.IsTrue(status.IsDead);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TailBreakConsumesOneGroggyAndDoesNotCarryOverkill()
        {
            BossPatternSO data = CreateBossData();
            Set(data, "_tailSegmentHealth", 100f);
            Set(data, "_groggyTailDamageMultiplier", 3f);
            Set(data, "_partPatternSeals", new[]
            {
                new BossPatternSO.PartPatternSealEntry { PartId = 1, PatternId = 6 },
                new BossPatternSO.PartPatternSealEntry { PartId = 2, PatternId = 8 },
            });
            GameObject go = new GameObject("BossPartRules", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossPartController));
            try
            {
                InitializeNetworkBehaviours(go);
                BossPartController parts = go.GetComponent<BossPartController>();
                Set(parts, "_boss", null);
                Invoke(parts, "ServerInitialize", data);
                Invoke(parts, "ServerBeginGroggy", 1u);

                float applied = (float) Invoke(parts, "ServerApplyDamage", (byte) 1,
                    new DamageInfo(100f, 7u, new ParticipantId(70UL), default), Vector3.zero);
                Assert.AreEqual(100f, applied, "300 incoming is capped to the part's actual 100 HP");
                Assert.IsTrue(parts.PartSnapshot.IsBroken(1));
                Assert.IsTrue(parts.PartSnapshot.BrokePartThisGeneration);
                Assert.IsTrue((bool) Invoke(parts, "ServerIsPatternSealed", 6));
                Assert.IsTrue((bool) Invoke(parts, "ServerIsTransferredCounterPattern",
                    CharacterKind.Chasa, 7));
                Assert.IsTrue((bool) Invoke(parts, "ServerIsTransferredParryRepeat", 7, 0));
                Assert.IsFalse((bool) Invoke(parts, "ServerIsTransferredParryRepeat", 7, 1));

                float carried = (float) Invoke(parts, "ServerApplyDamage", (byte) 2,
                    new DamageInfo(100f, 7u, new ParticipantId(70UL), default), Vector3.zero);
                Assert.AreEqual(0f, carried);
                Assert.AreEqual(100f, parts.PartSnapshot.Part2Health);

                Invoke(parts, "ServerBeginGroggy", 2u);
                Assert.AreEqual(2, parts.PartSnapshot.ActivePartId);
                Invoke(parts, "ServerApplyDamage", (byte) 2,
                    new DamageInfo(100f, 7u, new ParticipantId(70UL), default), Vector3.zero);
                Assert.IsTrue((bool) Invoke(parts, "ServerIsPatternSealed", 8));
                Assert.IsTrue((bool) Invoke(parts, "ServerIsTransferredCounterPattern",
                    CharacterKind.Munyeo, 9));
                Assert.IsTrue(data.IsCounterPattern(CharacterKind.Singung, 9),
                    "Singung keeps its original pattern 9 counter until the final tail transition");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FailureAttackPreservesSoulProgressUntilAttackEnds()
        {
            BossPatternSO data = CreateBossData();
            Set(data, "_wonhonHealth", 100f);
            Set(data, "_finalGauntletWindowSeconds", 8f);
            GameObject go = new GameObject("BossSoulRules", typeof(NetworkIdentity),
                typeof(BossSoulController));
            try
            {
                InitializeNetworkBehaviours(go);
                BossSoulController souls = go.GetComponent<BossSoulController>();
                Set(souls, "_boss", null);
                Invoke(souls, "ServerInitialize", data);
                Invoke(souls, "ServerBeginFinal", 10d);
                DamageInfo damage = new DamageInfo(100f, 8u, new ParticipantId(80UL), default);

                Assert.AreEqual(100f, (float) Invoke(souls, "ServerApplyDamage", (byte) 1,
                    damage));
                Invoke(souls, "ServerTick", 18d);
                Assert.AreEqual(BossFinalState.FailureAttack, souls.SoulSnapshot.State);
                Assert.AreEqual(2, souls.SoulSnapshot.FailureBranchCount);

                Invoke(souls, "ServerApplyDamage", (byte) 2, damage);
                Invoke(souls, "ServerApplyDamage", (byte) 3, damage);
                Assert.AreEqual(BossFinalState.FailureAttack, souls.SoulSnapshot.State,
                    "D10 keeps an already accepted Hundred Ghosts execution alive");
                Assert.AreEqual(0, souls.SoulSnapshot.RemainingCount);

                Invoke(souls, "ServerNotifyFailureAttackEnded");
                Assert.AreEqual(BossFinalState.Killable, souls.SoulSnapshot.State);
                Assert.IsTrue(souls.SoulSnapshot.IsRemoved(1));
                Assert.IsTrue(souls.SoulSnapshot.IsRemoved(2));
                Assert.IsTrue(souls.SoulSnapshot.IsRemoved(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FinalSoulSequenceCannotBeOverwrittenByPendingGroggyWake()
        {
            BossPatternSO data = CreateBossData();
            Set(data, "_tailSegmentHealth", 100f);
            Set(data, "_wonhonHealth", 100f);
            Set(data, "_finalGauntletWindowSeconds", 8f);
            GameObject go = new GameObject("FinalGroggyBoundaryBoss",
                typeof(NetworkIdentity), typeof(BossStatus), typeof(BossController),
                typeof(BossPartController), typeof(BossSoulController));
            try
            {
                InitializeNetworkBehaviours(go);
                BossStatus status = go.GetComponent<BossStatus>();
                BossController controller = go.GetComponent<BossController>();
                BossPartController parts = go.GetComponent<BossPartController>();
                BossSoulController souls = go.GetComponent<BossSoulController>();
                var phase = new ForcedAttackStateStub(go.transform, status);
                Set(controller, "_status", status);
                Set(controller, "_parts", parts);
                Set(controller, "_souls", souls);
                Set(controller, "_bossData", data);
                Set(controller, "_currentPhaseIndex", 2);
                Set(controller, "_currentPhaseState", phase);
                Set(parts, "_boss", controller);
                Set(parts, "_status", status);
                Set(souls, "_boss", controller);
                Invoke(parts, "ServerInitialize", data);
                Invoke(souls, "ServerInitialize", data);

                BossPartSnapshot beforeFinal = parts.PartSnapshot;
                beforeFinal.Part1Health = 0f;
                beforeFinal.Part2Health = 0f;
                beforeFinal.BrokenMask = 0b0000_0011;
                beforeFinal.ActivePartId = 0;
                beforeFinal.BrokePartThisGeneration = false;
                Set(parts, "_snapshot", beforeFinal);

                Invoke(controller, "EnterGroggy");
                Assert.AreEqual((byte)3, parts.PartSnapshot.ActivePartId);
                Invoke(parts, "ServerApplyDamage", (byte)3,
                    new DamageInfo(100f, 70u, new ParticipantId(70UL), default),
                    Vector3.zero);

                Assert.AreEqual(BossFinalState.SoulWindow, souls.SoulSnapshot.State);
                Assert.AreEqual(BossEncounterStage.FinalSoulWindow,
                    controller.PhaseSnapshot.Stage);
                Assert.AreEqual("Active", Get(controller, "_groggyPhase").ToString());
                Assert.AreEqual(1f, Get(status, "_bodyDamageMultiplier"));

                Invoke(controller, "TickGroggySystem", data.GroggyDuration * 2f);
                Assert.AreEqual(BossEncounterStage.FinalSoulWindow,
                    controller.PhaseSnapshot.Stage);

                Invoke(souls, "ServerTick",
                    souls.SoulSnapshot.WindowDeadlineGameplayTime);
                Assert.AreEqual(BossFinalState.FailureAttack,
                    souls.SoulSnapshot.State);
                Assert.AreEqual(BossEncounterStage.FinalFailureAttack,
                    controller.PhaseSnapshot.Stage);
                Assert.AreEqual(1, phase.ForcedAttackCount);

                Invoke(controller, "TickGroggySystem", data.GroggyDuration * 2f);
                Assert.AreEqual(BossEncounterStage.FinalFailureAttack,
                    controller.PhaseSnapshot.Stage);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void TransferredChasaWindowParriesPatternSevenAndCancelsWholeAttack()
        {
            BossPatternSO data = CreateBossData();
            GameObject boss = new GameObject("TransferredChasaBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController), typeof(BossPartController));
            GameObject target = new GameObject("TransferredChasaTarget");
            try
            {
                InitializeNetworkBehaviours(boss);
                SetNetId(boss.GetComponent<NetworkIdentity>(), 701u);
                BossStatus status = boss.GetComponent<BossStatus>();
                BossController controller = boss.GetComponent<BossController>();
                BossPartController parts = boss.GetComponent<BossPartController>();
                PrepareBoss(controller, status, parts, data, 1);

                BossParryWindowCapture capture = target.AddComponent<BossParryWindowCapture>();
                Phase2State phase = new Phase2State(boss.transform, status, controller)
                {
                    Target = target.transform,
                };
                Set(controller, "_currentPhaseState", phase);
                BeginPhase2Attack(phase, Pattern(7,
                    BossPatternSO.PatternTemplate.TargetedDash, 1.2f));

                Assert.IsNotNull(capture.Source,
                    "tail 1 transfers Chasa's sealed pattern 6 window to pattern 7 repeat 0");
                Assert.AreEqual((byte) 0, capture.RepeatIndex);
                Assert.AreEqual(1, controller.TelegraphCues.Count);

                capture.Source.OnParried(77u);

                Assert.AreEqual(90f, Get(controller, "_groggyGauge"));
                Assert.AreEqual(0u, Get(phase, "_activeAttackInstanceId"));
                Assert.AreEqual(0, controller.TelegraphCues.Count);
                Assert.AreEqual(1, capture.CloseCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(boss);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void PatternNinePrecisionCancelDoesNotAlsoGrantTransferredResonance()
        {
            BossPatternSO data = CreateBossData();
            GameObject boss = new GameObject("CoexistingCounterBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController), typeof(BossPartController));
            GameObject target = new GameObject("CoexistingCounterTarget");
            try
            {
                InitializeNetworkBehaviours(boss);
                SetNetId(boss.GetComponent<NetworkIdentity>(), 709u);
                BossStatus status = boss.GetComponent<BossStatus>();
                BossController controller = boss.GetComponent<BossController>();
                BossPartController parts = boss.GetComponent<BossPartController>();
                PrepareBoss(controller, status, parts, data, 2);
                MarkBody(controller, new ParticipantId(91UL));

                Phase2State phase = new Phase2State(boss.transform, status, controller)
                {
                    Target = target.transform,
                };
                Set(controller, "_currentPhaseState", phase);
                BeginPhase2Attack(phase, Pattern(9,
                    BossPatternSO.PatternTemplate.ConeBurst, 1.5f, 2f, 2.5f));

                PrecisionInterruptContext precision = new PrecisionInterruptContext(
                    9001u,
                    new ParticipantId(92UL),
                    92u,
                    (TargetRef) Invoke(controller, "CreateBodyTargetRef"),
                    true,
                    NetworkTime.time);
                Assert.IsTrue(controller.TryInterruptPrecisionShot(precision));
                phase.Tick(20f);

                Assert.AreEqual(92f, Get(controller, "_groggyGauge"),
                    "Singung reduces 8; canceled pattern 9 must never finish for Munyeo's 13");
                Assert.AreEqual(0, controller.TelegraphCues.Count);
            }
            finally
            {
                MarkRegistry.ClearAll();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(boss);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void PatternNineLastImpactResonatesOnceAndRefundsEveryMarkedOwner()
        {
            BossPatternSO data = CreateBossData();
            GameObject boss = new GameObject("TransferredResonanceBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController), typeof(BossPartController));
            GameObject target = new GameObject("TransferredResonanceTarget");
            GameObject ownerA = CreateResourceOwner(921u, new ParticipantId(921UL),
                out BossResourceTargetStub resourceA);
            GameObject ownerB = CreateResourceOwner(922u, new ParticipantId(922UL),
                out BossResourceTargetStub resourceB);
            try
            {
                InitializeNetworkBehaviours(boss);
                SetNetId(boss.GetComponent<NetworkIdentity>(), 719u);
                BossStatus status = boss.GetComponent<BossStatus>();
                BossController controller = boss.GetComponent<BossController>();
                BossPartController parts = boss.GetComponent<BossPartController>();
                PrepareBoss(controller, status, parts, data, 2);
                MarkBody(controller, resourceA.Participant);
                MarkBody(controller, resourceB.Participant);

                Phase2State phase = new Phase2State(boss.transform, status, controller)
                {
                    Target = target.transform,
                };
                Set(controller, "_currentPhaseState", phase);
                BeginPhase2Attack(phase, Pattern(9,
                    BossPatternSO.PatternTemplate.ConeBurst, 1.5f, 2f, 2.5f));

                uint attackId = (uint) Get(phase, "_activeAttackInstanceId");
                phase.Tick(10f);
                Assert.AreEqual(100f, Get(controller, "_groggyGauge"),
                    "the first impact is not pattern completion");
                phase.Tick(0f);
                Assert.AreEqual(100f, Get(controller, "_groggyGauge"),
                    "the second impact is not pattern completion");
                phase.Tick(0f);

                Assert.AreEqual(87f, Get(controller, "_groggyGauge"));
                Assert.AreEqual(15f, resourceA.Spirit);
                Assert.AreEqual(15f, resourceB.Spirit);
                Assert.AreEqual(1, resourceA.PublishCount);
                Assert.AreEqual(1, resourceB.PublishCount);

                Invoke(controller, "ServerNotifyPatternEnded", 9, attackId);
                Assert.AreEqual(87f, Get(controller, "_groggyGauge"));
                Assert.AreEqual(1, resourceA.PublishCount);
                Assert.AreEqual(1, resourceB.PublishCount);
            }
            finally
            {
                NetworkServer.spawned.Remove(921u);
                NetworkServer.spawned.Remove(922u);
                MarkRegistry.ClearAll();
                UnityEngine.Object.DestroyImmediate(ownerA);
                UnityEngine.Object.DestroyImmediate(ownerB);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(boss);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void AimResolverUsesStablePartIdInsteadOfSiblingComponentOrder()
        {
            GameObject root = new GameObject("AimResolverRoot");
            GameObject body = new GameObject("BodyAim");
            GameObject part = new GameObject("TailAim");
            try
            {
                body.transform.SetParent(root.transform);
                part.transform.SetParent(root.transform);
                BossLegacyAimStub bodyAim = body.AddComponent<BossLegacyAimStub>();
                part.AddComponent<BossLegacyAimStub>();
                BossFixedAimStub fixedAim = part.AddComponent<BossFixedAimStub>();
                fixedAim.PartId = BossTargetPartIds.Tail2;
                fixedAim.Targetable = true;

                Assert.IsTrue(AimAssistTargetResolver.TryResolve(root.transform,
                    TargetRef.BODY_PART_ID, out IAimAssistTarget resolvedBody));
                Assert.AreSame(bodyAim, resolvedBody);
                Assert.IsTrue(AimAssistTargetResolver.TryResolve(root.transform,
                    BossTargetPartIds.Tail2, out IAimAssistTarget resolvedPart));
                Assert.AreSame(fixedAim, resolvedPart);
                Assert.AreEqual(BossTargetPartIds.Tail2,
                    AimAssistTargetResolver.ResolvePartId(resolvedPart));

                fixedAim.Targetable = false;
                Assert.IsFalse(AimAssistTargetResolver.TryResolve(root.transform,
                    BossTargetPartIds.Tail2, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseObserverFailureCannotBlockCommittedSnapshotOrTrailingObserver()
        {
            GameObject boss = new GameObject("PhaseObserverBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController));
            try
            {
                InitializeNetworkBehaviours(boss);
                BossController controller = boss.GetComponent<BossController>();
                int trailingCalls = 0;
                controller.PhaseChanged += _ => throw new InvalidOperationException("observer test");
                controller.PhaseChanged += _ => ++trailingCalls;
                Set(controller, "_currentPhaseIndex", 2);
                LogAssert.Expect(LogType.Error,
                    new Regex("\\[BossController\\] PhaseChanged observer failed"));

                Invoke(controller, "CommitPhaseSnapshot", BossEncounterStage.Fighting);

                Assert.AreEqual(2, controller.PhaseSnapshot.PhaseIndex);
                Assert.AreEqual(1u, controller.PhaseSnapshot.Revision);
                Assert.AreEqual(1, trailingCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void PhaseGameplayTimestampChangesOnlyWithPhaseIndex()
        {
            GameObject boss = new GameObject("PhaseTimestampBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController));
            IRunTimeSource previousTime = RunTimeLocator.Current;
            var time = new MutableRunTimeSource { GameplayElapsedSeconds = 40d };
            try
            {
                RunTimeLocator.Register(time);
                InitializeNetworkBehaviours(boss);
                BossController controller = boss.GetComponent<BossController>();
                Set(controller, "_currentPhaseIndex", 1);
                Invoke(controller, "CommitPhaseSnapshot", BossEncounterStage.Fighting);
                Assert.AreEqual(40d,
                    controller.PhaseSnapshot.PhaseChangedGameplayTime);

                time.GameplayElapsedSeconds = 45d;
                Invoke(controller, "CommitPhaseSnapshot", BossEncounterStage.Groggy);
                Assert.AreEqual(40d,
                    controller.PhaseSnapshot.PhaseChangedGameplayTime,
                    "stage changes must not restart the arena transition");

                time.GameplayElapsedSeconds = 51d;
                Set(controller, "_currentPhaseIndex", 2);
                Invoke(controller, "CommitPhaseSnapshot", BossEncounterStage.Fighting);
                Assert.AreEqual(51d,
                    controller.PhaseSnapshot.PhaseChangedGameplayTime);
            }
            finally
            {
                RunTimeLocator.Register(previousTime);
                UnityEngine.Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void HostPhaseCommitPublishesExactlyOnceThroughSyncVarHook()
        {
            GameObject boss = new GameObject("HostPhaseObserverBoss",
                typeof(NetworkIdentity), typeof(BossStatus), typeof(BossController));
            LocalConnectionToClient previous = NetworkServer.localConnection;
            try
            {
                InitializeNetworkBehaviours(boss);
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.localConnection))
                    .SetValue(null, new LocalConnectionToClient());
                BossController controller = boss.GetComponent<BossController>();
                int calls = 0;
                controller.PhaseChanged += _ => ++calls;

                Invoke(controller, "CommitPhaseSnapshot", BossEncounterStage.Groggy);

                Assert.AreEqual(BossEncounterStage.Groggy,
                    controller.PhaseSnapshot.Stage);
                Assert.AreEqual(1, calls);
            }
            finally
            {
                typeof(NetworkServer).GetProperty(nameof(NetworkServer.localConnection))
                    .SetValue(null, previous);
                UnityEngine.Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void PhaseSourceLocatorTracksBossClientLifetimeAndContinuesAfterFailure()
        {
            GameObject boss = new GameObject("PhaseLocatorBoss", typeof(NetworkIdentity),
                typeof(BossStatus), typeof(BossController));
            Action<IBossPhaseSource> throwing = _ =>
                throw new InvalidOperationException("locator observer test");
            int trailingCalls = 0;
            Action<IBossPhaseSource> trailing = _ => ++trailingCalls;
            try
            {
                InitializeNetworkBehaviours(boss);
                BossController controller = boss.GetComponent<BossController>();
                BossPhaseSourceLocator.Changed += throwing;
                BossPhaseSourceLocator.Changed += trailing;
                LogAssert.Expect(LogType.Error,
                    new Regex("\\[BossPhaseSourceLocator\\] Changed observer failed"));

                controller.OnStartClient();

                Assert.AreSame(controller, BossPhaseSourceLocator.Current);
                Assert.AreEqual(1, trailingCalls);

                LogAssert.Expect(LogType.Error,
                    new Regex("\\[BossPhaseSourceLocator\\] Changed observer failed"));
                controller.OnStopClient();
                Assert.IsNull(BossPhaseSourceLocator.Current);
                Assert.AreEqual(2, trailingCalls);
            }
            finally
            {
                BossPhaseSourceLocator.Changed -= throwing;
                BossPhaseSourceLocator.Changed -= trailing;
                BossPhaseSourceLocator.Unregister(
                    BossPhaseSourceLocator.Current);
                UnityEngine.Object.DestroyImmediate(boss);
            }
        }

        private static BossPatternSO CreateBossData()
        {
            return ScriptableObject.CreateInstance<BossPatternSO>();
        }

        private sealed class MutableRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0d;
            public double GameplayElapsedSeconds { get; set; }
            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool IsPaused => false;
        }

        private sealed class ForcedAttackStateStub : BossPhaseStateBase
        {
            public int ForcedAttackCount { get; private set; }

            public ForcedAttackStateStub(Transform self, BossStatus status)
                : base(self, status)
            {
            }

            public override void Tick(float deltaTime)
            {
            }

            public override void CancelActiveAttack()
            {
            }

            public override bool IsAttackWindup(uint attackInstanceId)
            {
                return false;
            }

            public override bool TryBeginForcedAttack(int patternId,
                int repeatCountOverride)
            {
                ++ForcedAttackCount;
                return true;
            }
        }

        private static void PrepareBoss(BossController controller, BossStatus status,
            BossPartController parts, BossPatternSO data, int brokenTailCount)
        {
            Set(controller, "_status", status);
            Set(controller, "_bossData", data);
            Set(controller, "_parts", parts);
            Set(controller, "_groggyGauge", 100f);
            Set(status, "_maxHealth", 100f);
            Set(status, "_health", 100f);
            Set(parts, "_boss", null);
            Invoke(parts, "ServerInitialize", data);

            for (byte partId = 1; partId <= brokenTailCount; ++partId)
            {
                Invoke(parts, "ServerBeginGroggy", (uint) partId);
                Invoke(parts, "ServerApplyDamage", partId,
                    new DamageInfo(data.TailSegmentHealth, 70u,
                        new ParticipantId(70UL), default),
                    Vector3.zero);
            }
        }

        private static BossPatternSO.PatternEntry Pattern(int patternId,
            BossPatternSO.PatternTemplate template, params float[] impacts)
        {
            return new BossPatternSO.PatternEntry
            {
                PatternId = patternId,
                TelegraphDuration = impacts[0],
                ActiveDuration = 1f,
                Cooldown = 1f,
                Damage = 1f,
                Template = template,
                Targeting = BossPatternSO.PatternTargeting.PrimaryTarget,
                Range = 10f,
                AngleDegrees = 60f,
                MoveSpeed = 10f,
                MoveDistance = 10f,
                ImpactRadius = 1f,
                ImpactOffsets = impacts,
                ParryableRepeatIndex = -1,
                DerivedFromId = -1,
                SelectionWeight = 1f,
            };
        }

        private static void BeginPhase2Attack(Phase2State phase,
            BossPatternSO.PatternEntry pattern)
        {
            Type kindType = phase.GetType().GetField("_activeKind", INSTANCE).FieldType;
            object kind = Enum.Parse(kindType, pattern.Template.ToString());
            Invoke(phase, "BeginAttack", pattern, kind);
        }

        private static void MarkBody(BossController controller, ParticipantId owner)
        {
            TargetRef target = (TargetRef) Invoke(controller, "CreateBodyTargetRef");
            Assert.IsTrue(MarkRegistry.TryApply(owner, target,
                RunTimeLocator.Current.GameplayElapsedSeconds, 8f, 3,
                out MarkRejection rejection), rejection.ToString());
        }

        private static GameObject CreateResourceOwner(uint netId, ParticipantId participant,
            out BossResourceTargetStub resource)
        {
            GameObject go = new GameObject($"ResourceOwner{netId}", typeof(NetworkIdentity));
            resource = go.AddComponent<BossResourceTargetStub>();
            resource.ParticipantValue = participant;
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            InitializeNetworkBehaviours(go);
            SetNetId(identity, netId);
            NetworkServer.spawned.Add(netId, identity);
            return go;
        }

        private static void SetNetId(NetworkIdentity identity, uint netId)
        {
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId))
                .SetValue(identity, netId);
        }

        private static void InitializeNetworkBehaviours(GameObject go)
        {
            MethodInfo method = typeof(NetworkIdentity).GetMethod(
                "InitializeNetworkBehaviours", INSTANCE);
            Assert.IsNotNull(method);
            method.Invoke(go.GetComponent<NetworkIdentity>(), null);
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, INSTANCE);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private static object Get(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, INSTANCE);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(target);
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo[] methods = target.GetType().GetMethods(INSTANCE);
            for (int i = 0; i < methods.Length; ++i)
            {
                if ((methods[i].Name == methodName) &&
                    (methods[i].GetParameters().Length == arguments.Length))
                {
                    return methods[i].Invoke(target, arguments);
                }
            }

            Assert.Fail($"{target.GetType().Name}.{methodName}({arguments.Length}) not found");
            return null;
        }
    }

    public sealed class BossParryWindowCapture : MonoBehaviour, IParryWindowTarget
    {
        public IParryable Source { get; private set; }
        public byte RepeatIndex
        {
            get
            {
                if (Source == null)
                {
                    return byte.MaxValue;
                }

                PropertyInfo property = Source.GetType().GetProperty("RepeatIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return (property == null) ? byte.MaxValue : (byte) property.GetValue(Source);
            }
        }
        public int CloseCount { get; private set; }

        public bool OpenParryWindow(IParryable source, ParryWindowKey key,
            double telegraphStartTime, double impactTime)
        {
            Source = source;
            return true;
        }

        public void CloseParryWindow(ParryWindowKey key)
        {
            ++CloseCount;
        }
    }

    public sealed class BossLegacyAimStub : MonoBehaviour, IAimAssistTarget
    {
        public Vector3 AimPoint => transform.position;
    }

    public sealed class BossFixedAimStub : MonoBehaviour, IAimAssistTarget,
        ITargetPartIdSource
    {
        public byte PartId;
        public bool Targetable;
        public Vector3 AimPoint => transform.position;
        public byte TargetPartId => PartId;
        public bool IsTargetable => Targetable;
    }

    public sealed class BossResourceTargetStub : MonoBehaviour, IGrowthParticipant,
        IResourceTransactionTarget
    {
        public ParticipantId ParticipantValue;
        public float Spirit { get; private set; }
        public int PublishCount { get; private set; }
        public ParticipantId Participant => ParticipantValue;
        public GrowthState Growth { get; private set; }
        public uint ResourceRevision { get; private set; }
        public bool IsResourceTransactionLocked { get; private set; }
        public event Action<GrowthState> GrowthChanged;

        public void ServerApplyGrowth(GrowthState state)
        {
            Growth = state;
            GrowthChanged?.Invoke(state);
        }

        public ResourceTransactionResult ServerPrepareRecovery(
            RecoveryResourceKind resourceKind, ParticipantId sourceParticipant,
            float amount, out ResourceCommitPlan plan)
        {
            plan = new ResourceCommitPlan(resourceKind, sourceParticipant,
                ResourceRevision, Spirit, Spirit + amount);
            return (resourceKind == RecoveryResourceKind.Spirit) && plan.IsValid
                ? ResourceTransactionResult.Success
                : ResourceTransactionResult.InvalidRequest;
        }

        public bool ServerTryAcquireResourceTransaction(in ResourceCommitPlan plan)
        {
            if (IsResourceTransactionLocked || (plan.ExpectedRevision != ResourceRevision))
            {
                return false;
            }

            IsResourceTransactionLocked = true;
            return true;
        }

        public void ServerApplyResourceTransaction(in ResourceCommitPlan plan)
        {
            Spirit = plan.NextValue;
        }

        public void ServerPublishResourceTransaction(in ResourceCommitPlan plan)
        {
            ++ResourceRevision;
            ++PublishCount;
        }

        public void ServerReleaseResourceTransaction()
        {
            IsResourceTransactionLocked = false;
        }
    }
}
