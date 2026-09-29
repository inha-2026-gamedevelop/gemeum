// System
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
    /// <summary>
    /// Stage 5-0: TickGroggyState -> TickGroggySystem(TickStagger/TickGroggyImmune) 분리가
    /// 관찰 가능한 상태 변화 기준으로 기존과 동일하게 동작하는지 확인한다.
    /// 구 TickGroggyState는 프로덕션 코드에 남기지 않았으므로, 여기서는 새 구조만 검증한다
    /// </summary>
    public sealed class BossGroggySystemTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

        private const float GROGGY_DURATION           = 5f;
        private const float GROGGY_WAKE_WARNING        = 1f;
        private const float GROGGY_IMMUNITY_DURATION  = 3f;

        private GameObject         _boss;
        private BossController     _controller;
        private BossStatus         _status;
        private BossSoulController _souls;
        private BossPatternSO      _data;
        private IRunTimeSource     _previousRunTimeSource;
        private TestRunTimeSource  _testRunTimeSource;

        /****************************************
        *                Fixtures
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(NetworkServer.active, "실행 중인 서버에서는 이 테스트를 실행하지 않는다");
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);

            _previousRunTimeSource = RunTimeLocator.Current;
            _testRunTimeSource = new TestRunTimeSource
            {
                GameplayElapsedSeconds = 10d,
            };
            RunTimeLocator.Register(_testRunTimeSource);

            _boss = new GameObject("BossGroggySystemTest", typeof(NetworkIdentity), typeof(BossStatus),
                typeof(BossSoulController));
            _controller = _boss.AddComponent<BossController>();
            _status     = _boss.GetComponent<BossStatus>();
            _souls      = _boss.GetComponent<BossSoulController>();
            Invoke(_boss.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");

            _data = ScriptableObject.CreateInstance<BossPatternSO>();
            Set(_data, "_groggyDuration", GROGGY_DURATION);
            Set(_data, "_groggyWakeWarningDuration", GROGGY_WAKE_WARNING);
            Set(_data, "_groggyImmunityDuration", GROGGY_IMMUNITY_DURATION);
            Set(_data, "_groggyGaugeMax", 100f);

            Set(_controller, "_status", _status);
            Set(_controller, "_souls", _souls);
            Set(_controller, "_bossData", _data);
            Set(_controller, "_groggyGauge", 100f);
            Set(_status, "_maxHealth", 100f);
            Set(_status, "_health", 100f);

            // 원혼 스냅샷 기본값 = Dormant(HasEnteredFinalSequence == false)
            Set(_souls, "_snapshot", new BossSoulSnapshot { State = BossFinalState.Dormant });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_boss);
            UnityEngine.Object.DestroyImmediate(_data);
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

        // 1. Groggy 종료 틱 - 같은 호출 안에서 Immune 분기가 추가로 진행되면 안 된다
        [Test]
        public void GroggyExitDoesNotAdvanceImmuneTimerInSameTick()
        {
            Set(_controller, "_groggyPhase", ParseGroggyPhase("Groggy"));
            Set(_controller, "_groggyStateTimer", 0f);

            Invoke(_controller, "TickGroggySystem", GROGGY_DURATION);

            Assert.AreEqual("Immune", Get(_controller, "_groggyPhase").ToString());
            Assert.AreEqual(0f, (float) Get(_controller, "_groggyStateTimer"));
        }

        // 2. 다음 틱 - Immune 상태에서 호출하면 그때부터 정상적으로 진행된다
        [Test]
        public void ImmuneTimerAdvancesOnSubsequentTick()
        {
            Set(_controller, "_groggyPhase", ParseGroggyPhase("Immune"));
            Set(_controller, "_groggyStateTimer", 0f);

            Invoke(_controller, "TickGroggySystem", 1.5f);

            Assert.AreEqual("Immune", Get(_controller, "_groggyPhase").ToString());
            Assert.AreEqual(1.5f, (float) Get(_controller, "_groggyStateTimer"));
        }

        // 3. Immune 지속시간 종료 - 기존과 동일한 조건에서 Active로 복귀한다
        [Test]
        public void ImmuneDurationEndReturnsToActive()
        {
            Set(_controller, "_groggyPhase", ParseGroggyPhase("Immune"));
            Set(_controller, "_groggyStateTimer", 0f);

            Invoke(_controller, "TickGroggySystem", GROGGY_IMMUNITY_DURATION);

            Assert.AreEqual("Active", Get(_controller, "_groggyPhase").ToString());
            Assert.AreEqual(0f, (float) Get(_controller, "_groggyStateTimer"));
        }

        // 4-a. HasEnteredFinalSequence == true면 Groggy 분기가 진행되지 않는다
        [Test]
        public void FinalSequenceBlocksGroggyBranch()
        {
            Set(_souls, "_snapshot", new BossSoulSnapshot { State = BossFinalState.SoulWindow });
            Set(_controller, "_groggyPhase", ParseGroggyPhase("Groggy"));
            Set(_controller, "_groggyStateTimer", 0f);

            Invoke(_controller, "TickGroggySystem", GROGGY_DURATION * 10f);

            Assert.AreEqual("Groggy", Get(_controller, "_groggyPhase").ToString());
            Assert.AreEqual(0f, (float) Get(_controller, "_groggyStateTimer"));
        }

        // 4-b. HasEnteredFinalSequence == true면 Immune 분기도 진행되지 않는다
        [Test]
        public void FinalSequenceBlocksImmuneBranch()
        {
            Set(_souls, "_snapshot", new BossSoulSnapshot { State = BossFinalState.SoulWindow });
            Set(_controller, "_groggyPhase", ParseGroggyPhase("Immune"));
            Set(_controller, "_groggyStateTimer", 0f);

            Invoke(_controller, "TickGroggySystem", GROGGY_IMMUNITY_DURATION * 10f);

            Assert.AreEqual("Immune", Get(_controller, "_groggyPhase").ToString());
            Assert.AreEqual(0f, (float) Get(_controller, "_groggyStateTimer"));
        }

        private sealed class TestRunTimeSource : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }

            public double FieldElapsedSeconds => 0d;
            public double BossElapsedSeconds => 0d;
            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool IsPaused => false;
        }

        /****************************************
        *                Helpers
        ****************************************/

        private object ParseGroggyPhase(string name)
        {
            return System.Enum.Parse(Field(_controller, "_groggyPhase").FieldType, name);
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

            Assert.Fail($"메서드 {name}({arguments.Length}개 인자)를 찾지 못했다");
            return null;
        }
    }
}
