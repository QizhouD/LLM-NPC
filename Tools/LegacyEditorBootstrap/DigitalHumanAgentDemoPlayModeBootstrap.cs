#if UNITY_EDITOR
using System;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanAgentDemoPlayModeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SessionKey = "DigitalHuman.AgentDemo.PlayMode";

        private static AgentCommandController commands;
        private static AgentMovement movement;
        private static AgentInteraction interaction;
        private static DialogueController dialogue;
        private static DialogueUI ui;
        private static PickupItem redItem;
        private static VoiceController voice;
        private static Vector3 startPosition;
        private static double deadline;
        private static float maxTravelDistance;
        private static int maxPathCorners;
        private static int speechStartedCount;
        private static bool sawTalking;
        private static bool sawMoving;
        private static bool sawInteracting;

        [InitializeOnLoadMethod]
        private static void ResumePendingTest()
        {
            if (SessionState.GetInt(SessionKey, 0) == 0)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void StartTest()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SessionState.SetInt(SessionKey, 1);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (SessionState.GetInt(SessionKey, 0) == 0)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    commands = UnityEngine.Object.FindFirstObjectByType<
                        AgentCommandController>();
                    movement = UnityEngine.Object.FindFirstObjectByType<AgentMovement>();
                    interaction = UnityEngine.Object.FindFirstObjectByType<AgentInteraction>();
                    dialogue = UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    voice = UnityEngine.Object.FindFirstObjectByType<VoiceController>();
                    GameObject redBox = GameObject.Find("RedBox");
                    redItem = redBox != null ? redBox.GetComponent<PickupItem>() : null;

                    Require(commands != null && movement != null && interaction != null,
                        "Agent runtime components are missing.");
                    Require(dialogue != null && ui != null && voice != null,
                        "Dialogue or voice components are missing.");
                    Require(redItem != null, "RedBox PickupItem is missing.");
                    Require(movement.NavMeshAgent != null &&
                            movement.NavMeshAgent.isOnNavMesh,
                        "DigitalHuman NavMeshAgent is not on the baked NavMesh.");
                    Require(UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>() != null,
                        "NavMeshSurface is missing in Play Mode.");

                    RectTransform panel = (RectTransform)ui.transform.Find("DialoguePanel");
                    Vector2 panelRatio = panel.anchorMax - panel.anchorMin;
                    Require(ui.GetComponent<CommandChatLayout>() != null,
                        "CommandChatLayout is missing in Play Mode.");
                    Require(panelRatio.x >= 0.70f && panelRatio.x <= 0.90f &&
                            panelRatio.y >= 0.22f && panelRatio.y <= 0.32f,
                        "Command chat panel is not large enough to type into.");

                    sawTalking = false;
                    sawMoving = false;
                    sawInteracting = false;
                    speechStartedCount = 0;
                    maxTravelDistance = 0f;
                    maxPathCorners = 0;
                    startPosition = commands.transform.position;
                    commands.StateChanged += OnAgentStateChanged;
                    voice.SpeechStarted += OnSpeechStarted;

                    dialogue.StartDialogue();
                    Require(ui.IsOpen, "Dialogue panel did not open.");
                    ui.PlayerInputField.text = "pick up the red box";
                    ui.SendButton.onClick.Invoke();
                    deadline = EditorApplication.timeSinceStartup + 25d;
                    EditorApplication.update -= RunTestUpdate;
                    EditorApplication.update += RunTestUpdate;
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                int exitCode = SessionState.GetInt(SessionKey, -1) == 2 ? 0 : 1;
                CleanupCallbacks();
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                SessionState.EraseInt(SessionKey);
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunTestUpdate()
        {
            try
            {
                maxTravelDistance = Mathf.Max(
                    maxTravelDistance,
                    Vector3.Distance(startPosition, commands.transform.position));
                NavMeshAgent agent = movement.NavMeshAgent;
                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                {
                    maxPathCorners = Mathf.Max(
                        maxPathCorners,
                        agent.path.corners.Length);
                }

                bool completed = !commands.IsExecutingCommand &&
                                 commands.CurrentState == AgentNpcState.Idle &&
                                 redItem.IsPickedUp;
                if (completed)
                {
                    ValidateCompletedCommand();
                    SessionState.SetInt(SessionKey, 2);
                    Debug.Log(
                        "[AgentNPC] Play Mode acceptance passed: compact chat, " +
                        $"NavMesh route corners={maxPathCorners}, travel=" +
                        $"{maxTravelDistance:F2}m, RedBox attached to hand, " +
                        "feedback and voice triggered.");
                    CleanupCallbacks();
                    EditorApplication.ExitPlaymode();
                    return;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    throw new TimeoutException(
                        "Timed out waiting for 'pick up the red box'. " +
                        $"State={commands.CurrentState}, moving={movement.IsMoving}, " +
                        $"picked={redItem.IsPickedUp}.");
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void ValidateCompletedCommand()
        {
            Require(sawTalking && sawMoving && sawInteracting,
                "Agent did not traverse Talking, Moving and Interacting states.");
            Require(maxTravelDistance >= 5f,
                $"NPC did not travel far enough: {maxTravelDistance:F2}m.");
            Require(maxPathCorners >= 3,
                "NavMeshAgent did not route around the baked obstacle.");
            Require(interaction.HeldItem == redItem.gameObject,
                "AgentInteraction did not record RedBox as held.");
            Require(redItem.transform.IsChildOf(interaction.RightHand),
                "RedBox was not attached to the NPC right hand.");
            Require(redItem.GetComponent<Collider>() != null &&
                    !redItem.GetComponent<Collider>().enabled,
                "Held RedBox collider was not disabled.");
            Require(ui.ConversationHistory.Contains("pick up the red box") &&
                    ui.ConversationHistory.Contains("I've picked up the red box."),
                "Chat history does not contain command and success feedback.");
            TMP_Text historyText = ui.transform.Find(
                "DialoguePanel/DialogueText").GetComponent<TMP_Text>();
            Require(historyText.font != null && historyText.font.HasCharacter('p'),
                "Assigned TMP font cannot render command text.");
            Require(ui.DisplayedDialogue.Contains("I've picked up the red box."),
                "NPC final success response is missing.");
            Require(speechStartedCount >= 1,
                "Existing TTS/voice path did not start playback.");
            Require(GameObject.Find("BlueBox") != null &&
                    GameObject.Find("GreenBox") != null,
                "BlueBox or GreenBox was lost during the command.");
        }

        private static void OnAgentStateChanged(
            AgentNpcState _,
            AgentNpcState next)
        {
            sawTalking |= next == AgentNpcState.Talking;
            sawMoving |= next == AgentNpcState.Moving;
            sawInteracting |= next == AgentNpcState.Interacting;
        }

        private static void OnSpeechStarted(string _)
        {
            speechStartedCount++;
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(SessionKey, -1);
            CleanupCallbacks();
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.Exit(1);
            }
        }

        private static void CleanupCallbacks()
        {
            EditorApplication.update -= RunTestUpdate;
            if (commands != null)
            {
                commands.StateChanged -= OnAgentStateChanged;
            }

            if (voice != null)
            {
                voice.SpeechStarted -= OnSpeechStarted;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
