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
    public static class DigitalHumanStep12Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PersonaPath =
            "Assets/DigitalHuman/ScriptableObjects/EVA Persona.asset";
        private const string SmokeStateKey = "DigitalHuman.Step12.SmokeState";
        private const string TestEnvironmentVariable =
            "DIGITAL_HUMAN_STEP12_TEST_KEY";
        private const string TestApiKey = "step12-test-token-not-a-secret";

        private static AIConversationManager manager;
        private static OpenAILLMService llmService;
        private static DialogueController dialogueController;
        private static DigitalHumanController stateController;
        private static PlayerInteractionDetector detector;
        private static DialogueUI dialogueUI;
        private static GameObject player;
        private static RecordingTransport transport;
        private static int runtimeStage;
        private static double deadline;
        private static string previousApiKey;

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

                DigitalHumanPersona persona =
                    AssetDatabase.LoadAssetAtPath<DigitalHumanPersona>(PersonaPath);
                AIConversationManager sceneManager =
                    digitalHuman.GetComponent<AIConversationManager>();
                OpenAILLMService sceneService =
                    digitalHuman.GetComponent<OpenAILLMService>();
                DialogueController sceneDialogue =
                    digitalHuman.GetComponent<DialogueController>();
                Transform trigger = digitalHuman.transform.Find("InteractionTrigger");
                PlayerInteractionDetector sceneDetector =
                    trigger != null ? trigger.GetComponent<PlayerInteractionDetector>() : null;
                DialogueUI sceneUi = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();

                Require(persona != null, "EVA Persona asset is missing.");
                Require(sceneManager != null, "AIConversationManager is missing.");
                Require(sceneService != null, "OpenAILLMService is missing.");
                Require(sceneDialogue != null, "DialogueController is missing.");
                Require(sceneDetector != null, "PlayerInteractionDetector is missing.");
                Require(sceneUi != null, "DialogueUI is missing.");

                sceneManager.Configure(persona, sceneService, 16);
                sceneDialogue.Configure(sceneDetector, sceneUi, sceneManager);
                EditorUtility.SetDirty(sceneManager);
                EditorUtility.SetDirty(sceneDialogue);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 12 LLM connection scene setup completed.");
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
                AIConversationManager sceneManager =
                    UnityEngine.Object.FindFirstObjectByType<AIConversationManager>();
                Require(sceneManager != null && sceneManager.Persona != null,
                    "Persona is not connected to AIConversationManager.");
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
                    manager = UnityEngine.Object.FindFirstObjectByType<AIConversationManager>();
                    llmService = UnityEngine.Object.FindFirstObjectByType<OpenAILLMService>();
                    dialogueController =
                        UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    stateController =
                        UnityEngine.Object.FindFirstObjectByType<DigitalHumanController>();
                    detector =
                        UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    player = GameObject.FindWithTag("Player");
                    Require(manager != null, "AIConversationManager is missing in Play Mode.");
                    Require(llmService != null, "OpenAILLMService is missing in Play Mode.");
                    Require(dialogueController != null,
                        "DialogueController is missing in Play Mode.");
                    Require(stateController != null,
                        "DigitalHumanController is missing in Play Mode.");
                    Require(detector != null, "PlayerInteractionDetector is missing.");
                    Require(dialogueUI != null, "DialogueUI is missing.");
                    Require(player != null, "Player is missing.");
                    Require(manager.Persona != null, "Runtime Persona reference is missing.");
                    Require(manager.SystemPrompt == manager.Persona.BuildSystemPrompt(),
                        "AI manager is not using the Persona prompt.");

                    previousApiKey =
                        Environment.GetEnvironmentVariable(TestEnvironmentVariable);
                    Environment.SetEnvironmentVariable(
                        TestEnvironmentVariable,
                        TestApiKey);
                    llmService.Configure(
                        "https://api.openai.com/v1/responses",
                        "gpt-5.6-terra",
                        TestEnvironmentVariable,
                        10,
                        160,
                        "low");
                    transport = new RecordingTransport();
                    transport.EnqueueResponse(new LLMHttpResponse(
                        200,
                        "{\"id\":\"resp_connected\",\"status\":\"completed\"," +
                        "\"output\":[{\"type\":\"message\",\"role\":\"assistant\"," +
                        "\"content\":[{\"type\":\"output_text\"," +
                        "\"text\":\"Connected response.\"}]}]}"));
                    llmService.SetTransport(transport);
                    manager.ResetConversation();

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
                RestoreEnvironment();
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
                        Require(!detector.CanInteract,
                            "Player should start outside interaction range.");
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        Require(detector.CanInteract, "Player did not enter interaction range.");
                        Require(detector.TryInteract(), "Interaction request failed.");
                        Require(dialogueController.IsDialogueActive,
                            "Dialogue did not open.");
                        dialogueUI.PlayerInputField.text = "Are you EVA?";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.2d;
                        break;

                    case 3:
                        Require(dialogueUI.DisplayedDialogue == "Connected response.",
                            "LLM response did not reach DialogueUI.");
                        Require(manager.ConversationHistory.Count == 2,
                            "Connected conversation turn was not stored.");
                        Require(transport.Requests.Count == 1,
                            "AI manager did not call OpenAILLMService.");
                        Require(transport.Requests[0].BearerToken == TestApiKey,
                            "OpenAILLMService did not use the environment key.");
                        Require(transport.Requests[0].JsonBody.Contains("You are EVA"),
                            "LLM request omitted the Persona prompt.");
                        Require(transport.Requests[0].JsonBody.Contains("Are you EVA?"),
                            "LLM request omitted the player message.");
                        Require(stateController.CurrentState == DigitalHumanState.Speaking,
                            "NPC did not enter Speaking after LLM response.");
                        dialogueUI.CloseButton.onClick.Invoke();
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 4:
                        Require(!dialogueController.IsDialogueActive,
                            "Dialogue remained open after close.");
                        Require(stateController.CurrentState == DigitalHumanState.Idle,
                            "NPC did not return to Idle.");
                        RestoreEnvironment();
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 12 Play Mode LLM connection test passed.");
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

        private static void RestoreEnvironment()
        {
            Environment.SetEnvironmentVariable(
                TestEnvironmentVariable,
                previousApiKey);
        }

        private static void FailSmokeTest(Exception exception)
        {
            Debug.LogException(exception);
            RestoreEnvironment();
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

        private sealed class RecordingTransport : ILLMHttpTransport
        {
            private readonly Queue<LLMHttpResponse> responses =
                new Queue<LLMHttpResponse>();

            public readonly List<CapturedRequest> Requests =
                new List<CapturedRequest>();

            public void EnqueueResponse(LLMHttpResponse response)
            {
                responses.Enqueue(response);
            }

            public Task<LLMHttpResponse> PostJsonAsync(
                string url,
                string jsonBody,
                string bearerToken,
                int timeoutSeconds,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(responses.Count > 0, "No mock response was queued.");
                Requests.Add(new CapturedRequest(jsonBody, bearerToken));
                return Task.FromResult(responses.Dequeue());
            }
        }

        private sealed class CapturedRequest
        {
            public string JsonBody { get; }
            public string BearerToken { get; }

            public CapturedRequest(string jsonBody, string bearerToken)
            {
                JsonBody = jsonBody;
                BearerToken = bearerToken;
            }
        }
    }
}
#endif
