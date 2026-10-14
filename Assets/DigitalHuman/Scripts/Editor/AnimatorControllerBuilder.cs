#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Rebuilds <c>DigitalHuman.controller</c> from code so the parameter set stays in
    /// lockstep with <see cref="MotionIntentAnimator"/> and the planner intent vocabulary.
    /// </summary>
    public static class AnimatorControllerBuilder
    {
        public const string ControllerPath =
            "Assets/DigitalHuman/Animations/DigitalHuman.controller";

        private const string ClipSearchFolder =
            "Assets/Vefects/Stylized Female Character - Vexa/Animations";

        // The model Vexa_Visual is instantiated from; retargeted curves must resolve on it.
        private const string RigPrefabPath =
            "Assets/Vefects/Stylized Female Character - Vexa/Prefabs/Vefects_Vexa.prefab";

        private const string RetargetFolder = "Assets/DigitalHuman/Animations/Retargeted";

        // IdleChill animates this node at -90 degrees; the model keeps it at identity.
        private static readonly string[] BakedRigNodes = { "Armature" };

        private const string IdleClipName = "Idle";
        private const string IdleVariationClipName = "IdleChill";

        // The package ships no walk clip. Until one is imported the blend tree uses the
        // idle clip at both ends and ProceduralHumanoidAnimator supplies the stride.
        private const string WalkClipName = "Walk";

        // Crouch, take-off and landing in place, one second long.
        private const string JumpClipName = "JumpComplete";

        private const float ReferenceSpeed = 1.4f;

        [MenuItem("Digital Human/Animation/Rebuild Animator Controller")]
        public static string Rebuild()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                return $"FAILED: no AnimatorController at {ControllerPath}";
            }

            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rig == null)
            {
                return $"FAILED: no rig prefab at {RigPrefabPath}";
            }

            Dictionary<string, AnimationClip> clips = LoadClips();
            StringBuilder report = new StringBuilder();
            AnimationClip idle = Retargeted(clips, IdleClipName, rig.transform, report);
            if (idle == null)
            {
                return $"FAILED: clip '{IdleClipName}' not found under {ClipSearchFolder}";
            }

            AnimationClip idleVariation =
                Retargeted(clips, IdleVariationClipName, rig.transform, report) ?? idle;
            AnimationClip walk = Retargeted(clips, WalkClipName, rig.transform, report) ?? idle;
            AnimationClip jump = Retargeted(clips, JumpClipName, rig.transform, report);

            ResetController(controller);

            controller.AddParameter(MotionIntentAnimator.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MotionIntentAnimator.TurnRateParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MotionIntentAnimator.MotionIntentParameter, AnimatorControllerParameterType.Int);
            controller.AddParameter(MotionIntentAnimator.WaveParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(MotionIntentAnimator.JumpParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(MotionIntentAnimator.TalkingParameter, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
            baseLayer.name = "Base Layer";

            AnimatorState locomotion = controller.CreateBlendTreeInController(
                "Locomotion", out BlendTree tree, 0);
            tree.name = "LocomotionBlend";
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = MotionIntentAnimator.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, ReferenceSpeed);
            baseLayer.defaultState = locomotion;

            AnimatorState idleBreak = baseLayer.AddState("IdleVariation");
            idleBreak.motion = idleVariation;

            // Occasional idle variation so a stationary character is not perfectly static.
            AnimatorStateTransition toVariation = locomotion.AddTransition(idleBreak);
            toVariation.hasExitTime = true;
            toVariation.exitTime = 0.95f;
            toVariation.duration = 0.5f;
            toVariation.AddCondition(
                AnimatorConditionMode.Less, 0.05f, MotionIntentAnimator.SpeedParameter);

            AnimatorStateTransition fromVariation = idleBreak.AddTransition(locomotion);
            fromVariation.hasExitTime = true;
            fromVariation.exitTime = 0.9f;
            fromVariation.duration = 0.5f;

            AnimatorStateTransition variationInterrupt = idleBreak.AddTransition(locomotion);
            variationInterrupt.hasExitTime = false;
            variationInterrupt.duration = 0.2f;
            variationInterrupt.AddCondition(
                AnimatorConditionMode.Greater, 0.15f, MotionIntentAnimator.SpeedParameter);

            // The package has no wave. CharacterIKLayer raises and waves the arm for as long
            // as this state plays, so the clip only has to keep the rest of the body idling.
            // Its length over the state speed sets how long the wave lasts: about 3 s.
            AnimatorState waveState = baseLayer.AddState(MotionIntentAnimator.WaveStateName);
            waveState.motion = idle;
            waveState.speed = 0.75f;
            AddTriggeredTransition(locomotion, waveState, MotionIntentAnimator.WaveParameter);
            AddTriggeredTransition(idleBreak, waveState, MotionIntentAnimator.WaveParameter);

            AnimatorStateTransition fromWave = waveState.AddTransition(locomotion);
            fromWave.hasExitTime = true;
            fromWave.exitTime = 0.85f;
            fromWave.duration = 0.25f;

            if (jump != null)
            {
                AnimatorState jumpState = baseLayer.AddState(MotionIntentAnimator.JumpStateName);
                jumpState.motion = jump;
                AddTriggeredTransition(locomotion, jumpState, MotionIntentAnimator.JumpParameter);
                AddTriggeredTransition(idleBreak, jumpState, MotionIntentAnimator.JumpParameter);

                // The clip is imported looping, so leave on exit time before it starts again.
                AnimatorStateTransition fromJump = jumpState.AddTransition(locomotion);
                fromJump.hasExitTime = true;
                fromJump.exitTime = 0.92f;
                fromJump.duration = 0.15f;
            }
            else
            {
                report.AppendLine($"  WARNING clip '{JumpClipName}' not found, Jump state skipped.");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Insert(0,
                $"Rebuilt {ControllerPath}\n" +
                $"  parameters: Speed, TurnRate, MotionIntent, Wave, Jump, IsTalking\n" +
                $"  idle={idle.name} idleVariation={idleVariation.name} " +
                $"walk={walk.name}{(walk == idle ? " (placeholder, no walk clip in package)" : string.Empty)} " +
                $"wave={idle.name} + procedural arm " +
                $"jump={(jump != null ? jump.name : "none")}\n");
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Animation/Report Animator Controller")]
        public static string Report()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                return $"FAILED: no AnimatorController at {ControllerPath}";
            }

            StringBuilder report = new StringBuilder();
            report.Append("parameters: ");
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                report.Append(parameter.name).Append('(').Append(parameter.type).Append(") ");
            }

            report.AppendLine();
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                report.Append("layer ").Append(layer.name).Append(": ");
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    string motionName = child.state.motion != null
                        ? child.state.motion.name
                        : "<none>";
                    report.Append(child.state.name).Append('[').Append(motionName).Append("] ");
                }

                report.AppendLine();
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private static void AddTriggeredTransition(
            AnimatorState from,
            AnimatorState to,
            string trigger)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.15f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void ResetController(AnimatorController controller)
        {
            while (controller.parameters.Length > 0)
            {
                controller.RemoveParameter(0);
            }

            while (controller.layers.Length > 1)
            {
                controller.RemoveLayer(controller.layers.Length - 1);
            }

            if (controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states)
            {
                machine.RemoveState(child.state);
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                machine.RemoveStateMachine(child.stateMachine);
            }
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            Dictionary<string, AnimationClip> clips =
                new Dictionary<string, AnimationClip>();
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ClipSearchFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip &&
                        !clip.name.StartsWith("__preview__") &&
                        !clips.ContainsKey(clip.name))
                    {
                        clips.Add(clip.name, clip);
                    }
                }
            }

            return clips;
        }

        private static AnimationClip Resolve(
            Dictionary<string, AnimationClip> clips,
            string name)
        {
            return clips.TryGetValue(name, out AnimationClip clip) ? clip : null;
        }

        /// <summary>
        /// The imported clip copied so that its curves bind on <paramref name="rig"/>. See
        /// <see cref="ClipRetargeter"/> for why the package clips do not bind as shipped.
        /// </summary>
        private static AnimationClip Retargeted(
            Dictionary<string, AnimationClip> clips,
            string name,
            Transform rig,
            StringBuilder report)
        {
            AnimationClip source = Resolve(clips, name);
            if (source == null)
            {
                return null;
            }

            AnimationClip clip = ClipRetargeter.Retarget(
                source,
                rig,
                $"{RetargetFolder}/{name}.anim",
                BakedRigNodes,
                out string clipReport);
            report.AppendLine("  " + clipReport);
            return clip;
        }
    }
}
#endif
