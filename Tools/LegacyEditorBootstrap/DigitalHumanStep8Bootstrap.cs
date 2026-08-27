#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep8Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step8.SmokeState";

        private static PlayerInteractionDetector detector;
        private static DialogueController dialogueController;
        private static DigitalHumanController stateController;
        private static DialogueUI dialogueUI;
        private static GameObject player;
        private static readonly List<DigitalHumanState> ObservedStates =
            new List<DigitalHumanState>();
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
                Transform trigger = digitalHuman.transform.Find("InteractionTrigger");
                Require(trigger != null, "InteractionTrigger is missing.");

                PlayerInteractionDetector sceneDetector =
                    trigger.GetComponent<PlayerInteractionDetector>();
                DialogueController sceneDialogueController =
                    digitalHuman.GetComponent<DialogueController>();
                Require(sceneDetector != null, "PlayerInteractionDetector is missing.");
                Require(sceneDialogueController != null, "DialogueController is missing.");

                DigitalHumanController sceneStateController =
                    digitalHuman.GetComponent<DigitalHumanController>();
                if (sceneStateController == null)
                {
                    sceneStateController = digitalHuman.AddComponent<DigitalHumanController>();
                }

                sceneStateController.Configure(sceneDetector, sceneDialogueController, 0.65f);
                EditorUtility.SetDirty(sceneStateController);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 8 State Machine scene setup completed.");
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
                Require(UnityEngine.Object.FindFirstObjectByType<DigitalHumanController>() != null,
                    "DigitalHumanController is missing.");
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
                    detector = UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    dialogueController = UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    stateController = UnityEngine.Object.FindFirstObjectByType<DigitalHumanController>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    player = GameObject.FindWithTag("Player");
                    Require(detector != null, "PlayerInteractionDetector is missing in Play Mode.");
                    Require(dialogueController != null, "DialogueController is missing in Play Mode.");
                    Require(stateController != null, "DigitalHumanController is missing in Play Mode.");
                    Require(dialogueUI != null, "DialogueUI is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");

                    dialogueController.SetConversationService(new DelayedConversationService());
                    ObservedStates.Clear();
                    ObservedStates.Add(stateController.CurrentState);
                    stateController.StateChanged += OnStateChanged;
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
                        Require(stateController.CurrentState == DigitalHumanState.Idle,
                            "NPC should be Idle while Player is outside.");
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        Require(stateController.CurrentState == DigitalHumanState.PlayerNearby,
                            "NPC did not enter PlayerNearby.");
                        Require(detector.TryInteract(), "Interaction request failed.");
                        Require(stateController.CurrentState == DigitalHumanState.Listening,
                            "NPC did not enter Listening.");
                        dialogueUI.PlayerInputField.text = "State test";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.12d;
                        break;

                    case 3:
                        Require(stateController.CurrentState == DigitalHumanState.Thinking,
                            "NPC did not remain in Thinking during the request.");
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.38d;
                        break;

                    case 4:
                        Require(stateController.CurrentState == DigitalHumanState.Speaking,
                            "NPC did not enter Speaking after the response.");
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.75d;
                        break;

                    case 5:
                        Require(stateController.CurrentState == DigitalHumanState.Listening,
                            "NPC did not return to Listening after speaking.");
                        dialogueUI.CloseButton.onClick.Invoke();
                        Require(stateController.CurrentState == DigitalHumanState.PlayerNearby,
                            "NPC did not return to PlayerNearby after dialogue close.");
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 6;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 6:
                        Require(stateController.CurrentState == DigitalHumanState.Idle,
                            "NPC did not return to Idle after Player exit.");
                        Require(ContainsOrderedSequence(
                                DigitalHumanState.Idle,
                                DigitalHumanState.PlayerNearby,
                                DigitalHumanState.Listening,
                                DigitalHumanState.Thinking,
                                DigitalHumanState.Speaking,
                                DigitalHumanState.Listening,
                                DigitalHumanState.PlayerNearby,
                                DigitalHumanState.Idle),
                            "NPC state transition sequence was incomplete or out of order.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 8 Play Mode State Machine test passed.");
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

        private static void OnStateChanged(DigitalHumanState _, DigitalHumanState nextState)
        {
            ObservedStates.Add(nextState);
        }

        private static bool ContainsOrderedSequence(params DigitalHumanState[] expected)
        {
            int expectedIndex = 0;
            for (int i = 0; i < ObservedStates.Count && expectedIndex < expected.Length; i++)
            {
                if (ObservedStates[i] == expected[expectedIndex])
                {
                    expectedIndex++;
                }
            }

            return expectedIndex == expected.Length;
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
                return "State transition response.";
            }
        }
    }
}
#endif
