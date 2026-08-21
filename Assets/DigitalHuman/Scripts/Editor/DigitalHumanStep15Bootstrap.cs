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
    public static class DigitalHumanStep15Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step15.SmokeState";

        private static GameContextProvider contextProvider;
        private static AIConversationManager conversationManager;
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
                GameObject scenePlayer = GameObject.FindWithTag("Player");
                Require(digitalHuman != null, "DigitalHuman root is missing.");
                Require(scenePlayer != null, "Player is missing.");

                AIConversationManager manager =
                    digitalHuman.GetComponent<AIConversationManager>();
                Require(manager != null, "AIConversationManager is missing.");
                GameContextProvider provider =
                    digitalHuman.GetComponent<GameContextProvider>();
                if (provider == null)
                {
                    provider = digitalHuman.AddComponent<GameContextProvider>();
                }

                provider.Configure(
                    scenePlayer.transform,
                    "Research Laboratory",
                    100f,
                    "Meet EVA and learn about the facility.");
                manager.SetGameContextProvider(provider);
                EditorUtility.SetDirty(provider);
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 15 Game Context scene setup completed.");
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
                GameContextProvider provider =
                    UnityEngine.Object.FindFirstObjectByType<GameContextProvider>();
                Require(provider != null, "GameContextProvider is missing.");
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
                    contextProvider =
                        UnityEngine.Object.FindFirstObjectByType<GameContextProvider>();
                    conversationManager =
                        UnityEngine.Object.FindFirstObjectByType<AIConversationManager>();
                    player = GameObject.FindWithTag("Player");
                    Require(contextProvider != null,
                        "GameContextProvider is missing in Play Mode.");
                    Require(conversationManager != null,
                        "AIConversationManager is missing in Play Mode.");
                    Require(player != null, "Player is missing.");
                    Require(ReferenceEquals(
                            conversationManager.GameContextProvider,
                            contextProvider),
                        "GameContextProvider is not connected to AIConversationManager.");

                    contextProvider.Configure(
                        player.transform,
                        "Control Wing",
                        80f,
                        "Find the control room.",
                        true,
                        true);
                    backend = new RecordingBackend();
                    conversationManager.SetResponseProvider(backend);
                    conversationManager.ResetConversation();
                    player.transform.position = new Vector3(1.25f, 1f, -2.5f);
                    Physics.SyncTransforms();
                    pendingResponse = null;
                    runtimeStage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.2d;
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
                        string context = contextProvider.GetGameContext();
                        Require(context.Contains("Scene: SampleScene"),
                            "Context omitted the active scene.");
                        Require(context.Contains("Location: Control Wing"),
                            "Context omitted the current location.");
                        Require(context.Contains("(1.25, 1.00, -2.50)"),
                            "Context omitted the live player position.");
                        Require(context.Contains("Player health: 80/100"),
                            "Context omitted player health.");
                        Require(context.Contains("Find the control room"),
                            "Context omitted the objective.");
                        Require(context.Contains("Session time:"),
                            "Context omitted session time.");

                        pendingResponse =
                            conversationManager.GetResponseAsync("Where should I go?");
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 2:
                        Require(pendingResponse != null &&
                                pendingResponse.Status == TaskStatus.RanToCompletion,
                            pendingResponse != null && pendingResponse.Exception != null
                                ? pendingResponse.Exception.ToString()
                                : "Context conversation did not complete.");
                        Require(pendingResponse.Result == "Use the east corridor.",
                            "Unexpected context test response.");
                        Require(backend.Requests.Count == 1,
                            "Conversation backend was not called.");
                        IReadOnlyList<ConversationMessage> request = backend.Requests[0];
                        Require(request.Count == 2,
                            "Context request should contain system and user messages.");
                        Require(request[0].Role == ConversationMessage.SystemRole,
                            "First request message is not the system prompt.");
                        Require(request[0].Content.Contains("Current game context:"),
                            "System prompt omitted the game-context section.");
                        Require(request[0].Content.Contains("Control Wing") &&
                                request[0].Content.Contains("Player health: 80/100") &&
                                request[0].Content.Contains("Where") == false,
                            "System prompt contains incorrect context data.");
                        Require(request[1].Content == "Where should I go?",
                            "Player message was not kept separate from context.");

                        player.transform.position = new Vector3(2f, 1f, 2f);
                        Physics.SyncTransforms();
                        string updatedContext = contextProvider.GetGameContext();
                        Require(updatedContext.Contains("(2.00, 1.00, 2.00)"),
                            "Context did not update with the Player transform.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 15 Play Mode Game Context test passed.");
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
                return Task.FromResult("Use the east corridor.");
            }
        }
    }
}
#endif
