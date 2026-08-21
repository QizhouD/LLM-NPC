#if UNITY_EDITOR
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep7Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step7.SmokeState";

        private static PlayerInteractionDetector detector;
        private static DialogueController controller;
        private static DialogueUI dialogueUI;
        private static GameObject player;
        private static int runtimeStage;
        private static double deadline;
        private static bool responseReceived;

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
                DialogueUI sceneUi = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                Require(sceneDetector != null, "PlayerInteractionDetector is missing.");
                Require(sceneUi != null, "DialogueUI is missing.");

                DialogueController sceneController =
                    digitalHuman.GetComponent<DialogueController>();
                if (sceneController == null)
                {
                    sceneController = digitalHuman.AddComponent<DialogueController>();
                }

                sceneController.Configure(sceneDetector, sceneUi, null);
                EditorUtility.SetDirty(sceneController);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 7 Dialogue Controller scene setup completed.");
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
                Require(UnityEngine.Object.FindFirstObjectByType<DialogueController>() != null,
                    "DialogueController is missing.");
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
                    controller = UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    player = GameObject.FindWithTag("Player");
                    Require(detector != null, "PlayerInteractionDetector is missing in Play Mode.");
                    Require(controller != null, "DialogueController is missing in Play Mode.");
                    Require(dialogueUI != null, "DialogueUI is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");

                    controller.SetConversationService(new MockConversationService());
                    responseReceived = false;
                    controller.NpcResponseReceived += OnNpcResponseReceived;
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
                        Require(!detector.CanInteract, "Detector should be inactive outside range.");
                        Require(!controller.IsDialogueActive, "Dialogue should start inactive.");
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        Require(detector.CanInteract, "Player did not enter interaction range.");
                        Require(dialogueUI.IsInteractionPromptVisible,
                            "Interaction prompt did not appear.");
                        Require(detector.TryInteract(), "Interaction request failed.");
                        Require(controller.IsDialogueActive && dialogueUI.IsOpen,
                            "Dialogue Controller did not open the dialogue.");
                        dialogueUI.PlayerInputField.text = "Hi EVA";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.25d;
                        break;

                    case 3:
                        Require(responseReceived, "NPC response event was not raised.");
                        Require(dialogueUI.DisplayedDialogue == MockConversationService.Response,
                            "Mock response was not displayed.");
                        Require(!controller.IsBusy, "Controller remained busy after response.");
                        Require(dialogueUI.PlayerInputField.interactable &&
                                dialogueUI.SendButton.interactable,
                            "Input controls were not restored after response.");
                        dialogueUI.CloseButton.onClick.Invoke();
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.15d;
                        break;

                    case 4:
                        Require(!controller.IsDialogueActive && !dialogueUI.IsOpen,
                            "Dialogue did not close cleanly.");
                        Require(dialogueUI.IsInteractionPromptVisible,
                            "Prompt should return while Player remains nearby.");
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 5:
                        Require(!detector.CanInteract, "Detector remained active after exit.");
                        Require(!dialogueUI.IsInteractionPromptVisible,
                            "Prompt remained visible after exit.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 7 Play Mode Dialogue Controller test passed.");
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

        private static void OnNpcResponseReceived(string response)
        {
            responseReceived = response == MockConversationService.Response;
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

        private sealed class MockConversationService : IConversationService
        {
            public const string Response = "Hello from mock.";

            public Task<string> GetResponseAsync(
                string playerMessage,
                CancellationToken cancellationToken = default)
            {
                Require(playerMessage == "Hi EVA", "Controller forwarded the wrong message.");
                return Task.FromResult(Response);
            }
        }
    }
}
#endif
