#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanRemoteUiCameraPlayModeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SessionKey = "DigitalHuman.RemoteUiCamera.PlayMode";

        private static DialogueController dialogue;
        private static DialogueUI ui;
        private static AgentCommandController commands;
        private static AgentMovement movement;
        private static PlayerInteractionDetector detector;
        private static Camera mainCamera;
        private static int stage;
        private static double deadline;
        private static Vector3 commandStart;
        private static float maxTravel;
        private static bool sawMoving;
        private static bool sawTalking;

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

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (SessionState.GetInt(SessionKey, 0) == 0)
            {
                return;
            }

            if (stateChange == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    dialogue = UnityEngine.Object.FindFirstObjectByType<DialogueController>();
                    ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    commands = UnityEngine.Object.FindFirstObjectByType<AgentCommandController>();
                    movement = UnityEngine.Object.FindFirstObjectByType<AgentMovement>();
                    detector = UnityEngine.Object.FindFirstObjectByType<
                        PlayerInteractionDetector>();
                    mainCamera = Camera.main;
                    Require(dialogue != null && ui != null && commands != null &&
                            movement != null && detector != null && mainCamera != null,
                        "Remote Agent Demo runtime components are incomplete.");

                    commands.StateChanged += OnAgentStateChanged;
                    stage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.6d;
                    EditorApplication.update -= RunTestUpdate;
                    EditorApplication.update += RunTestUpdate;
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }
            else if (stateChange == PlayModeStateChange.EnteredEditMode)
            {
                int exitCode = SessionState.GetInt(SessionKey, -1) == 2 ? 0 : 1;
                CleanupRuntimeCallbacks();
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                SessionState.EraseInt(SessionKey);
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunTestUpdate()
        {
            if (EditorApplication.timeSinceStartup < deadline)
            {
                return;
            }

            try
            {
                if (stage == 1)
                {
                    ValidateInitialRemoteUiAndCamera();
                    sawMoving = false;
                    sawTalking = false;
                    maxTravel = 0f;
                    commandStart = commands.transform.position;
                    ui.PlayerInputField.Select();
                    ui.PlayerInputField.ActivateInputField();
                    ui.PlayerInputField.text = "go to the blue box";
                    ui.SendButton.onClick.Invoke();
                    Require(commands.IsExecutingCommand,
                        "Remote chat did not execute the movement command.");
                    stage = 2;
                    deadline = EditorApplication.timeSinceStartup + 22d;
                    return;
                }

                maxTravel = Mathf.Max(
                    maxTravel,
                    Vector3.Distance(commandStart, commands.transform.position));
                bool complete = !commands.IsExecutingCommand &&
                                commands.CurrentState == AgentNpcState.Idle &&
                                ui.DisplayedDialogue.Contains("I've arrived at the blue box.");
                if (complete)
                {
                    Require(sawTalking && sawMoving,
                        "Agent did not enter Talking and Moving states.");
                    Require(maxTravel >= 5f,
                        $"Remote command travel was too short: {maxTravel:F2}m.");
                    Require(ui.IsOpen && dialogue.IsDialogueActive,
                        "Remote dialogue closed while the NPC moved away.");
                    Require(!detector.CanInteract,
                        "Test player unexpectedly entered the proximity trigger.");
                    Require(ui.ConversationHistory.Contains("go to the blue box") &&
                            ui.ConversationHistory.Contains("I've arrived at the blue box."),
                        "Remote command or success feedback is missing from chat history.");
                    Require(IsWorldPointVisible(commands.transform.position),
                        "NPC destination is outside the overhead camera view.");

                    SessionState.SetInt(SessionKey, 2);
                    Debug.Log(
                        "[AgentNPC] Remote UI and overhead camera Play Mode test passed: " +
                        $"dialogue auto-opened outside trigger, travel={maxTravel:F2}m, " +
                        "blue-box movement completed, panel remained visible/interactable.");
                    CleanupRuntimeCallbacks();
                    EditorApplication.ExitPlaymode();
                    return;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    throw new TimeoutException(
                        "Timed out executing remote command 'go to the blue box'. " +
                        $"State={commands.CurrentState}, moving={movement.IsMoving}.");
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void ValidateInitialRemoteUiAndCamera()
        {
            Require(dialogue.AutoStartDialogue && dialogue.AllowRemoteDialogue,
                "DialogueController remote access flags are disabled.");
            Require(dialogue.IsDialogueActive && ui.IsOpen && ui.IsPersistent,
                "Chat did not open automatically at game start.");
            Require(!detector.CanInteract,
                "Player is still close enough to trigger proximity dialogue.");
            Require(!ui.IsInteractionPromptVisible,
                "Legacy proximity prompt is visible in remote mode.");
            Require(ui.PlayerInputField != null &&
                    ui.PlayerInputField.gameObject.activeInHierarchy &&
                    ui.PlayerInputField.interactable,
                "TMP input field is hidden or non-interactable.");
            Require(ui.SendButton != null &&
                    ui.SendButton.gameObject.activeInHierarchy &&
                    ui.SendButton.interactable,
                "Send button is hidden or non-interactable.");
            Require(ui.CloseButton == null ||
                    !ui.CloseButton.gameObject.activeInHierarchy,
                "Close button should be hidden for persistent remote chat.");
            Require(EventSystem.current != null &&
                    ui.GetComponent<GraphicRaycaster>() != null,
                "EventSystem or GraphicRaycaster is missing.");

            Canvas.ForceUpdateCanvases();
            RectTransform panel = (RectTransform)ui.transform.Find("DialoguePanel");
            Vector3[] corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Require(corners[i].x >= -1f && corners[i].x <= Screen.width + 1f &&
                        corners[i].y >= -1f && corners[i].y <= Screen.height + 1f,
                    $"Chat panel corner {i} is outside {Screen.width}x{Screen.height}: " +
                    corners[i]);
            }

            Require(mainCamera.transform.position.y >= 12f &&
                    Vector3.Dot(mainCamera.transform.forward, Vector3.down) >= 0.65f,
                "Main Camera is not using the configured overhead angle.");
            Require(IsWorldPointVisible(commands.transform.position),
                "DigitalHuman is not visible in the overhead camera.");
        }

        private static bool IsWorldPointVisible(Vector3 worldPoint)
        {
            Vector3 viewport = mainCamera.WorldToViewportPoint(worldPoint + Vector3.up);
            return viewport.z > 0f &&
                   viewport.x >= 0f && viewport.x <= 1f &&
                   viewport.y >= 0f && viewport.y <= 1f;
        }

        private static void OnAgentStateChanged(
            AgentNpcState _,
            AgentNpcState next)
        {
            sawTalking |= next == AgentNpcState.Talking;
            sawMoving |= next == AgentNpcState.Moving;
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(SessionKey, -1);
            CleanupRuntimeCallbacks();
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

        private static void CleanupRuntimeCallbacks()
        {
            EditorApplication.update -= RunTestUpdate;
            if (commands != null)
            {
                commands.StateChanged -= OnAgentStateChanged;
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
