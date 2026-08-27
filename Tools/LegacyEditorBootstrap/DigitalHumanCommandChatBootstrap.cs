#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Rebuilds the scene dialogue UI into a usable command console.
    /// Menu: Digital Human / Configure Command Chat UI
    /// </summary>
    public static class DigitalHumanCommandChatBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ChineseFontAssetPath =
            "Assets/DigitalHuman/Fonts/SimHeiDynamicSDF.asset";

        [MenuItem("Digital Human/Configure Command Chat UI")]
        public static void ConfigureFromMenu()
        {
            ConfigureAndValidate();
        }

        [MenuItem("Digital Human/Send Test Command (Play Mode)")]
        public static void SendTestCommandInPlayMode()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Enter Play Mode before sending a test command.");
            }

            DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
            Require(ui != null && ui.PlayerInputField != null, "DialogueUI is missing.");
            ui.PlayerInputField.text = "pick up the red box";
            ui.RequestSubmit();
            Debug.Log("[AgentNPC] Sent test command: pick up the red box");
        }

        public static void ConfigureAndValidate()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);

                DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>(
                    FindObjectsInactive.Include);
                Require(ui != null, "DialogueUI is missing.");

                TMP_FontAsset chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    ChineseFontAssetPath);
                Require(chineseFont != null, "SimHei dynamic TMP font is missing.");

                CommandChatLayout layout = CommandChatLayout.EnsureConfigured(
                    ui,
                    chineseFont);
                Require(layout != null, "CommandChatLayout was not added.");

                DialogueController dialogue =
                    UnityEngine.Object.FindFirstObjectByType<DialogueController>(
                        FindObjectsInactive.Include);
                if (dialogue != null &&
                    string.Equals(dialogue.Greeting, "直接输入指令即可控制我。",
                        StringComparison.Ordinal))
                {
                    dialogue.ConfigureGreeting(dialogue.NpcName, CommandChatLayout.DefaultGreeting);
                    EditorUtility.SetDirty(dialogue);
                }

                ApplyEnglishPickupNames();

                DemoPlayerController player =
                    UnityEngine.Object.FindFirstObjectByType<DemoPlayerController>(
                        FindObjectsInactive.Include);
                if (player != null)
                {
                    Transform cameraTransform = Camera.main != null
                        ? Camera.main.transform
                        : null;
                    player.Configure(cameraTransform, 3f, ui);
                    EditorUtility.SetDirty(player);
                }

                Validate(ui, layout, chineseFont);

                EditorUtility.SetDirty(ui);
                EditorUtility.SetDirty(layout);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[AgentNPC] Command chat UI configured: bottom-center console, " +
                    "readable input field, English commands, TMP shader channels.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void Validate(
            DialogueUI ui,
            CommandChatLayout layout,
            TMP_FontAsset chineseFont)
        {
            Require(layout != null && ui.GetComponent<CommandChatLayout>() != null,
                "CommandChatLayout is not on DialogueCanvas.");

            Canvas canvas = ui.GetComponent<Canvas>();
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            GraphicRaycaster raycaster = ui.GetComponent<GraphicRaycaster>();
            Require(canvas != null && scaler != null && raycaster != null,
                "DialogueCanvas rendering components are incomplete.");
            Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay,
                "Canvas must be Screen Space Overlay.");
            Require(
                (canvas.additionalShaderChannels &
                 AdditionalCanvasShaderChannels.TexCoord1) != 0,
                "Canvas is missing TMP TexCoord1 shader channel.");
            Require(scaler.referenceResolution == new Vector2(1920f, 1080f),
                "CanvasScaler reference resolution is not 1920x1080.");

            RectTransform panel = (RectTransform)ui.transform.Find("DialoguePanel");
            Require(panel != null, "DialoguePanel is missing.");
            Require(panel.gameObject.activeSelf,
                "DialoguePanel must start active for command input.");
            Vector2 size = panel.anchorMax - panel.anchorMin;
            Require(size.x >= 0.70f && size.x <= 0.90f &&
                    size.y >= 0.22f && size.y <= 0.32f,
                $"Command panel size is not usable: {size}");

            Image panelImage = panel.GetComponent<Image>();
            Require(panelImage != null && panelImage.sprite == null,
                "DialoguePanel must use a solid background, not the stretched art.");

            TMP_InputField input = ui.PlayerInputField;
            Require(input != null && input.interactable && !input.readOnly,
                "Command input field is missing or locked.");
            Require(!input.richText, "Command input must not use rich text.");
            RectTransform inputRect = input.GetComponent<RectTransform>();
            Vector2 inputSize = inputRect.anchorMax - inputRect.anchorMin;
            Require(inputSize.y >= 0.18f,
                $"Input field is too short inside the panel: {inputSize}");

            TMP_Text placeholder = input.placeholder as TMP_Text;
            Require(placeholder != null &&
                    placeholder.text.Contains("pick up the red box"),
                "English command placeholder is missing.");

            Button send = ui.SendButton;
            Require(send != null && send.interactable,
                "Send button is missing or not interactable.");
            TMP_Text sendLabel = send.GetComponentInChildren<TMP_Text>(true);
            Require(sendLabel != null && sendLabel.text == "Send",
                "Send button label is not Send.");

            EventSystem eventSystem =
                UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            Require(eventSystem != null, "EventSystem is missing.");

            TMP_Text[] texts = ui.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Require(texts[i].font == chineseFont,
                    $"Chinese TMP font is not assigned to {texts[i].name}.");
            }

            AgentCommandController commands =
                UnityEngine.Object.FindFirstObjectByType<AgentCommandController>(
                    FindObjectsInactive.Include);
            DialogueController dialogue =
                UnityEngine.Object.FindFirstObjectByType<DialogueController>(
                    FindObjectsInactive.Include);
            Require(commands != null, "AgentCommandController is missing.");
            Require(dialogue != null &&
                    dialogue.AutoStartDialogue &&
                    dialogue.AllowRemoteDialogue,
                "DialogueController is not configured for remote command input.");
        }

        private static void ApplyEnglishPickupNames()
        {
            PickupItem[] items = UnityEngine.Object.FindObjectsByType<PickupItem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < items.Length; i++)
            {
                string objectName = items[i].gameObject.name;
                if (objectName.Equals("RedBox", StringComparison.OrdinalIgnoreCase))
                {
                    items[i].Configure("red box", "Red");
                }
                else if (objectName.Equals("BlueBox", StringComparison.OrdinalIgnoreCase))
                {
                    items[i].Configure("blue box", "Blue");
                }
                else if (objectName.Equals("GreenBox", StringComparison.OrdinalIgnoreCase))
                {
                    items[i].Configure("green box", "Green");
                }

                EditorUtility.SetDirty(items[i]);
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
