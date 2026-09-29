// System
using System.Reflection;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// Stage 5-1: BossController.ResolveUpperState(순수 분류 함수)가 확정 우선순위대로
    /// 동작하는지, 그리고 기존 ServerTick 게이트("Combat 실행을 허용하는가")와 Combat/
    /// 비-Combat 판정이 일치하는지 확인한다. ResolveUpperState는 여전히 게임플레이
    /// 권한이 없는 순수 함수이고, 이 테스트도 실제 게이트를 호출하지 않는다
    /// </summary>
    public sealed class BossUpperStateShadowTests
    {
        private const BindingFlags PRIVATE_STATIC = BindingFlags.NonPublic | BindingFlags.Static;

        /****************************************
        *        ResolveUpperState 순수 분류
        ****************************************/

        [Test]
        public void DeadTakesPriorityOverEverything()
        {
            Assert.AreEqual("Dead",
                Resolve(isDead: true, soulState: BossFinalState.SoulWindow, groggyPhaseName: "Groggy"));
        }

        [Test]
        public void SoulWindowMapsToSoulWindow()
        {
            Assert.AreEqual("SoulWindow",
                Resolve(isDead: false, soulState: BossFinalState.SoulWindow, groggyPhaseName: "Active"));
        }

        [Test]
        public void GroggyMapsToStagger()
        {
            Assert.AreEqual("Stagger",
                Resolve(isDead: false, soulState: BossFinalState.Dormant, groggyPhaseName: "Groggy"));
        }

        [Test]
        public void ImmuneMapsToCombat()
        {
            Assert.AreEqual("Combat",
                Resolve(isDead: false, soulState: BossFinalState.Dormant, groggyPhaseName: "Immune"));
        }

        [Test]
        public void ActiveMapsToCombat()
        {
            Assert.AreEqual("Combat",
                Resolve(isDead: false, soulState: BossFinalState.Dormant, groggyPhaseName: "Active"));
        }

        [Test]
        public void FailureAttackMapsToCombat()
        {
            Assert.AreEqual("Combat",
                Resolve(isDead: false, soulState: BossFinalState.FailureAttack, groggyPhaseName: "Active"));
        }

        [Test]
        public void KillableMapsToCombat()
        {
            Assert.AreEqual("Combat",
                Resolve(isDead: false, soulState: BossFinalState.Killable, groggyPhaseName: "Active"));
        }

        [Test]
        public void DormantMapsToCombat()
        {
            Assert.AreEqual("Combat",
                Resolve(isDead: false, soulState: BossFinalState.Dormant, groggyPhaseName: "Active"));
        }

        // 비정상 동시 관찰 조합 - SoulWindow가 Groggy를 이긴다(ExitGroggyForFinal 의미와 일치)
        [Test]
        public void SoulWindowTakesPriorityOverGroggy()
        {
            Assert.AreEqual("SoulWindow",
                Resolve(isDead: false, soulState: BossFinalState.SoulWindow, groggyPhaseName: "Groggy"));
        }

        /****************************************
        *   oldAllowed / newAllowed 조합 (Combat 허용 여부 일치)
        ****************************************/

        // 기존 ServerTick 11·12번 게이트가 실제로 쓰는 조건식을 그대로 재현한 것이다:
        //   oldAllowed = (_groggyPhase != Groggy) && !(soulState == SoulWindow)
        [TestCase(BossFinalState.Dormant,       "Active")]
        [TestCase(BossFinalState.Dormant,       "Groggy")]
        [TestCase(BossFinalState.Dormant,       "Immune")]
        [TestCase(BossFinalState.SoulWindow,    "Active")]
        [TestCase(BossFinalState.SoulWindow,    "Groggy")]
        [TestCase(BossFinalState.FailureAttack, "Active")]
        [TestCase(BossFinalState.FailureAttack, "Groggy")]
        [TestCase(BossFinalState.Killable,      "Active")]
        public void OldGateAndNewClassificationAgreeOnCombatPermission(
            BossFinalState soulState, string groggyPhaseName)
        {
            bool oldAllowed = (groggyPhaseName != "Groggy") && (soulState != BossFinalState.SoulWindow);

            string upperState = Resolve(isDead: false, soulState: soulState, groggyPhaseName: groggyPhaseName);
            bool newAllowed = (upperState == "Combat");

            Assert.AreEqual(oldAllowed, newAllowed,
                $"soulState={soulState}, groggyPhase={groggyPhaseName}, upperState={upperState}");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static string Resolve(bool isDead, BossFinalState soulState, string groggyPhaseName)
        {
            System.Type groggyPhaseType = typeof(BossController).GetNestedType("GroggyPhase", BindingFlags.NonPublic);
            object groggyPhase = System.Enum.Parse(groggyPhaseType, groggyPhaseName);

            MethodInfo method = typeof(BossController).GetMethod("ResolveUpperState", PRIVATE_STATIC);
            Assert.IsNotNull(method, "ResolveUpperState를 찾지 못했다");

            object result = method.Invoke(null, new object[] { isDead, soulState, groggyPhase });
            return result.ToString();
        }
    }
}
