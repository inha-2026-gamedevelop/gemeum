// System
using System;
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;

// Unity 테스트
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary> 보스 종료와 취소가 남은 판정, 창, 예고를 되살리지 않는지 확인한다 </summary>
    public class BossExecutionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
        private const uint  ATTACK_ID       = 7u;
        private const float TEST_HEALTH     = 100f;
        private const float MARK_DURATION   = 8f;
        private const int   MARK_SLOT_COUNT = 3;
        private const uint  BOSS_NET_ID     = 1u;

        private GameObject     _boss;
        private GameObject     _target;
        private BossController _controller;
        private BossStatus     _status;
        private BossPatternSO  _data;
        private IRunTimeSource  _previousRunTimeSource;
        private TestRunTimeSource _testRunTimeSource;

        /****************************************
        *                Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서는 이 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            DevAccess.Unlock();

            _previousRunTimeSource = RunTimeLocator.Current;
            _testRunTimeSource = new TestRunTimeSource
            {
                GameplayElapsedSeconds = 10d,
            };
            RunTimeLocator.Register(_testRunTimeSource);

            _boss = new GameObject("BossExecutionTest", typeof(NetworkIdentity), typeof(BossStatus));
            _target = new GameObject("BossExecutionTarget");
            _controller = _boss.AddComponent<BossController>();
            _status = _boss.GetComponent<BossStatus>();
            Invoke(_boss.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");

            // 스폰된 보스를 흉내낸다. netId가 0이면 표식 대상(TargetRef)이 성립하지 않는다
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId))
                .SetValue(_boss.GetComponent<NetworkIdentity>(), BOSS_NET_ID);
            _data = ScriptableObject.CreateInstance<BossPatternSO>();
            Set(_controller, "_status", _status);
            Set(_controller, "_targeting", new BossTargetingSystem(_boss.transform, LayerMask.GetMask(Constants.Layer.PLAYER)));
            Set(_controller, "_bossData", _data);
            Set(_controller, "_groggyGauge", 100f);
            Set(_status, "_maxHealth", TEST_HEALTH);
            Set(_status, "_health", TEST_HEALTH);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_boss);
            UnityEngine.Object.DestroyImmediate(_target);
            UnityEngine.Object.DestroyImmediate(_data);
            MarkRegistry.ClearAll(); // 표식은 정적 상태라 테스트 사이에 새면 다음 검사가 오염된다
            RunTimeLocator.Unregister(_testRunTimeSource);
            if (_previousRunTimeSource != null)
            {
                RunTimeLocator.Register(_previousRunTimeSource);
            }
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void DevPhaseRestartCancelsAttackAndCanReturnToPhaseOne()
        {
            var database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(database, "_boss", _data);
            Set(_data, "_maxHealth", TEST_HEALTH);
            Set(_data, "_phases", new[]
            {
                new BossPatternSO.PhaseEntry { PhaseIndex = 1, EnterHealthRatio = 1f },
                new BossPatternSO.PhaseEntry { PhaseIndex = 2, EnterHealthRatio = 0.6f },
            });
            GameDB.Initialize(database);
            BossPhaseStateBase old = CreateWindup(1);
            try
            {
                Assert.IsTrue(_controller.ServerDevRestartPhase(2));
                Assert.AreEqual(2, _controller.CurrentPhaseIndex);
                Assert.AreEqual(60f, _status.Health, 0.001f);
                Assert.IsFalse(_controller.TelegraphCue.IsActive);
                Assert.IsNull(Get(old, "_activeExecution"));
                Assert.IsTrue(_controller.ServerDevRestartPhase(1));
                Assert.AreEqual(1, _controller.PhaseSnapshot.PhaseIndex);
                Assert.AreEqual(TEST_HEALTH, _status.Health);
                Assert.IsFalse(_controller.ServerDevRestartPhase(99));
                Assert.AreEqual(1, _controller.CurrentPhaseIndex);
            }
            finally { GameDB.ResetStatic(); UnityEngine.Object.DestroyImmediate(database); }
        }

        [Test]
        public void DevPhaseRestartReportsPauseAndStoppedStateWithoutChangingSnapshot()
        {
            BossPhaseSnapshot before = _controller.PhaseSnapshot;
            _testRunTimeSource.IsPaused = true;
            Assert.IsFalse(_controller.ServerDevRestartPhase(2, out string failure));
            Assert.That(failure, Does.Contain("일시정지"));
            Assert.AreEqual(before.Revision, _controller.PhaseSnapshot.Revision);
            _testRunTimeSource.IsPaused = false;
            Set(_controller, "_serverStopped", true);
            Assert.IsFalse(_controller.ServerDevRestartPhase(2, out failure));
            Assert.That(failure, Does.Contain("종료되거나 비활성화"));
            Assert.AreEqual(before.Revision, _controller.PhaseSnapshot.Revision);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void CancelClosesWindowOnceAndClearsExecution(int phaseIndex)
        {
            BossPhaseStateBase phase = CreateWindup(phaseIndex);
            var window = new WindowStub();
            Set(phase, "_openParryTarget", window);
            Set(phase, "_openParryKey", new ParryWindowKey(1u, ATTACK_ID));

            phase.CancelActiveAttack();
            phase.CancelActiveAttack();

            Assert.AreEqual(1, window.CloseCount);
            Assert.AreEqual(ATTACK_ID, window.ClosedKey.WindowId);
            Assert.IsNull(Get(phase, "_activeExecution"));
            Assert.AreEqual(0u, Get(phase, "_activeAttackInstanceId"));
            Assert.AreEqual("Recovery", Get(phase, "_state").ToString());
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ExitedPhaseCannotTickOrAcceptLateParry(int phaseIndex)
        {
            BossPhaseStateBase phase = CreateWindup(phaseIndex);
            object execution = Get(phase, "_activeExecution");
            phase.Exit();
            phase.Exit();
            phase.Tick(100f);
            Invoke(phase, "HandleAttackParried", execution, 1u);

            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"));
            Assert.IsNull(phase.Target);
            Assert.AreEqual("Recovery", Get(phase, "_state").ToString());
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void DuplicateParryReducesGaugeOnlyOnce(int phaseIndex)
        {
            BossPhaseStateBase phase = CreateWindup(phaseIndex);
            object execution = Get(phase, "_activeExecution");
            Invoke(phase, "HandleAttackParried", execution, 1u);
            Invoke(phase, "HandleAttackParried", execution, 1u);

            Assert.AreEqual(90f, Get(_controller, "_groggyGauge"));
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [Test]
        public void DeflectKeepsSequenceAndGroggyButFinalCounterStillCancelsOnce()
        {
            var phase = CreateWindup(1);
            var type = Get(phase, "_activeExecution").GetType();
            object execution = Activator.CreateInstance(type, new object[] { .32f, ATTACK_ID, (byte)0, null });
            type.GetProperty("IsDeflect").SetValue(execution, true);
            Set(phase, "_activeExecution", execution);
            Invoke(phase, "HandleAttackParried", execution, 17u);
            Invoke(phase, "HandleAttackParried", execution, 17u);
            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"));
            Assert.AreEqual(ATTACK_ID, Get(phase, "_activeAttackInstanceId"));
            Assert.AreEqual("Windup", Get(phase, "_state").ToString());
            Assert.AreEqual(17u, Get(phase, "_deflectedDefender"));
            object final = Activator.CreateInstance(type, new object[] { .32f, ATTACK_ID, (byte)2, null });
            Set(phase, "_activeExecution", final);
            Invoke(phase, "HandleAttackParried", final, 17u);
            Invoke(phase, "HandleAttackParried", final, 17u);
            Assert.AreEqual(90f, Get(_controller, "_groggyGauge"));
            Assert.AreEqual(0u, Get(phase, "_activeAttackInstanceId"));
        }

        private sealed class DeflectVictim : MonoBehaviour, IDamageable
        {
            public float Health = 100f;
            public HitStrength LastStrength = HitStrength.Critical; // 어느 기본값과도 안 겹치게
            public bool IsDead => Health <= 0;
            public void TakeDamage(in DamageInfo damage)
            {
                Health      -= damage.Amount;
                LastStrength = damage.Strength;
            }
        }

        [Test]
        public void DeflectFiltersOnlyItsDefenderAcrossDuplicateColliders()
        {
            var first = new GameObject("defender", typeof(NetworkIdentity), typeof(DeflectVictim), typeof(SphereCollider));
            var second = new GameObject("bystander", typeof(NetworkIdentity), typeof(DeflectVictim), typeof(SphereCollider));
            try
            {
                first.layer = second.layer = 30;
                first.transform.position = Vector3.forward;
                second.transform.position = Vector3.forward + Vector3.right;
                first.AddComponent<SphereCollider>();
                typeof(NetworkIdentity).GetProperty("netId").SetValue(first.GetComponent<NetworkIdentity>(), 17u);
                typeof(NetworkIdentity).GetProperty("netId").SetValue(second.GetComponent<NetworkIdentity>(), 18u);
                Physics.SyncTransforms();
                var runner = typeof(BossController).Assembly.GetType("Geumeum.AI.BossPatternRunner");
                var hit = runner.GetMethod("ExecuteMeleeArc");

                // 인자 순서가 (.., damageRouter, hitStrength, deflectedDefender)로 합쳐졌다.
                // 쳐내기 면제와 피격 강도는 서로 독립이어야 한다 - 둘을 같이 넘겨서 확인한다
                Assert.AreEqual(10, hit.GetParameters().Length);
                Assert.AreEqual("hitStrength",       hit.GetParameters()[8].Name);
                Assert.AreEqual("deflectedDefender", hit.GetParameters()[9].Name);

                hit.Invoke(null, new object[] { Vector3.zero, Vector3.forward, 3f, 360f, 10f, 1u, 1 << 30, null, HitStrength.Light, 17u });
                Assert.AreEqual(100f, first.GetComponent<DeflectVictim>().Health);
                Assert.AreEqual(90f, second.GetComponent<DeflectVictim>().Health);
                // 면제되지 않은 쪽에는 넘긴 강도가 그대로 실려야 한다
                Assert.AreEqual(HitStrength.Light, second.GetComponent<DeflectVictim>().LastStrength);

                hit.Invoke(null, new object[] { Vector3.zero, Vector3.forward, 3f, 360f, 10f, 1u, 1 << 30, null, HitStrength.Heavy, 0u });
                Assert.AreEqual(90f, first.GetComponent<DeflectVictim>().Health);
                Assert.AreEqual(80f, second.GetComponent<DeflectVictim>().Health);
                Assert.AreEqual(HitStrength.Heavy, first.GetComponent<DeflectVictim>().LastStrength);
                Assert.AreEqual(HitStrength.Heavy, second.GetComponent<DeflectVictim>().LastStrength);
            }
            finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void MissingTargetCancelsBeforeImpact(int phaseIndex)
        {
            BossPhaseStateBase phase = CreateWindup(phaseIndex);
            UnityEngine.Object.DestroyImmediate(_target);
            phase.Tick(100f);

            Assert.AreEqual("Recovery", Get(phase, "_state").ToString());
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [Test]
        public void PreviousAttackCancelCannotCloseNewCue()
        {
            CreateWindup(1);
            Invoke(_controller, "ServerCancelTelegraph", ATTACK_ID - 1u);
            Assert.IsTrue(_controller.TelegraphCue.IsActive);
            Invoke(_controller, "ServerCancelTelegraph", ATTACK_ID);
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [Test]
        public void AttackIdSkipsZeroAfterWrap()
        {
            Set(_controller, "_nextAttackInstanceId", uint.MaxValue);
            Invoke(_controller, "ServerNextAttackInstanceId");
            Assert.AreEqual(1u, Get(_controller, "_nextAttackInstanceId"));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void GroggyCancelsActiveAttackAndBlocksRepeat(int phaseIndex)
        {
            BossPhaseStateBase phase = CreateWindup(phaseIndex);
            var identity = _boss.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            try
            {
                Invoke(_controller, "EnterGroggy");
                Assert.IsFalse(_controller.TelegraphCue.IsActive);
                Assert.AreEqual(0u, Get(phase, "_activeAttackInstanceId"));
                Assert.AreEqual("Groggy", Get(_controller, "_groggyPhase").ToString());
                Assert.AreEqual(_data.GroggyBodyDamageMultiplier, Get(_status, "_bodyDamageMultiplier"));
            }
            finally
            {
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, false);
            }
        }

        [Test]
        public void ResultPhaseStopsExecution()
        {
            CreateWindup(2);
            Invoke(_controller, "HandleRunPhaseChanged", new RunPhaseChangedEvent(RunPhase.Boss, RunPhase.Result));
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
            Assert.IsTrue((bool) Get(_controller, "_serverStopped"));
        }

        [Test]
        public void PhaseTransitionExitsOldStateAndLocksNewState()
        {
            BossPhaseStateBase previous = CreateWindup(1);
            Set(_data, "_phases", new[]
            {
                new BossPatternSO.PhaseEntry { PhaseIndex = 2, EnterHealthRatio = 0.5f },
            });
            Set(_status, "_health", TEST_HEALTH * 0.5f);

            Invoke(_controller, "CommitPendingPhaseTransitions");

            Assert.AreEqual(2, _controller.CurrentPhaseIndex);
            Assert.IsInstanceOf<Phase2State>(Get(_controller, "_currentPhaseState"));
            Assert.AreEqual(_data.TransitionLockDuration, Get(_controller, "_transitionLockRemaining"));
            Assert.IsNull(previous.Target);
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [TestCase("HandleDied")]
        [TestCase("StopServerExecution")]
        public void ServerTerminationExitsActiveState(string method)
        {
            BossPhaseStateBase phase = CreateWindup(2);
            if (method == "HandleDied")
            {
                // 이 테스트에는 GameEventBus가 없다. 사망 사실 발행 실패는 여기서 검증할 대상이 아니다
                ExpectMissingEventBusError();
                Invoke(_controller, method, 1u);
            }
            else
            {
                Invoke(_controller, method, true);
            }

            phase.Tick(100f);
            Assert.IsNull(phase.Target);
            Assert.IsTrue((bool) Get(_controller, "_serverStopped"));
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [Test]
        public void BodyDamageMultiplierResetsAfterGroggy()
        {
            Invoke(_status, "ServerSetBodyDamageMultiplier", _data.GroggyBodyDamageMultiplier);
            _status.TakeDamage(new DamageInfo(10f, 1u, default));
            Assert.AreEqual(85f, _status.Health);

            Set(_controller, "_groggyPhase", Enum.Parse(Field(_controller, "_groggyPhase").FieldType, "Groggy"));
            Invoke(_controller, "TickGroggySystem", _data.GroggyDuration);
            _status.TakeDamage(new DamageInfo(10f, 1u, default));
            Assert.AreEqual(75f, _status.Health);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void InvalidDamageDoesNotCorruptHealth(float amount)
        {
            _status.TakeDamage(new DamageInfo(amount, 1u, default));
            Assert.AreEqual(TEST_HEALTH, _status.Health);
        }

        [Test]
        public void TelegraphRejectsCancelledExpiredAndNonFiniteTime()
        {
            BossTelegraphCue cue = new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                IsActive = true,
                StartServerTime = 10d,
                ImpactServerTime = 12d,
            };

            Assert.IsTrue(cue.IsPendingAt(10d));
            Assert.IsFalse(cue.IsPendingAt(9d));
            Assert.IsFalse(cue.IsPendingAt(12d));
            Assert.IsFalse(cue.IsPendingAt(double.NaN));
            cue.IsActive = false;
            Assert.IsFalse(cue.IsPendingAt(11d));
        }

        [Test]
        public void DisabledBossResumesAfterReEnable()
        {
            BossPhaseStateBase phase = CreateWindup(2);
            using (var server = new ServerRuntimeScope(_boss))
            {
                Invoke(_controller, "OnDisable");

                Assert.IsTrue((bool) Get(_controller, "_serverStopped"));
                Assert.IsFalse((bool) Get(_controller, "_serverTerminated"));
                Assert.IsNotNull(phase.Target, "비활성화는 종료가 아니라 정지다, State를 닫지 않는다");
                Assert.IsFalse(_controller.TelegraphCue.IsActive);
                Assert.AreEqual(1, server.PendingRemovalCount, "정지하면 서버 틱을 내린다");

                Invoke(_controller, "OnEnable");

                Assert.IsFalse((bool) Get(_controller, "_serverStopped"));
                Assert.AreEqual(1, server.PendingAddCount, "재개하면 서버 틱을 다시 올린다");
                Assert.AreEqual(_data.TransitionLockDuration, Get(_controller, "_transitionLockRemaining"),
                    "재개 직후 바로 때리지 않도록 유예를 준다");
            }
        }

        [Test]
        public void TerminatedBossDoesNotResumeOnEnable()
        {
            CreateWindup(2);
            using (var server = new ServerRuntimeScope(_boss))
            {
                ExpectMissingEventBusError();
                Invoke(_controller, "HandleDied", 1u);
                Invoke(_controller, "OnDisable");
                Invoke(_controller, "OnEnable");

                Assert.IsTrue((bool) Get(_controller, "_serverTerminated"));
                Assert.IsTrue((bool) Get(_controller, "_serverStopped"),
                    "사망으로 끝난 보스는 다시 켜도 실행되지 않는다");
                Assert.AreEqual(0, server.PendingAddCount, "종료된 보스는 서버 틱을 다시 올리지 않는다");
            }
        }

        [Test]
        public void StopDuringGroggyRestoresBodyDamageMultiplier()
        {
            CreateWindup(2);
            using (new ServerRuntimeScope(_boss))
            {
                Invoke(_controller, "EnterGroggy");
                Assert.AreEqual(_data.GroggyBodyDamageMultiplier, Get(_status, "_bodyDamageMultiplier"));

                Invoke(_controller, "OnDisable");
                Assert.AreEqual(1f, Get(_status, "_bodyDamageMultiplier"),
                    "그로기 도중 멈춰도 배율이 남지 않는다");

                Invoke(_controller, "OnEnable");
                Assert.AreEqual(_data.GroggyBodyDamageMultiplier, Get(_status, "_bodyDamageMultiplier"),
                    "그로기가 아직 안 끝났으면 재개할 때 배율을 다시 건다");
            }
        }

        [Test]
        public void ResonanceCountsOnceOnCounterPatternOnly()
        {
            const int MUNYEO_PATTERN = 4;
            const int OTHER_PATTERN  = 1;

            MarkBossBody(_testRunTimeSource.GameplayElapsedSeconds);

            // 카운터 패턴이 아니면 표식이 살아 있어도 깎이지 않는다
            Invoke(_controller, "ServerNotifyPatternEnded", OTHER_PATTERN, ATTACK_ID);
            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"));

            Assert.IsTrue(_data.TryGetGaugeReduction(CharacterKind.Munyeo, out float reduction));

            // 씬 매니저가 다시 활성화돼도 공명 조회 직전에 fixture clock을 확정한다
            RunTimeLocator.Register(_testRunTimeSource);
            Invoke(_controller, "ServerNotifyPatternEnded", MUNYEO_PATTERN, ATTACK_ID);
            Assert.AreEqual(100f - reduction, Get(_controller, "_groggyGauge"));

            // 같은 공격의 재통지는 무녀가 둘이어도 두 번 깎지 않는다
            RunTimeLocator.Register(_testRunTimeSource);
            Invoke(_controller, "ServerNotifyPatternEnded", MUNYEO_PATTERN, ATTACK_ID);
            Assert.AreEqual(100f - reduction, Get(_controller, "_groggyGauge"));
        }

        [Test]
        public void ResonanceRejectsMarkAppliedBeforeWindow()
        {
            // 표식은 아직 살아 있지만(지속 8초) 공명 창(3초) 밖에 걸렸다
            MarkBossBody(_testRunTimeSource.GameplayElapsedSeconds - _data.BrandWindow - 1d);

            RunTimeLocator.Register(_testRunTimeSource);
            Invoke(_controller, "ServerNotifyPatternEnded", 4, ATTACK_ID);

            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"),
                "미리 찍어 둔 표식은 게이지를 깎지 못한다");
        }

        [Test]
        public void PrecisionShotInterruptsCurrentCounterAttackOnce()
        {
            BossPhaseStateBase phase = CreateWindup(1);
            Invoke(_controller, "ServerBeginTelegraph", new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                PatternId        = 5,
                ImpactServerTime = NetworkTime.time + 10d,
            });
            Assert.IsTrue(_data.TryGetGaugeReduction(CharacterKind.Singung, out float reduction));

            PrecisionInterruptContext context = CreatePrecisionContext(NetworkTime.time);

            Assert.IsTrue(_controller.TryInterruptPrecisionShot(context));
            Assert.IsFalse(_controller.TryInterruptPrecisionShot(context));
            Assert.AreEqual(100f - reduction, Get(_controller, "_groggyGauge"));
            Assert.AreEqual(0u, Get(phase, "_activeAttackInstanceId"));
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
        }

        [Test]
        public void PrecisionShotAcceptsWindupPreservedAcrossLongPause()
        {
            BossPhaseStateBase phase = CreateWindup(1);
            Set(phase, "_activePattern", new BossPatternSO.PatternEntry
            {
                PatternId = 5,
                TelegraphDuration = 10f,
                ActiveDuration = 1f,
            });
            ((float[]) Get(phase, "_activeImpactOffsets"))[0] = 10f;
            Invoke(_controller, "ServerBeginTelegraph", new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                PatternId        = 5,
                ImpactServerTime = NetworkTime.time + 10d,
            });
            BossTelegraphCue cue = _controller.TelegraphCue;

            _testRunTimeSource.IsPaused = true;
            Invoke(_controller, "ServerTick", 100f);
            Assert.IsTrue(phase.IsAttackWindup(ATTACK_ID));

            _testRunTimeSource.IsPaused = false;
            Invoke(_controller, "ServerTick", 0.02f);
            Assert.IsTrue(phase.IsAttackWindup(ATTACK_ID));

            PrecisionInterruptContext afterLongPause = CreatePrecisionContext(
                cue.ImpactServerTime + 100d);
            Assert.IsTrue(_controller.TryInterruptPrecisionShot(afterLongPause));
            Assert.IsFalse(phase.IsAttackWindup(ATTACK_ID));
        }

        [Test]
        public void PrecisionShotRejectsWrongTargetAndEndedWindup()
        {
            BossPhaseStateBase phase = CreateWindup(1);
            Invoke(_controller, "ServerBeginTelegraph", new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                PatternId        = 5,
                ImpactServerTime = NetworkTime.time + 10d,
            });

            PrecisionInterruptContext valid = CreatePrecisionContext(NetworkTime.time);
            TargetRef wrong = TargetRef.Create(
                new RunId(valid.Target.RunIdValue + 1u),
                new SceneRevision(valid.Target.SceneRevisionValue),
                valid.Target.EntityNetId,
                valid.Target.PartId);
            PrecisionInterruptContext wrongTarget = new PrecisionInterruptContext(
                valid.ActionId,
                valid.Attacker,
                valid.AttackerNetId,
                wrong,
                true,
                valid.HitServerTime);
            Assert.IsFalse(_controller.TryInterruptPrecisionShot(wrongTarget));
            phase.CancelActiveAttack();
            Assert.IsFalse(_controller.TryInterruptPrecisionShot(valid));
            Assert.IsFalse(_controller.TelegraphCue.IsActive);
            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"));
        }

        [Test]
        public void PrecisionShotRejectsCounterPatternAssignedToAnotherCharacter()
        {
            BossPhaseStateBase phase = CreateWindup(1);
            Invoke(_controller, "ServerBeginTelegraph", new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                PatternId        = 4,
                ImpactServerTime = NetworkTime.time + 10d,
            });

            Assert.IsFalse(_controller.TryInterruptPrecisionShot(
                CreatePrecisionContext(NetworkTime.time)));
            Assert.AreEqual(100f, Get(_controller, "_groggyGauge"));
            Assert.AreEqual(ATTACK_ID, Get(phase, "_activeAttackInstanceId"));
            Assert.IsTrue(_controller.TelegraphCue.IsActive);
        }

        /****************************************
        *                Helpers
        ****************************************/

        /// <summary> 보스 본체에 표식을 건다. 부여 시각을 직접 정해 공명 창 경계를 검사한다 </summary>
        private void MarkBossBody(double appliedServerTime)
        {
            TargetRef target = (TargetRef) Invoke(_controller, "CreateBodyTargetRef");

            bool applied = MarkRegistry.TryApply(
                new ParticipantId(1UL), target, appliedServerTime,
                MARK_DURATION, MARK_SLOT_COUNT, out MarkRejection rejection);

            Assert.IsTrue(applied, $"표식 부여가 거부됐다: {rejection}");
        }

        private PrecisionInterruptContext CreatePrecisionContext(double hitServerTime)
        {
            return new PrecisionInterruptContext(
                31u,
                new ParticipantId(9UL),
                17u,
                (TargetRef) Invoke(_controller, "CreateBodyTargetRef"),
                true,
                hitServerTime);
        }

        /// <summary> isServer와 서버 틱 스케줄러를 세운 채로 수명 경로를 실행하고, 끝나면 되돌린다 </summary>
        private sealed class ServerRuntimeScope : IDisposable
        {
            private readonly NetworkIdentity        _identity;
            private readonly GameObject             _schedulerObject;
            private readonly AuthorityTickScheduler _scheduler;

            public ServerRuntimeScope(GameObject owner)
            {
                _identity = owner.GetComponent<NetworkIdentity>();
                SetIsServer(true);

                _schedulerObject = new GameObject("BossExecutionTestScheduler", typeof(NetworkIdentity));
                _scheduler       = _schedulerObject.AddComponent<AuthorityTickScheduler>();
                SetServerInstance(_scheduler);
            }

            /// <summary> 등록 요청 수. 실제 반영은 다음 틱이라 대기열로 확인한다 </summary>
            public int PendingAddCount => Count(Get(_scheduler, "_pendingAdds"));

            /// <summary> 해제 요청 수 </summary>
            public int PendingRemovalCount => Count(Get(_scheduler, "_pendingRemovals"));

            // 대기열이 List와 HashSet으로 갈려 있어 비제네릭 IEnumerable로만 함께 셀 수 있다
            private static int Count(object collection)
            {
                int count = 0;
                foreach (object unused in (IEnumerable) collection)
                {
                    ++count;
                }

                return count;
            }

            public void Dispose()
            {
                SetServerInstance(null);
                UnityEngine.Object.DestroyImmediate(_schedulerObject);
                SetIsServer(false);
            }

            private void SetIsServer(bool value)
            {
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(_identity, value);
            }

            private static void SetServerInstance(AuthorityTickScheduler value)
            {
                typeof(AuthorityTickScheduler)
                    .GetProperty(nameof(AuthorityTickScheduler.ServerInstance)).SetValue(null, value);
            }
        }

        private BossPhaseStateBase CreateWindup(int phaseIndex)
        {
            BossPhaseStateBase phase = (phaseIndex == 1)
                ? new Phase1State(_boss.transform, _status, _controller)
                : new Phase2State(_boss.transform, _status, _controller);
            phase.Target = _target.transform;
            Set(phase, "_activeTarget", _target.transform);
            Set(phase, "_state", Enum.Parse(Field(phase, "_state").FieldType, "Windup"));
            Set(phase, "_activeAttackInstanceId", ATTACK_ID);
            Set(phase, "_activeExecution", Activator.CreateInstance(Field(phase, "_activeExecution").FieldType,
                new object[] { 0.32f, null }));
            Set(_controller, "_currentPhaseState", phase);
            Invoke(_controller, "ServerBeginTelegraph", new BossTelegraphCue
            {
                AttackInstanceId = ATTACK_ID,
                ImpactServerTime = NetworkTime.time + 10d,
            });
            return phase;
        }

        private static FieldInfo Field(object target, string name)
        {
            return target.GetType().GetField(name, PRIVATE_INSTANCE);
        }

        private static object Get(object target, string name)
        {
            return Field(target, name).GetValue(target);
        }

        private static void Set(object target, string name, object value)
        {
            Field(target, name).SetValue(target, value);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo[] methods = target.GetType().GetMethods(PRIVATE_INSTANCE);
            for (int i = 0; i < methods.Length; ++i)
            {
                if ((methods[i].Name == name) &&
                    (methods[i].GetParameters().Length == arguments.Length))
                {
                    return methods[i].Invoke(target, arguments);
                }
            }

            Assert.Fail($"{target.GetType().Name}.{name}({arguments.Length}) not found");
            return null;
        }

        private sealed class WindowStub : IParryWindowTarget
        {
            public int CloseCount { get; private set; }
            public ParryWindowKey ClosedKey { get; private set; }

            public bool OpenParryWindow(IParryable source, ParryWindowKey key,
                double telegraphStartTime, double impactTime)
            {
                return true;
            }

            public void CloseParryWindow(ParryWindowKey key)
            {
                ++CloseCount;
                ClosedKey = key;
            }
        }

        /// <summary> 표식 테스트가 NetworkTime과 우연히 어긋나지 않도록 gameplay clock을 고정한다 </summary>
        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }

            public double FieldElapsedSeconds => 0d;
            public double BossElapsedSeconds => 0d;
            public RunPauseReason PauseReasons => IsPaused
                ? RunPauseReason.ParticipantMissing
                : RunPauseReason.None;
            public bool IsPaused { get; set; }
        }
        /// <summary>
        /// HandleDied는 보스 사망 사실을 GameEventBus로 발행한다. EditMode에는 버스가 없어
        /// 오류 로그가 남고, NUnit은 처리하지 않은 오류 로그를 실패로 본다
        /// 사망 사실이 실제로 발행되는지는 RunManager 쪽 검증의 몫이다
        /// </summary>
        private static void ExpectMissingEventBusError()
        {
            LogAssert.Expect(LogType.Error, "[BossController] GameEventBus가 없어 보스 사망을 알리지 못했다, 런이 끝나지 않는다");
        }
    }
}
