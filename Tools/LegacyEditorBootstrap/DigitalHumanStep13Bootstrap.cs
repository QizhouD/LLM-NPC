#if UNITY_EDITOR
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep13Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ControllerPath =
            "Assets/DigitalHuman/Animations/DigitalHuman.controller";
        private const string SmokeStateKey = "DigitalHuman.Step13.SmokeState";

        private static PlayerInteractionDetector detector;
        private static DialogueController dialogueController;
        private static DigitalHumanController stateController;
        private static DigitalHumanAnimator digitalHumanAnimator;
        private static DialogueUI dialogueUI;
        private static GameObject player;
        private static int runtimeStage;
        private static double deadline;

        [InitializeOnLoadMethod]
        private static void ResumePendingSmokeTest()
        {
            if (SessionState.GetInt(SmokeStateKey, 0) == 0)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void ConfigureScene()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject digitalHuman = GameObject.Find("DigitalHuman");
                Require(digitalHuman != null, "DigitalHuman root is missing.");

                Animator animator = digitalHuman.GetComponent<Animator>();
                DigitalHumanController sceneStateController =
                    digitalHuman.GetComponent<DigitalHumanController>();
                Require(animator != null, "Animator is missing.");
                Require(sceneStateController != null, "DigitalHumanController is missing.");

                AnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (controller == null)
                {
                    controller =
                        AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                }

                AddBoolParameterIfMissing(controller, "IsTalking");
                AddBoolParameterIfMissing(controller, "IsListening");
                AddBoolParameterIfMissing(controller, "IsThinking");
                AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
                if (stateMachine.states.Length == 0)
                {
                    AnimatorState idleState = stateMachine.AddState("Idle");
                    stateMachine.defaultState = idleState;
                }

                if (animator.runtimeAnimatorController == null)
                {
                    animator.runtimeAnimatorController = controller;
                }

                DigitalHumanAnimator animationBridge =
                    digitalHuman.GetComponent<DigitalHumanAnimator>();
                if (animationBridge == null)
                {
                    animationBridge = digitalHuman.AddComponent<DigitalHumanAnimator>();
                }

                animationBridge.Configure(sceneStateController, animator);
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(animator);
                EditorUtility.SetDirty(animationBridge);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 13 Animation integration scene setup completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void StartPlayModeSmokeTest()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Require(UnityEngine.Object.FindFirstObjectByType<DigitalHumanAnimator>() != null,
                    "DigitalHumanAnimator is missing.");
                SessionState.SetInt(SmokeStateKey, 1);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.EraseInt(SmokeStateKey);
                EditorApplication.Exit(1);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (SessionState.GetInt(SmokeStateKey, 0) == 0)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    detector =
                        UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    dialogueController =
                        UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    stateController =
                        UnityEngine.Object.FindFirstObjectByType<DigitalHumanController>();
                    digitalHumanAnimator =
                        UnityEngine.Object.FindFirstObjectByType<DigitalHumanAnimator>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    player = GameObject.FindWithTag("Player");
                    Require(detector != null, "PlayerInteractionDetector is missing.");
                    Require(dialogueController != null, "DialogueController is missing.");
                    Require(stateController != null, "DigitalHumanController is missing.");
                    Require(digitalHumanAnimator != null, "DigitalHumanAnimator is missing.");
                    Require(dialogueUI != null, "DialogueUI is missing.");
                    Require(player != null, "Player is missing.");

                    dialogueController.SetConversationService(
                        new DelayedConversationService());
                    player.transform.position = new Vector3(0f, 1f, -3f);
                    Physics.SyncTransforms();
                    runtimeStage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.4d;
                    EditorApplication.update -= RunSmokeTestUpdate;
                    EditorApplication.update += RunSmokeTestUpdate;
                }
                catch (Exception exception)
                {
                    FailSmokeTest(exception);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                int exitCode = SessionState.GetInt(SmokeStateKey, -1) == 2 ? 0 : 1;
                SessionState.EraseInt(SmokeStateKey);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= RunSmokeTestUpdate;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunSmokeTestUpdate()
        {
            if (EditorApplication.timeSinceStartup < deadline)
            {
                return;
            }

            try
            {
                switch (runtimeStage)
                {
                    case 1:
                        RequireState(DigitalHumanState.Idle, false, false, false);
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        RequireState(DigitalHumanState.PlayerNearby, false, false, false);
                        Require(detector.TryInteract(), "Interaction request failed.");
                        RequireState(DigitalHumanState.Listening, false, true, false);
                        dialogueUI.PlayerInputField.text = "Animation test";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.12d;
                        break;

                    case 3:
                        RequireState(DigitalHumanState.Thinking, false, false, true);
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.35d;
                        break;

                    case 4:
                        RequireState(DigitalHumanState.Speaking, true, false, false);
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.75d;
                        break;

                    case 5:
                        RequireState(DigitalHumanState.Listening, false, true, false);
                        dialogueUI.CloseButton.onClick.Invoke();
                        RequireState(DigitalHumanState.PlayerNearby, false, false, false);
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 6;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 6:
                        RequireState(DigitalHumanState.Idle, false, false, false);
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 13 Play Mode Animation integration test passed.");
                        EditorApplication.update -= RunSmokeTestUpdate;
                        EditorApplication.ExitPlaymode();
                        break;
                }
            }
            catch (Exception exception)
            {
                FailSmokeTest(exception);
            }
        }

        private static void RequireState(
            DigitalHumanState expectedState,
            bool talking,
            bool listening,
            bool thinking)
        {
            Require(stateController.CurrentState == expectedState,
                $"Expected state {expectedState}, received {stateController.CurrentState}.");
            Require(digitalHumanAnimator.IsTalking == talking,
                $"IsTalking mismatch in {expectedState}.");
            Require(digitalHumanAnimator.IsListening == listening,
                $"IsListening mismatch in {expectedState}.");
            Require(digitalHumanAnimator.IsThinking == thinking,
                $"IsThinking mismatch in {expectedState}.");
        }

        private static void AddBoolParameterIfMissing(
            AnimatorController controller,
            string parameterName)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    Require(parameters[i].type == AnimatorControllerParameterType.Bool,
                        $"Animator parameter {parameterName} must be Bool.");
                    return;
                }
            }

            controller.AddParameter(parameterName, AnimatorControllerParameterType.Bool);
        }

        private static void FailSmokeTest(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(SmokeStateKey, -1);
            EditorApplication.update -= RunSmokeTestUpdate;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.Exit(1);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class DelayedConversationService : IConversationService
        {
            public async Task<string> GetResponseAsync(
                string playerMessage,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(300, cancellationToken);
                return "Animation response.";
            }
        }
    }
}
#endif
