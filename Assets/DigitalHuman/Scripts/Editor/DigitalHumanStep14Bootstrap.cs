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
    public static class DigitalHumanStep14Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step14.SmokeState";

        private static PlayerInteractionDetector detector;
        private static DialogueController dialogueController;
        private static DigitalHumanController stateController;
        private static DialogueUI dialogueUI;
        private static MockVoiceService voiceService;
        private static VoiceController voiceController;
        private static AudioSource audioSource;
        private static GameObject player;
        private static bool speechStarted;
        private static bool speechCompleted;
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

                DialogueController sceneDialogue =
                    digitalHuman.GetComponent<DialogueController>();
                DigitalHumanController sceneStateController =
                    digitalHuman.GetComponent<DigitalHumanController>();
                AudioSource sceneAudioSource = digitalHuman.GetComponent<AudioSource>();
                Require(sceneDialogue != null, "DialogueController is missing.");
                Require(sceneStateController != null, "DigitalHumanController is missing.");
                Require(sceneAudioSource != null, "AudioSource is missing.");

                MockVoiceService sceneVoiceService =
                    digitalHuman.GetComponent<MockVoiceService>();
                if (sceneVoiceService == null)
                {
                    sceneVoiceService = digitalHuman.AddComponent<MockVoiceService>();
                }

                VoiceController sceneVoiceController =
                    digitalHuman.GetComponent<VoiceController>();
                if (sceneVoiceController == null)
                {
                    sceneVoiceController = digitalHuman.AddComponent<VoiceController>();
                }

                sceneVoiceService.Configure(0.5f);
                sceneVoiceController.Configure(
                    sceneDialogue,
                    sceneStateController,
                    sceneAudioSource,
                    sceneVoiceService);
                sceneAudioSource.playOnAwake = false;
                sceneAudioSource.loop = false;

                EditorUtility.SetDirty(sceneVoiceService);
                EditorUtility.SetDirty(sceneVoiceController);
                EditorUtility.SetDirty(sceneAudioSource);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 14 Voice architecture scene setup completed.");
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
                Require(UnityEngine.Object.FindFirstObjectByType<VoiceController>() != null,
                    "VoiceController is missing.");
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
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    voiceService =
                        UnityEngine.Object.FindFirstObjectByType<MockVoiceService>();
                    voiceController =
                        UnityEngine.Object.FindFirstObjectByType<VoiceController>();
                    player = GameObject.FindWithTag("Player");
                    audioSource = voiceController != null
                        ? voiceController.GetComponent<AudioSource>()
                        : null;
                    Require(detector != null, "PlayerInteractionDetector is missing.");
                    Require(dialogueController != null, "DialogueController is missing.");
                    Require(stateController != null, "DigitalHumanController is missing.");
                    Require(dialogueUI != null, "DialogueUI is missing.");
                    Require(voiceService != null, "MockVoiceService is missing.");
                    Require(voiceController != null, "VoiceController is missing.");
                    Require(audioSource != null, "AudioSource is missing.");
                    Require(player != null, "Player is missing.");

                    dialogueController.SetConversationService(
                        new ImmediateConversationService());
                    speechStarted = false;
                    speechCompleted = false;
                    voiceController.SpeechStarted += _ => speechStarted = true;
                    voiceController.SpeechCompleted += _ => speechCompleted = true;
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
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        Require(detector.TryInteract(), "Interaction request failed.");
                        dialogueUI.PlayerInputField.text = "Voice test";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.15d;
                        break;

                    case 3:
                        Require(voiceService.GenerationCount == 1,
                            "Mock voice service was not called.");
                        Require(voiceService.LastRequestedText == "Voice response.",
                            "Voice service received the wrong response.");
                        Require(speechStarted, "SpeechStarted event was not raised.");
                        Require(voiceController.IsSpeaking && audioSource.clip != null,
                            "VoiceController did not begin clip playback.");
                        Require(stateController.CurrentState == DigitalHumanState.Speaking,
                            "NPC did not remain in Speaking during playback.");
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.55d;
                        break;

                    case 4:
                        Require(speechCompleted, "SpeechCompleted event was not raised.");
                        Require(!voiceController.IsSpeaking && !audioSource.isPlaying,
                            "Voice playback did not finish.");
                        Require(stateController.CurrentState == DigitalHumanState.Listening,
                            "NPC did not return to Listening after voice playback.");
                        dialogueUI.CloseButton.onClick.Invoke();
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 5:
                        Require(stateController.CurrentState == DigitalHumanState.Idle,
                            "NPC did not return to Idle.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 14 Play Mode Voice architecture test passed.");
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

        private sealed class ImmediateConversationService : IConversationService
        {
            public Task<string> GetResponseAsync(
                string playerMessage,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult("Voice response.");
            }
        }
    }
}
#endif
