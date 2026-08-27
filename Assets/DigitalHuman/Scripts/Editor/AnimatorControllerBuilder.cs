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

        private const string IdleClipName = "Idle";
        private const string IdleVariationClipName = "IdleChill";

        // The package ships no walk clip. Until one is imported the blend tree uses the
        // idle clip at both ends and ProceduralHumanoidAnimator supplies the stride.
        private const string WalkClipName = "Walk";

        // Closest thing to a greeting in the shipped set: a short one-handed flick.
        private const string WaveClipName = "SnappySpell";

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

            Dictionary<string, AnimationClip> clips = LoadClips();
            AnimationClip idle = Resolve(clips, IdleClipName);
            if (idle == null)
            {
                return $"FAILED: clip '{IdleClipName}' not found under {ClipSearchFolder}";
            }

            AnimationClip idleVariation = Resolve(clips, IdleVariationClipName) ?? idle;
            AnimationClip walk = Resolve(clips, WalkClipName) ?? idle;
            AnimationClip wave = Resolve(clips, WaveClipName);

            ResetController(controller);

            controller.AddParameter(MotionIntentAnimator.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MotionIntentAnimator.TurnRateParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MotionIntentAnimator.MotionIntentParameter, AnimatorControllerParameterType.Int);
            controller.AddParameter(MotionIntentAnimator.WaveParameter, AnimatorControllerParameterType.Trigger);
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

            StringBuilder report = new StringBuilder();
            if (wave != null)
            {
                AnimatorState waveState = baseLayer.AddState("Wave");
                waveState.motion = wave;

                AnimatorStateTransition toWave = locomotion.AddTransition(waveState);
                toWave.hasExitTime = false;
                toWave.duration = 0.15f;
                toWave.AddCondition(AnimatorConditionMode.If, 0f, MotionIntentAnimator.WaveParameter);

                AnimatorStateTransition variationToWave = idleBreak.AddTransition(waveState);
                variationToWave.hasExitTime = false;
                variationToWave.duration = 0.15f;
                variationToWave.AddCondition(
                    AnimatorConditionMode.If, 0f, MotionIntentAnimator.WaveParameter);

                AnimatorStateTransition fromWave = waveState.AddTransition(locomotion);
                fromWave.hasExitTime = true;
                fromWave.exitTime = 0.85f;
                fromWave.duration = 0.25f;
            }
            else
            {
                report.AppendLine($"  WARNING clip '{WaveClipName}' not found, Wave state skipped.");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Insert(0,
                $"Rebuilt {ControllerPath}\n" +
                $"  parameters: Speed, TurnRate, MotionIntent, Wave, IsTalking\n" +
                $"  idle={idle.name} idleVariation={idleVariation.name} " +
                $"walk={walk.name}{(walk == idle ? " (placeholder, no walk clip in package)" : string.Empty)} " +
                $"wave={(wave != null ? wave.name : "none")}\n");
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
    }
}
#endif
