// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// Mirror
using Mirror;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI.Tests
{
    /// <summary> 보스 시계 경계, 광폭화 snapshot, 전역 cadence를 검증한다 </summary>
    public sealed class BossEnrageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
        private const float         NORMAL_CADENCE_PHASE1 = 3f;
        private const float         NORMAL_CADENCE_PHASE2 = 2.2f;
        private const float         ENRAGE_CADENCE        = 1.4f;
        private const float         ENRAGE_DAMAGE         = 3f;
        private const double        WARNING_TIME          = 720d;
        private const double        ENRAGE_TIME           = 810d;

        private GameObject          _boss;
        private GameObject          _target;
        private BossController      _controller;
        private BossPatternSO       _data;
        private IRunTimeSource      _previousRunTimeSource;
        private TestRunTimeSource   _runTimeSource;

        /****************************************
        *              Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _previousRunTimeSource = RunTimeLocator.Current;
            _runTimeSource = new TestRunTimeSource();
            RunTimeLocator.Register(_runTimeSource);

            _boss       = new GameObject("BossEnrageTest", typeof(NetworkIdentity), typeof(BossStatus));
            _target     = new GameObject("BossEnrageTarget");
            _controller = _boss.AddComponent<BossController>();
            _data       = ScriptableObject.CreateInstance<BossPatternSO>();

            Invoke(_boss.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");

            Set(_controller, "_bossData", _data);
            Set(_controller, "_status", _boss.GetComponent<BossStatus>());
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_boss);
            UnityEngine.Object.DestroyImmediate(_target);
            UnityEngine.Object.DestroyImmediate(_data);

            RunTimeLocator.Unregister(_runTimeSource);
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
        public void ScriptableObjectContainsNormalAndEnrageCadenceValues()
        {
            Assert.AreEqual(NORMAL_CADENCE_PHASE1, _data.Phase1AttackIntervalSeconds);
            Assert.AreEqual(NORMAL_CADENCE_PHASE2, _data.Phase2AttackIntervalSeconds);
            Assert.AreEqual(ENRAGE_CADENCE, _data.EnragedAttackIntervalSeconds);
            Assert.AreEqual(ENRAGE_DAMAGE, _data.EnrageDamageMultiplier);
        }

        [Test]
        public void WarningAndEnrageCrossExactlyOnceFromBossClock()
        {
            _runTimeSource.BossElapsedSeconds = WARNING_TIME - 0.001d;
            TickEnrageState();
            Assert.IsFalse(_controller.IsEnrageWarning);
            Assert.IsFalse(_controller.IsEnraged);

            _runTimeSource.BossElapsedSeconds = WARNING_TIME;
            TickEnrageState();
            Assert.IsTrue(_controller.IsEnrageWarning);
            Assert.IsFalse(_controller.IsEnraged);

            _runTimeSource.BossElapsedSeconds = ENRAGE_TIME - 0.001d;
            TickEnrageState();
            Assert.IsFalse(_controller.IsEnraged);

            _runTimeSource.BossElapsedSeconds = ENRAGE_TIME;
            TickEnrageState();
            Assert.IsTrue(_controller.IsEnrageWarning);
            Assert.IsTrue(_controller.IsEnraged);

            _runTimeSource.BossElapsedSeconds = 0d;
            TickEnrageState();
            Assert.IsTrue(_controller.IsEnrageWarning);
            Assert.IsTrue(_controller.IsEnraged);
        }

        [Test]
        public void WarningAndEnrageDoNotProgressWhilePaused()
        {
            _runTimeSource.IsPaused = true;
            _runTimeSource.BossElapsedSeconds = ENRAGE_TIME;
            TickEnrageState();

            Assert.IsFalse(_controller.IsEnrageWarning);
            Assert.IsFalse(_controller.IsEnraged);

            _runTimeSource.IsPaused = false;
            TickEnrageState();
            Assert.IsTrue(_controller.IsEnrageWarning);
            Assert.IsTrue(_controller.IsEnraged);
        }

        [Test]
        public void EnrageDoesNotProgressDuringPhaseTransitionLock()
        {
            Set(_controller, "_currentPhaseState",
                new Phase1State(_boss.transform, _boss.GetComponent<BossStatus>(), _controller));
            Set(_controller, "_transitionLockRemaining", 1f);
            _runTimeSource.BossElapsedSeconds = ENRAGE_TIME;

            Invoke(_controller, "ServerTick", 0.25f);

            Assert.IsFalse(_controller.IsEnrageWarning);
            Assert.IsFalse(_controller.IsEnraged);
            Assert.AreEqual(0.75f, Get(_controller, "_transitionLockRemaining"));
        }

        [TestCase(1, "MeleeArc")]
        [TestCase(2, "ConeBurst")]
        public void AcceptedAttackKeepsDamageMultiplierSnapshot(int phaseIndex, string kindName)
        {
            BossPhaseStateBase phase = CreatePhase(phaseIndex);
            BossPatternSO.PatternEntry pattern = new BossPatternSO.PatternEntry
            {
                PatternId = 1,
                Damage = 10f,
                TelegraphDuration = 1f,
                Cooldown = 25f,
            };

            phase.Target = _target.transform;
            Set(_controller, "_isEnraged", false);
            BeginAttack(phase, pattern, kindName);
            Assert.AreEqual(1f, Get(phase, "_activeDamageMultiplier"));

            Set(_controller, "_isEnraged", true);
            Assert.AreEqual(1f, Get(phase, "_activeDamageMultiplier"));

            Invoke(phase, "CancelActiveAttack");
            BeginAttack(phase, pattern, kindName);
            Assert.AreEqual(ENRAGE_DAMAGE, Get(phase, "_activeDamageMultiplier"));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ImpactUsesAcceptedDamageSnapshotBeforeApplyingLaterEnrage(int phaseIndex)
        {
            _target.layer = LayerMask.NameToLayer(Constants.Layer.PLAYER);
            _target.AddComponent<SphereCollider>();
            DamageTargetStub damageTarget = _target.AddComponent<DamageTargetStub>();

            BossPhaseStateBase phase = CreatePhase(phaseIndex);
            phase.Target = _target.transform;
            BossPatternSO.PatternEntry pattern = new BossPatternSO.PatternEntry
            {
                PatternId = 1,
                Damage = 10f,
                TelegraphDuration = 1f,
                Cooldown = 25f,
            };

            Set(_controller, "_isEnraged", false);
            BeginAttack(phase, pattern, "MeleeArc");
            Set(_controller, "_isEnraged", true);
            Invoke(phase, "ResolveImpact");

            Assert.AreEqual(1, damageTarget.HitCount);
            Assert.AreEqual(pattern.Damage, damageTarget.LastDamage.Amount);

            Invoke(phase, "CancelActiveAttack");
            BeginAttack(phase, pattern, "MeleeArc");
            Invoke(phase, "ResolveImpact");

            Assert.AreEqual(2, damageTarget.HitCount);
            Assert.AreEqual(pattern.Damage * ENRAGE_DAMAGE, damageTarget.LastDamage.Amount);
        }

        [Test]
        public void IndividualCooldownAndGlobalCadenceRemainSeparate()
        {
            Phase1State phase = new Phase1State(_boss.transform, _boss.GetComponent<BossStatus>(), _controller);
            phase.Target = _target.transform;
            BossPatternSO.PatternEntry pattern = new BossPatternSO.PatternEntry
            {
                PatternId = 1,
                Damage = 10f,
                TelegraphDuration = 1f,
                Cooldown = 25f,
            };

            BeginAttack(phase, pattern, "MeleeArc");
            float[] cooldowns = (float[]) Get(phase, "_patternCooldowns");
            Assert.AreEqual(pattern.Cooldown, cooldowns[0]);

            Invoke(phase, "CancelActiveAttack");
            Assert.AreEqual(NORMAL_CADENCE_PHASE1, Get(phase, "_cooldownRemaining"));

            Assert.AreEqual(NORMAL_CADENCE_PHASE2, Invoke(_controller, "ServerGetGlobalCadence", 2));
            Set(_controller, "_isEnraged", true);
            Assert.AreEqual(ENRAGE_CADENCE, Invoke(_controller, "ServerGetGlobalCadence", 1));
            Assert.AreEqual(ENRAGE_CADENCE, Invoke(_controller, "ServerGetGlobalCadence", 2));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void TickEnrageState()
        {
            Invoke(_controller, "TickEnrageState");
        }

        private BossPhaseStateBase CreatePhase(int phaseIndex)
        {
            return (phaseIndex == 1)
                ? new Phase1State(_boss.transform, _boss.GetComponent<BossStatus>(), _controller)
                : new Phase2State(_boss.transform, _boss.GetComponent<BossStatus>(), _controller);
        }

        private static void BeginAttack(
            BossPhaseStateBase phase,
            BossPatternSO.PatternEntry pattern,
            string kindName)
        {
            Type kindType = Field(phase, "_activeKind").FieldType;
            object kind = Enum.Parse(kindType, kindName);
            Invoke(phase, "BeginAttack", pattern, kind);
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
            return target.GetType().GetMethod(name, PRIVATE_INSTANCE | BindingFlags.Public)
                .Invoke(target, arguments);
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds    => 0d;
            public double BossElapsedSeconds     { get; set; }
            public double GameplayElapsedSeconds => 0d;
            public RunPauseReason PauseReasons   => IsPaused ? RunPauseReason.ParticipantMissing : RunPauseReason.None;
            public bool IsPaused                 { get; set; }
        }

        private sealed class DamageTargetStub : MonoBehaviour, IDamageable
        {
            public bool IsDead => false;
            public int HitCount { get; private set; }
            public DamageInfo LastDamage { get; private set; }

            public void TakeDamage(in DamageInfo damage)
            {
                LastDamage = damage;
                ++HitCount;
            }
        }
    }
}
