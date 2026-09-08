// System
using System;
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
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
        private const uint ATTACK_ID    = 7u;
        private const float TEST_HEALTH = 100f;

        private GameObject     _boss;
        private GameObject     _target;
        private BossController _controller;
        private BossStatus     _status;
        private BossPatternSO  _data;

        /****************************************
        *                Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서는 이 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _boss = new GameObject("BossExecutionTest", typeof(NetworkIdentity), typeof(BossStatus));
            _target = new GameObject("BossExecutionTarget");
            _controller = _boss.AddComponent<BossController>();
            _status = _boss.GetComponent<BossStatus>();
            Invoke(_boss.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
            _data = ScriptableObject.CreateInstance<BossPatternSO>();
            Set(_controller, "_status", _status);
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
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, false);
        }

        /****************************************
        *                Tests
        ****************************************/

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
            Invoke(_controller, "TickGroggyState", _data.GroggyDuration);
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

        /****************************************
        *                Helpers
        ****************************************/

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

        private static void Invoke(object target, string name, params object[] arguments)
        {
            target.GetType().GetMethod(name, PRIVATE_INSTANCE).Invoke(target, arguments);
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
    }
}
