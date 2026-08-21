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
    public static class DigitalHumanStep16Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step16.SmokeState";
        private const string TestEnvironmentVariable =
            "DIGITAL_HUMAN_STEP16_TEST_KEY";
        private const string TestApiKey = "step16-test-token-not-a-secret";

        private static GameObject digitalHuman;
        private static GameObject player;
        private static PlayerInteractionDetector detector;
        private static LookAtController lookAtController;
        private static DialogueController dialogueController;
        private static DialogueUI dialogueUI;
        private static AIConversationManager conversationManager;
        private static OpenAILLMService llmService;
        private static DigitalHumanController stateController;
        private static DigitalHumanAnimator digitalHumanAnimator;
        private static GameContextProvider contextProvider;
        private static VoiceController voiceController;
        private static MockVoiceService voiceService;
        private static DelayedRecordingTransport transport;
        private static readonly List<DigitalHumanState> ObservedStates =
            new List<DigitalHumanState>();
        private static int runtimeStage;
        private static double deadline;
        private static float initialLookAngle;
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

        public static void ValidateAndSaveScene()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject root = GameObject.Find("DigitalHuman");
                Require(root != null, "DigitalHuman root is missing.");
                Require(root.GetComponent<Animator>() != null, "Animator is missing.");
                Require(root.GetComponent<AudioSource>() != null, "AudioSource is missing.");
                Require(root.GetComponent<DialogueController>() != null,
                    "DialogueController is missing.");
                Require(root.GetComponent<AIConversationManager>() != null,
                    "AIConversationManager is missing.");
                Require(root.GetComponent<OpenAILLMService>() != null,
                    "OpenAILLMService is missing.");
                Require(root.GetComponent<DigitalHumanController>() != null,
                    "DigitalHumanController is missing.");
                Require(root.GetComponent<DigitalHumanAnimator>() != null,
                    "DigitalHumanAnimator is missing.");
                Require(root.GetComponent<VoiceController>() != null,
                    "VoiceController is missing.");
                Require(root.GetComponent<MockVoiceService>() != null,
                    "MockVoiceService is missing.");
                Require(root.GetComponent<GameContextProvider>() != null,
                    "GameContextProvider is missing.");
                Require(root.GetComponent<LookAtController>() != null,
                    "LookAtController is missing.");

                AIConversationManager manager =
                    root.GetComponent<AIConversationManager>();
                Require(manager.Persona != null, "Persona is not connected.");
                GameContextProvider provider =
                    root.GetComponent<GameContextProvider>();
                manager.SetGameContextProvider(provider);
                Require(ReferenceEquals(manager.GameContextProvider, provider),
                    "Game context is not connected.");
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 16 End-to-end scene validation completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void StartPlayModeEndToEndTest()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
                    digitalHuman = GameObject.Find("DigitalHuman");
                    player = GameObject.FindWithTag("Player");
                    Require(digitalHuman != null, "DigitalHuman is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");

                    detector =
                        UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    lookAtController = digitalHuman.GetComponent<LookAtController>();
                    dialogueController = digitalHuman.GetComponent<DialogueController>();
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    conversationManager = digitalHuman.GetComponent<AIConversationManager>();
                    llmService = digitalHuman.GetComponent<OpenAILLMService>();
                    stateController = digitalHuman.GetComponent<DigitalHumanController>();
                    digitalHumanAnimator = digitalHuman.GetComponent<DigitalHumanAnimator>();
                    contextProvider = digitalHuman.GetComponent<GameContextProvider>();
                    voiceController = digitalHuman.GetComponent<VoiceController>();
                    voiceService = digitalHuman.GetComponent<MockVoiceService>();

                    Require(detector != null, "PlayerInteractionDetector is missing.");
                    Require(lookAtController != null, "LookAtController is missing.");
                    Require(dialogueController != null, "DialogueController is missing.");
                    Require(dialogueUI != null, "DialogueUI is missing.");
                    Require(conversationManager != null, "AIConversationManager is missing.");
                    Require(llmService != null, "OpenAILLMService is missing.");
                    Require(stateController != null, "DigitalHumanController is missing.");
                    Require(digitalHumanAnimator != null,
                        "DigitalHumanAnimator is missing.");
                    Require(contextProvider != null, "GameContextProvider is missing.");
                    Require(voiceController != null, "VoiceController is missing.");
                    Require(voiceService != null, "MockVoiceService is missing.");

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
                    transport = new DelayedRecordingTransport();
                    transport.EnqueueResponse("EVA online. Welcome to the laboratory.");
                    transport.EnqueueResponse("The control room is along the east corridor.");
                    llmService.SetTransport(transport);
                    voiceService.Configure(0.5f);
                    conversationManager.ResetConversation();

                    ObservedStates.Clear();
                    ObservedStates.Add(stateController.CurrentState);
                    stateController.StateChanged += OnStateChanged;
                    digitalHuman.transform.rotation = Quaternion.identity;
                    player.transform.position = new Vector3(0f, 1f, -3f);
                    Physics.SyncTransforms();
                    runtimeStage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.4d;
                    EditorApplication.update -= RunEndToEndUpdate;
                    EditorApplication.update += RunEndToEndUpdate;
                }
                catch (Exception exception)
                {
                    FailTest(exception);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreEnvironment();
                int exitCode = SessionState.GetInt(SmokeStateKey, -1) == 2 ? 0 : 1;
                SessionState.EraseInt(SmokeStateKey);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= RunEndToEndUpdate;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunEndToEndUpdate()
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
                        Require(!detector.CanInteract, "Detector should start inactive.");
                        Require(lookAtController.CurrentTarget == null,
                            "Look target should start empty.");
                        Require(!dialogueUI.IsOpen &&
                                !dialogueUI.IsInteractionPromptVisible,
                            "Dialogue UI should start hidden.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Idle,
                            false,
                            false,
                            false);
                        Require(!voiceController.IsSpeaking,
                            "Voice should start inactive.");

                        player.transform.position = new Vector3(2f, 1f, 2.5f);
                        Physics.SyncTransforms();
                        Vector3 initialDirection =
                            player.transform.position - digitalHuman.transform.position;
                        initialDirection.y = 0f;
                        initialLookAngle = Vector3.Angle(
                            digitalHuman.transform.forward,
                            initialDirection);
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.8d;
                        break;

                    case 2:
                        Require(detector.CanInteract,
                            "NPC did not detect the approaching Player.");
                        Require(detector.CurrentPlayer == player.transform,
                            "Detector stored the wrong Player.");
                        Require(lookAtController.CurrentTarget == player.transform,
                            "LookAtController did not acquire the Player.");
                        Require(dialogueUI.IsInteractionPromptVisible,
                            "Interaction prompt did not appear.");
                        RequireStateAndAnimation(
                            DigitalHumanState.PlayerNearby,
                            false,
                            false,
                            false);

                        Vector3 direction =
                            player.transform.position - digitalHuman.transform.position;
                        direction.y = 0f;
                        float lookAngle = Vector3.Angle(
                            digitalHuman.transform.forward,
                            direction);
                        Require(lookAngle < initialLookAngle * 0.35f,
                            "NPC did not smoothly turn toward the Player.");
                        Require(Mathf.Abs(NormalizeAngle(
                                    digitalHuman.transform.eulerAngles.x)) < 0.1f &&
                                Mathf.Abs(NormalizeAngle(
                                    digitalHuman.transform.eulerAngles.z)) < 0.1f,
                            "Look At rotation tilted outside the Y axis.");

                        Require(detector.TryInteract(),
                            "Press-E interaction path failed.");
                        Require(dialogueController.IsDialogueActive && dialogueUI.IsOpen,
                            "Dialogue UI did not open.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Listening,
                            false,
                            true,
                            false);
                        dialogueUI.PlayerInputField.text = "Who are you?";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.12d;
                        break;

                    case 3:
                        Require(dialogueController.IsBusy,
                            "DialogueController is not waiting for the LLM.");
                        Require(dialogueUI.DisplayedDialogue == "Thinking...",
                            "Thinking UI was not displayed.");
                        Require(!dialogueUI.PlayerInputField.interactable &&
                                !dialogueUI.SendButton.interactable,
                            "Input was not disabled while thinking.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Thinking,
                            false,
                            false,
                            true);
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.32d;
                        break;

                    case 4:
                        Require(dialogueUI.DisplayedDialogue ==
                                "EVA online. Welcome to the laboratory.",
                            "First LLM response did not reach the UI.");
                        Require(!dialogueController.IsBusy,
                            "Dialogue remained busy after the response.");
                        Require(conversationManager.ConversationHistory.Count == 2,
                            "First conversation turn was not stored.");
                        Require(transport.Requests.Count == 1,
                            "First LLM request was not sent.");
                        ValidateRequest(
                            transport.Requests[0],
                            "Who are you?",
                            false);
                        RequireStateAndAnimation(
                            DigitalHumanState.Speaking,
                            true,
                            false,
                            false);
                        Require(voiceController.IsSpeaking &&
                                voiceService.GenerationCount == 1,
                            "First voice playback did not start.");
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.55d;
                        break;

                    case 5:
                        Require(!voiceController.IsSpeaking,
                            "First voice playback did not finish.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Listening,
                            false,
                            true,
                            false);
                        dialogueUI.PlayerInputField.text = "Where is the control room?";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 6;
                        deadline = EditorApplication.timeSinceStartup + 0.12d;
                        break;

                    case 6:
                        RequireStateAndAnimation(
                            DigitalHumanState.Thinking,
                            false,
                            false,
                            true);
                        Require(dialogueController.IsBusy,
                            "Second request did not enter busy state.");
                        runtimeStage = 7;
                        deadline = EditorApplication.timeSinceStartup + 0.32d;
                        break;

                    case 7:
                        Require(dialogueUI.DisplayedDialogue ==
                                "The control room is along the east corridor.",
                            "Second LLM response did not reach the UI.");
                        Require(conversationManager.ConversationHistory.Count == 4,
                            "Two-turn conversation memory is incomplete.");
                        Require(transport.Requests.Count == 2,
                            "Second LLM request was not sent.");
                        ValidateRequest(
                            transport.Requests[1],
                            "Where is the control room?",
                            true);
                        RequireStateAndAnimation(
                            DigitalHumanState.Speaking,
                            true,
                            false,
                            false);
                        Require(voiceController.IsSpeaking &&
                                voiceService.GenerationCount == 2,
                            "Second voice playback did not start.");
                        runtimeStage = 8;
                        deadline = EditorApplication.timeSinceStartup + 0.55d;
                        break;

                    case 8:
                        Require(!voiceController.IsSpeaking,
                            "Second voice playback did not finish.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Listening,
                            false,
                            true,
                            false);
                        dialogueUI.CloseButton.onClick.Invoke();
                        RequireStateAndAnimation(
                            DigitalHumanState.PlayerNearby,
                            false,
                            false,
                            false);
                        Require(dialogueUI.IsInteractionPromptVisible,
                            "Prompt did not return after closing dialogue.");
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 9;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 9:
                        Require(!detector.CanInteract,
                            "Detector remained active after Player exit.");
                        Require(lookAtController.CurrentTarget == null,
                            "Look target was not cleared after Player exit.");
                        Require(!dialogueUI.IsOpen &&
                                !dialogueUI.IsInteractionPromptVisible,
                            "Dialogue UI did not fully hide after exit.");
                        RequireStateAndAnimation(
                            DigitalHumanState.Idle,
                            false,
                            false,
                            false);
                        Require(ContainsRequiredStateSequence(),
                            "End-to-end state sequence was incomplete.");

                        RestoreEnvironment();
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 16 End-to-end Play Mode test passed.");
                        EditorApplication.update -= RunEndToEndUpdate;
                        EditorApplication.ExitPlaymode();
                        break;
                }
            }
            catch (Exception exception)
            {
                FailTest(exception);
            }
        }

        private static void ValidateRequest(
            CapturedRequest request,
            string currentPlayerMessage,
            bool expectPriorTurn)
        {
            Require(request.BearerToken == TestApiKey,
                "LLM request did not use the environment API key.");
            Require(request.JsonBody.Contains("\"model\":\"gpt-5.6-terra\""),
                "LLM request used the wrong model.");
            Require(request.JsonBody.Contains("You are EVA"),
                "LLM request omitted Persona.");
            Require(request.JsonBody.Contains("Current game context:") &&
                    request.JsonBody.Contains("Research Laboratory") &&
                    request.JsonBody.Contains("(2.00, 1.00, 2.50)"),
                "LLM request omitted live game context.");
            Require(request.JsonBody.Contains(currentPlayerMessage),
                "LLM request omitted the current Player message.");
            if (expectPriorTurn)
            {
                Require(request.JsonBody.Contains("Who are you?") &&
                        request.JsonBody.Contains(
                            "EVA online. Welcome to the laboratory."),
                    "Second LLM request omitted conversation memory.");
            }
        }

        private static void RequireStateAndAnimation(
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

        private static void OnStateChanged(
            DigitalHumanState _,
            DigitalHumanState nextState)
        {
            ObservedStates.Add(nextState);
        }

        private static bool ContainsRequiredStateSequence()
        {
            DigitalHumanState[] expected =
            {
                DigitalHumanState.Idle,
                DigitalHumanState.PlayerNearby,
                DigitalHumanState.Listening,
                DigitalHumanState.Thinking,
                DigitalHumanState.Speaking,
                DigitalHumanState.Listening,
                DigitalHumanState.Thinking,
                DigitalHumanState.Speaking,
                DigitalHumanState.Listening,
                DigitalHumanState.PlayerNearby,
                DigitalHumanState.Idle
            };

            int expectedIndex = 0;
            for (int i = 0;
                 i < ObservedStates.Count && expectedIndex < expected.Length;
                 i++)
            {
                if (ObservedStates[i] == expected[expectedIndex])
                {
                    expectedIndex++;
                }
            }

            return expectedIndex == expected.Length;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static void RestoreEnvironment()
        {
            Environment.SetEnvironmentVariable(
                TestEnvironmentVariable,
                previousApiKey);
        }

        private static void FailTest(Exception exception)
        {
            Debug.LogException(exception);
            RestoreEnvironment();
            SessionState.SetInt(SmokeStateKey, -1);
            EditorApplication.update -= RunEndToEndUpdate;
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

        private sealed class DelayedRecordingTransport : ILLMHttpTransport
        {
            private readonly Queue<string> responseTexts = new Queue<string>();

            public readonly List<CapturedRequest> Requests =
                new List<CapturedRequest>();

            public void EnqueueResponse(string responseText)
            {
                responseTexts.Enqueue(responseText);
            }

            public async Task<LLMHttpResponse> PostJsonAsync(
                string url,
                string jsonBody,
                string bearerToken,
                int timeoutSeconds,
                CancellationToken cancellationToken = default)
            {
                Require(responseTexts.Count > 0, "No end-to-end response was queued.");
                Requests.Add(new CapturedRequest(jsonBody, bearerToken));
                await Task.Delay(300, cancellationToken);
                string responseText = responseTexts.Dequeue();
                string json =
                    "{\"id\":\"resp_e2e\",\"status\":\"completed\"," +
                    "\"output\":[{\"type\":\"message\",\"role\":\"assistant\"," +
                    "\"content\":[{\"type\":\"output_text\",\"text\":\"" +
                    responseText +
                    "\"}]}]}";
                return new LLMHttpResponse(200, json);
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
