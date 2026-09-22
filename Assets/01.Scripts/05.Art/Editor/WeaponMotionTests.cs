using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Geumeum.Art.Tests
{
    public sealed class WeaponMotionTests
    {
        [TestCase("Bow_Attack", .6f)]
        [TestCase("Bow_Draw", .6f)]
        [TestCase("Bell_Strike", .55f)]
        [TestCase("Bell_Recall", .48f)]
        [TestCase("Bell_Heal", .8f)]
        public void CombatClipHasHumanoidCurvesAndNoGameplayEvents(string name, float seconds)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/04.Animations/" + name + ".anim");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.humanMotion, Is.True);
            Assert.That(clip.length, Is.EqualTo(seconds).Within(.002f));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.StartsWith("MotionT.") || b.propertyName == "RootT.x" || b.propertyName == "RootT.z"))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(curve.keys.Max(k => k.value) - curve.keys.Min(k => k.value), Is.LessThan(.001f), binding.propertyName);
            }
        }

        [TestCase("Bow")]
        [TestCase("Chasa")]
        [TestCase("Singung")]
        [TestCase("Munyeo")]
        [TestCase("Player")]
        public void EveryCharacterControllerCanPresentEitherWeapon(string name)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/05.Animator/" + name + "Locomotion.controller");
            foreach (string stateName in new[] { "BowAttack", "BowDraw", "BowHold", "BowRelease", "BellStrike", "EchoRecall", "BellHeal" })
            {
                var state = controller.layers[0].stateMachine.states.Select(s => s.state).Single(s => s.name == stateName);
                Assert.That(state.motion, Is.Not.Null, stateName);
                if (state.speedParameterActive)
                {
                    Assert.That(controller.parameters.Any(p => p.name == state.speedParameter), Is.True, stateName);
                }
            }
        }
    }
}
