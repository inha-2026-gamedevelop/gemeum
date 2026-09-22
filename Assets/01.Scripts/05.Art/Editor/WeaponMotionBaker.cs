using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Geumeum.Art.EditorTools
{
    /// <summary>Purchased humanoid curves are resampled into owned, event-free combat clips</summary>
    public static class WeaponMotionBaker
    {
        private const string Source = "Assets/99.ThirdParty/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/";
        private const string Output = "Assets/04.Animations/";

        [MenuItem("Geumeum/Art/Bake Bow and Bell Motions")]
        public static void Bake()
        {
            AnimationClip shot = Load("2Hand-Bow", "2Hand-Bow-Attack1");
            float release = AnimationUtility.GetAnimationEvents(shot).First(e => e.functionName == "Shoot").time;
            BakeClip("Bow_Attack", shot, shot, .6f, .25f, release, release, true);
            BakeClip("Bow_Draw", shot, shot, .6f, .6f, release, release, true);
            var hold = UnityEngine.Object.Instantiate(Clip("Bow_Draw"));
            foreach (var binding in AnimationUtility.GetCurveBindings(hold))
            {
                float value = AnimationUtility.GetEditorCurve(hold, binding).Evaluate(hold.length);
                AnimationUtility.SetEditorCurve(hold, binding, AnimationCurve.Constant(0f, .6f, value));
            }
            Finish("Bow_Hold", hold, true, true);
            BakeClip("Bow_Release", shot, shot, .35f, .35f, shot.length, shot.length, true, false, release);
            Copy("Bow_Idle", Load("2Hand-Bow", "2Hand-Bow-Idle"), true, true);
            Copy("Bow_Walk", Load("2Hand-Bow", "2Hand-Bow-Walk"), true, true);
            Copy("Bow_Run", Load("2Hand-Bow", "2Hand-Bow-Run-Foward"), true, true);
            AnimationClip strike = Load("Unarmed", "Unarmed-Cast-R-Attack1", "Unarmed-Cast-R-Attack1_start");
            AnimationClip settle = Load("Unarmed", "Unarmed-Cast-R-Attack1", "Unarmed-Cast-R-Attack1_end");
            BakeClip("Bell_Strike", strike, settle, .55f, .2f, strike.length, 0f, false);
            AnimationClip recall = Load("Unarmed", "Unarmed-Cast-R-Attack2", "Unarmed-Cast-R-Attack2_start");
            BakeClip("Bell_Recall", recall, Load("Unarmed", "Unarmed-Cast-R-Attack2", "Unarmed-Cast-R-Attack2_end"), .48f, .18f, recall.length, 0f, false);
            AnimationClip heal = Load("Unarmed", "Unarmed-Cast-R-Buff1", "Unarmed-Cast-R-Buff1_start");
            BakeClip("Bell_Heal", heal, Load("Unarmed", "Unarmed-Cast-R-Buff1", "Unarmed-Cast-R-Buff1_end"), .8f, .4f, heal.length, 0f, false);
            foreach (string name in new[] { "Bow", "Singung", "Munyeo", "Chasa", "Player" })
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/05.Animator/" + name + "Locomotion.controller");
                Wire(controller, "BowAttack", "Bow_Attack", "BowAttackSpeed", true);
                Wire(controller, "BowDraw", "Bow_Draw", "BowDrawSpeed", false);
                Wire(controller, "BowHold", "Bow_Hold", null, false);
                Wire(controller, "BowRelease", "Bow_Release", "BowReleaseSpeed", true);
                Wire(controller, "BellStrike", "Bell_Strike", "BellStrikeSpeed", true);
                Wire(controller, "EchoRecall", "Bell_Recall", "EchoRecallSpeed", true);
                Wire(controller, "BellHeal", "Bell_Heal", "BellHealSpeed", true);
                if (name == "Bow")
                {
                    var tree = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state.motion as BlendTree;
                    if (tree != null)
                    {
                        var children = tree.children;
                        for (int i = 0; i < children.Length; ++i)
                        {
                            children[i].motion = Clip(children[i].threshold < .5f ? "Bow_Idle" : children[i].threshold < 1.5f ? "Bow_Walk" : "Bow_Run");
                        }

                        tree.children = children;
                        EditorUtility.SetDirty(tree);
                    }
                }
                EditorUtility.SetDirty(controller);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Geumeum/Art/Bake Weapon R Motions")]
        public static void BakeSkills()
        {
            AnimationClip sword = Load("1Hand-Sword", "Sword-Attack-R2");
            float swordHit = AnimationUtility.GetAnimationEvents(sword).First(e => e.functionName == "Hit").time;
            var combat = AssetDatabase.LoadAssetAtPath<Geumeum.Data.CombatConfigSO>("Assets/09.Data/Combat/CombatConfigSO.asset");
            var swordSettings = combat.Skill(Geumeum.Common.WeaponKind.Sword);
            BakeClip("SwordSkill", sword, sword, swordSettings.Startup + swordSettings.Recovery, swordSettings.Startup, swordHit, swordHit, false);
            AnimationClip counter = Load("1Hand-Sword", "Sword-Attack-R1");
            float counterHit = AnimationUtility.GetAnimationEvents(counter).First(e => e.functionName == "Hit").time;
            BakeClip("SwordCounter", counter, counter, combat.CounterStartup + swordSettings.Recovery, combat.CounterStartup, counterHit, counterHit, false);
            BakeBowSlide();
            AnimationClip cast = Load("Unarmed", "Unarmed-Cast-R-AOE1", "Unarmed-Cast-R-AOE1_start");
            AnimationClip end = Load("Unarmed", "Unarmed-Cast-R-AOE1", "Unarmed-Cast-R-AOE1_end");
            BakeClip("BellField", cast, end, .50f, .25f, cast.length, 0f, false);
            foreach (string name in new[] { "Bow", "Singung", "Munyeo", "Chasa", "Player" })
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/05.Animator/" + name + "Locomotion.controller");
                foreach (string state in new[] { "SwordSkill", "SwordCounter", "BowStab", "BellField" }) { Wire(controller, state, state, state + "Speed", true); }
                EditorUtility.SetDirty(controller);
            }
            // Serialize new fields into the existing balance asset as reviewable data
            var config = AssetDatabase.LoadAssetAtPath<Geumeum.Data.CombatConfigSO>("Assets/09.Data/Combat/CombatConfigSO.asset");
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private static void BakeBowSlide()
        {
            AnimationClip dash = Load("2Hand-Bow", "2Hand-Bow-Dash-Forward");
            AnimationClip crouch = Load("2Hand-Bow", "2Hand-Bow-Crouch-Idle");
            AnimationClip slash = Load("1Hand-Dagger", "Dagger-Attack-R2");
            var config = AssetDatabase.LoadAssetAtPath<Geumeum.Data.CombatConfigSO>("Assets/09.Data/Combat/CombatConfigSO.asset");
            var skill = config.Skill(Geumeum.Common.WeaponKind.Bow);
            float begin = skill.Startup, end = begin + config.BowSkillSlideDuration;
            float duration = end + skill.Recovery;
            var clip = new AnimationClip { frameRate = 60f };
            foreach (var binding in AnimationUtility.GetCurveBindings(dash))
            {
                string property = binding.propertyName;
                AnimationCurve body = AnimationUtility.GetEditorCurve(dash, binding);
                AnimationCurve hand = AnimationUtility.GetEditorCurve(slash, binding);
                AnimationCurve grounded = AnimationUtility.GetEditorCurve(crouch, binding);
                bool lowerBody = property == "RootT.y" || property.Contains(" Leg ") || property.Contains(" Foot ") ||
                    property.Contains(" Toes ") || property.StartsWith("LeftFoot") || property.StartsWith("RightFoot");
                bool slashArm = property.StartsWith("Right Arm") || property.StartsWith("Right Forearm") ||
                    property.StartsWith("Right Hand") || property.StartsWith("Right Shoulder") || property.StartsWith("RightHand");
                int frames = Mathf.CeilToInt(duration * 60);
                var curve = new AnimationCurve();
                for (int i = 0; i <= frames; ++i)
                {
                    float t = duration * i / frames;
                    float bodyTime = t < begin ? Mathf.Lerp(0, .16f, t / begin) : t < end ?
                        Mathf.Lerp(.16f, .24f, (t - begin) / (end - begin)) : Mathf.Lerp(.24f, dash.length, (t - end) / skill.Recovery);
                    float value = body.Evaluate(bodyTime);
                    float lowPose = Mathf.SmoothStep(0, 1, t / begin) * (1 - Mathf.SmoothStep(0, 1, (t - end) / skill.Recovery));
                    // The source dash hops. Use its crouched stance to keep the slide grounded instead
                    if (lowerBody && grounded != null) { value = Mathf.Lerp(value, grounded.Evaluate(0), lowPose); }
                    if (slashArm && hand != null)
                    {
                        float slashTime = t < begin ? Mathf.Lerp(0, .30f, t / begin) : t < end ?
                            Mathf.Lerp(.30f, .64f, (t - begin) / (end - begin)) : Mathf.Lerp(.64f, slash.length, (t - end) / skill.Recovery);
                        value = Mathf.Lerp(value, hand.Evaluate(slashTime), lowPose);
                    }
                    if (property == "RootT.x" || property == "RootT.z" || property.StartsWith("MotionT."))
                    {
                        value = body.Evaluate(0);
                    }

                    curve.AddKey(new Keyframe(t, value));
                }
                for (int i = 0; i < curve.length; ++i)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            // Mirror the complete pose once: bow in right hand, scratching arrow in left
            Finish("BowStab", clip, false, true);
        }

        private static AnimationClip Load(string folder, string file, string clipName = null)
        {
            return AssetDatabase.LoadAllAssetsAtPath(Source + folder + "/RPG-Character@" + file + ".FBX")
                .OfType<AnimationClip>().First(c => clipName == null ? !c.name.StartsWith("__preview") : c.name == clipName);
        }

        private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Output + name + ".anim");

        private static void Copy(string name, AnimationClip source, bool loop, bool mirror)
        {
            var clip = UnityEngine.Object.Instantiate(source);
            Finish(name, clip, loop, mirror);
        }

        private static void BakeClip(string name, AnimationClip before, AnimationClip after,
            float duration, float impact, float beforeEnd, float afterStart, bool mirror, bool reverse = false, float beforeStart = 0f)
        {
            var clip = new AnimationClip { frameRate = 60f };
            foreach (var binding in AnimationUtility.GetCurveBindings(before))
            {
                AnimationCurve a = AnimationUtility.GetEditorCurve(before, binding);
                AnimationCurve b = AnimationUtility.GetEditorCurve(after, binding) ?? a;
                int frames = Mathf.RoundToInt(duration * 60f);
                var keys = new Keyframe[frames + 1];
                for (int i = 0; i <= frames; ++i)
                {
                    float t = duration * i / frames;
                    float value = t <= impact ? a.Evaluate(Mathf.Lerp(beforeStart, beforeEnd, t / impact)) :
                        b.Evaluate(Mathf.Lerp(afterStart, after.length, (t - impact) / (duration - impact)));
                    if (reverse) { value = a.Evaluate(t <= impact ? (1f - t / impact) * beforeEnd : 0f); }
                    string property = binding.propertyName;
                    // Keep weight transfer, pelvis rotation and knees together. Only planar root travel is server-owned
                    if (property == "RootT.x" || property == "RootT.z" || property.StartsWith("MotionT."))
                    {
                        value = a.Evaluate(beforeStart);
                    }

                    keys[i] = new Keyframe(t, value);
                }
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; ++i)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            Finish(name, clip, false, mirror);
        }

        private static void Finish(string name, AnimationClip clip, bool loop, bool mirror)
        {
            clip.name = name;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = clip.length;
            settings.loopTime = loop;
            settings.mirror = mirror; // Existing game bows are held in the right hand
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
            settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var existing = Clip(name);
            if (existing == null) { AssetDatabase.CreateAsset(clip, Output + name + ".anim"); }
            else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(clip); }
        }

        private static void Wire(AnimatorController controller, string name, string clip, string speed, bool exit)
        {
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = Clip(clip);
            state.speed = 1f;
            if (speed != null)
            {
                if (!controller.parameters.Any(p => p.name == speed)) { controller.AddParameter(speed, AnimatorControllerParameterType.Float); }
                state.speedParameter = speed;
                state.speedParameterActive = true;
            }
            if (exit && state.transitions.Length == 0)
            {
                var transition = state.AddTransition(machine.states.First(s => s.state.name == "Locomotion").state);
                transition.hasExitTime = true;
                transition.exitTime = 1f;
                transition.hasFixedDuration = true;
                transition.duration = .05f;
            }
            EditorUtility.SetDirty(state);
        }
    }
}
