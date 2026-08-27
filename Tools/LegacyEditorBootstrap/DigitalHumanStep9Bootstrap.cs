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
    public static class DigitalHumanStep9Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step9.SmokeState";
        private const string TestSystemPrompt =
            "You are EVA, a concise and friendly digital human.";

        private static AIConversationManager conversationManager;
        private static DialogueController dialogueController;
        private static PlayerInteractionDetector detector;
        private static DialogueUI dialogueUI;
        private static GameObject player;
        private static RecordingBackend backend;
        private static Task<string> pendingResponse;
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
                DialogueUI sceneUi = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                Require(sceneDetector != null, "PlayerInteractionDetector is missing.");
                Require(sceneDialogueController != null, "DialogueController is missing.");
                Require(sceneUi != null, "DialogueUI is missing.");

                AIConversationManager sceneManager =
                    digitalHuman.GetComponent<AIConversationManager>();
                if (sceneManager == null)
                {
                    sceneManager = digitalHuman.AddComponent<AIConversationManager>();
                }

                sceneManager.Configure(null, TestSystemPrompt, 16);
                sceneDialogueController.Configure(sceneDetector, sceneUi, sceneManager);
                EditorUtility.SetDirty(sceneManager);
                EditorUtility.SetDirty(sceneDialogueController);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 9 AI Conversation Manager scene setup completed.");
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
                Require(UnityEngine.Object.FindFirstObjectByType<AIConversationManager>() != null,
                    "AIConversationManager is missing.");
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
                    conversationManager = UnityEngine.Object.FindFirstObjectByType<AIConversationManager>();
                    dialogueController = UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    detector = UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    player = GameObject.FindWithTag("Player");
                    Require(conversationManager != null, "AIConversationManager is missing in Play Mode.");
                    Require(dialogueController != null, "DialogueController is missing in Play Mode.");
                    Require(detector != null, "PlayerInteractionDetector is missing in Play Mode.");
                    Require(dialogueUI != null, "DialogueUI is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");

                    conversationManager.Configure(null, TestSystemPrompt, 4);
                    backend = new RecordingBackend();
                    conversationManager.SetResponseProvider(backend);
                    player.transform.position = new Vector3(0f, 1f, -3f);
                    Physics.SyncTransforms();
                    pendingResponse = null;
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
                        Require(conversationManager.ConversationHistory.Count == 0,
                            "Conversation history should start empty.");
                        pendingResponse = conversationManager.GetResponseAsync("First");
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 2:
                        RequireCompleted("Mock reply 1");
                        Require(backend.Requests.Count == 1, "First backend request is missing.");
                        ValidateMessage(backend.Requests[0][0], ConversationMessage.SystemRole,
                            TestSystemPrompt);
                        ValidateMessage(backend.Requests[0][1], ConversationMessage.UserRole, "First");
                        Require(conversationManager.ConversationHistory.Count == 2,
                            "First conversation turn was not stored.");
                        pendingResponse = conversationManager.GetResponseAsync("Second");
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 3:
                        RequireCompleted("Mock reply 2");
                        Require(backend.Requests.Count == 2, "Second backend request is missing.");
                        Require(backend.Requests[1].Count == 4,
                            "Second request did not include prior context.");
                        ValidateMessage(backend.Requests[1][1], ConversationMessage.UserRole, "First");
                        ValidateMessage(backend.Requests[1][2], ConversationMessage.AssistantRole,
                            "Mock reply 1");
                        ValidateMessage(backend.Requests[1][3], ConversationMessage.UserRole, "Second");
                        pendingResponse = conversationManager.GetResponseAsync("Third");
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 4:
                        RequireCompleted("Mock reply 3");
                        Require(backend.Requests.Count == 3, "Third backend request is missing.");
                        Require(backend.Requests[2].Count == 4,
                            "Bounded request should contain system plus three history messages.");
                        ValidateMessage(backend.Requests[2][1], ConversationMessage.UserRole, "Second");
                        ValidateMessage(backend.Requests[2][2], ConversationMessage.AssistantRole,
                            "Mock reply 2");
                        ValidateMessage(backend.Requests[2][3], ConversationMessage.UserRole, "Third");
                        Require(conversationManager.ConversationHistory.Count == 4,
                            "Conversation history exceeded its configured limit.");
                        ValidateMessage(conversationManager.ConversationHistory[0],
                            ConversationMessage.UserRole, "Second");
                        ValidateMessage(conversationManager.ConversationHistory[3],
                            ConversationMessage.AssistantRole, "Mock reply 3");
                        conversationManager.ResetConversation();
                        Require(conversationManager.ConversationHistory.Count == 0,
                            "ResetConversation did not clear history.");
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 5:
                        Require(detector.CanInteract, "Player did not enter interaction range.");
                        Require(detector.TryInteract(), "Integrated dialogue interaction failed.");
                        Require(dialogueController.IsDialogueActive,
                            "Integrated DialogueController did not open.");
                        dialogueUI.PlayerInputField.text = "Integrated";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 6;
                        deadline = EditorApplication.timeSinceStartup + 0.2d;
                        break;

                    case 6:
                        Require(dialogueUI.DisplayedDialogue == "Mock reply 4",
                            "AI manager response did not reach DialogueUI.");
                        Require(!dialogueController.IsBusy,
                            "DialogueController remained busy after AI manager response.");
                        Require(conversationManager.ConversationHistory.Count == 2,
                            "Integrated turn was not recorded.");
                        dialogueUI.CloseButton.onClick.Invoke();
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 7;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 7:
                        Require(!detector.CanInteract, "Detector remained active after exit.");
                        Require(!dialogueController.IsDialogueActive,
                            "Dialogue remained active after close and exit.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 9 Play Mode AI Conversation Manager test passed.");
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

        private static void RequireCompleted(string expectedResponse)
        {
            Require(pendingResponse != null, "Conversation task was not created.");
            Require(pendingResponse.Status == TaskStatus.RanToCompletion,
                pendingResponse.Exception != null
                    ? pendingResponse.Exception.ToString()
                    : "Conversation task did not complete.");
            Require(pendingResponse.Result == expectedResponse,
                $"Unexpected conversation response: {pendingResponse.Result}");
        }

        private static void ValidateMessage(
            ConversationMessage message,
            string expectedRole,
            string expectedContent)
        {
            Require(message != null, "Conversation message is null.");
            Require(message.Role == expectedRole,
                $"Expected role {expectedRole}, received {message.Role}.");
            Require(message.Content == expectedContent,
                $"Expected content '{expectedContent}', received '{message.Content}'.");
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

        private sealed class RecordingBackend : IConversationBackend
        {
            public readonly List<List<ConversationMessage>> Requests =
                new List<List<ConversationMessage>>();

            public Task<string> SendMessageAsync(
                IReadOnlyList<ConversationMessage> messages,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(new List<ConversationMessage>(messages));
                return Task.FromResult($"Mock reply {Requests.Count}");
            }
        }
    }
}
#endif
