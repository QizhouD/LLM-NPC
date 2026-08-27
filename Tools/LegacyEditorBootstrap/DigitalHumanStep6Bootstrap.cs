#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep6Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step6.SmokeState";

        private static DialogueUI dialogueUI;
        private static int runtimeStage;
        private static double deadline;
        private static string submittedMessage;
        private static bool closeEventReceived;

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

        public static void ImportTmpEssentials()
        {
            string[] settingsAssets = AssetDatabase.FindAssets("t:TMP_Settings");
            if (settingsAssets.Length > 0)
            {
                Debug.Log("[DigitalHuman] TMP Essential Resources already available.");
                EditorApplication.Exit(0);
                return;
            }

            try
            {
                UnityEditor.PackageManager.PackageInfo packageInfo =
                    UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
                Require(packageInfo != null, "Unable to locate the UGUI package.");
                string packagePath = Path.Combine(
                    packageInfo.resolvedPath,
                    "Package Resources",
                    "TMP Essential Resources.unitypackage");
                Require(File.Exists(packagePath), $"TMP Essential Resources package is missing: {packagePath}");

                AssetDatabase.importPackageCompleted -= OnTmpImportCompleted;
                AssetDatabase.importPackageFailed -= OnTmpImportFailed;
                AssetDatabase.importPackageCancelled -= OnTmpImportCancelled;
                AssetDatabase.importPackageCompleted += OnTmpImportCompleted;
                AssetDatabase.importPackageFailed += OnTmpImportFailed;
                AssetDatabase.importPackageCancelled += OnTmpImportCancelled;
                AssetDatabase.ImportPackage(packagePath, false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ConfigureScene()
        {
            try
            {
                TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
                Require(defaultFont != null, "TMP default font asset is unavailable.");

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject existingCanvas = GameObject.Find("DialogueCanvas");
                if (existingCanvas != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingCanvas);
                }

                GameObject canvasObject = new GameObject(
                    "DialogueCanvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                TextMeshProUGUI prompt = CreateText(
                    "InteractionPrompt",
                    canvasObject.transform,
                    defaultFont,
                    "Press E to Talk",
                    30f,
                    TextAlignmentOptions.Center);
                SetRect(prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(640f, 54f), new Vector2(0f, 86f));
                prompt.color = new Color(1f, 0.9f, 0.55f, 1f);
                prompt.gameObject.SetActive(false);

                GameObject panelObject = CreateUiObject("DialoguePanel", canvasObject.transform);
                Image panelImage = panelObject.AddComponent<Image>();
                panelImage.color = new Color(0.035f, 0.045f, 0.07f, 0.94f);
                RectTransform panelRect = panelObject.GetComponent<RectTransform>();
                SetStretchRect(panelRect, new Vector2(0.14f, 0.05f), new Vector2(0.86f, 0.43f),
                    Vector2.zero, Vector2.zero);

                TextMeshProUGUI npcName = CreateText(
                    "NPCNameText", panelObject.transform, defaultFont, "EVA", 34f,
                    TextAlignmentOptions.Left);
                npcName.fontStyle = FontStyles.Bold;
                npcName.color = new Color(0.35f, 0.85f, 1f, 1f);
                SetStretchRect(npcName.rectTransform, new Vector2(0.025f, 0.82f), new Vector2(0.85f, 0.98f),
                    Vector2.zero, Vector2.zero);

                TextMeshProUGUI dialogueText = CreateText(
                    "DialogueText", panelObject.transform, defaultFont,
                    "Start a conversation to hear EVA's response.", 26f,
                    TextAlignmentOptions.TopLeft);
                dialogueText.color = Color.white;
                SetStretchRect(dialogueText.rectTransform, new Vector2(0.025f, 0.31f),
                    new Vector2(0.975f, 0.81f), Vector2.zero, Vector2.zero);

                TMP_InputField inputField = CreateInputField(panelObject.transform, defaultFont);
                SetStretchRect(inputField.GetComponent<RectTransform>(), new Vector2(0.025f, 0.065f),
                    new Vector2(0.77f, 0.265f), Vector2.zero, Vector2.zero);

                Button sendButton = CreateButton(
                    "SendButton", panelObject.transform, defaultFont, "Send",
                    new Color(0.10f, 0.48f, 0.72f, 1f));
                SetStretchRect(sendButton.GetComponent<RectTransform>(), new Vector2(0.79f, 0.065f),
                    new Vector2(0.975f, 0.265f), Vector2.zero, Vector2.zero);

                Button closeButton = CreateButton(
                    "CloseButton", panelObject.transform, defaultFont, "X",
                    new Color(0.35f, 0.12f, 0.16f, 1f));
                SetRect(closeButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                    Vector2.one, new Vector2(48f, 48f), new Vector2(-12f, -12f));

                DialogueUI ui = canvasObject.AddComponent<DialogueUI>();
                ui.Configure(panelObject, prompt, npcName, dialogueText, inputField, sendButton, closeButton);
                panelObject.SetActive(false);

                EventSystem eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
                if (eventSystem == null)
                {
                    GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
                    InputSystemUIInputModule inputModule =
                        eventSystemObject.AddComponent<InputSystemUIInputModule>();
                    inputModule.AssignDefaultActions();
                }
                else if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                {
                    BaseInputModule existingModule = eventSystem.GetComponent<BaseInputModule>();
                    if (existingModule != null)
                    {
                        UnityEngine.Object.DestroyImmediate(existingModule);
                    }

                    InputSystemUIInputModule inputModule =
                        eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                    inputModule.AssignDefaultActions();
                }

                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 6 Dialogue UI scene setup completed.");
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
                DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                Require(ui != null, "DialogueUI is missing.");
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

        private static void OnTmpImportCompleted(string packageName)
        {
            ClearTmpImportCallbacks();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DigitalHuman] TMP Essential Resources imported: {packageName}");
            EditorApplication.Exit(0);
        }

        private static void OnTmpImportFailed(string packageName, string errorMessage)
        {
            ClearTmpImportCallbacks();
            Debug.LogError($"[DigitalHuman] TMP import failed ({packageName}): {errorMessage}");
            EditorApplication.Exit(1);
        }

        private static void OnTmpImportCancelled(string packageName)
        {
            ClearTmpImportCallbacks();
            Debug.LogError($"[DigitalHuman] TMP import cancelled: {packageName}");
            EditorApplication.Exit(1);
        }

        private static void ClearTmpImportCallbacks()
        {
            AssetDatabase.importPackageCompleted -= OnTmpImportCompleted;
            AssetDatabase.importPackageFailed -= OnTmpImportFailed;
            AssetDatabase.importPackageCancelled -= OnTmpImportCancelled;
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
                    dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
                    Require(dialogueUI != null, "DialogueUI is missing in Play Mode.");
                    submittedMessage = null;
                    closeEventReceived = false;
                    dialogueUI.MessageSubmitted += message => submittedMessage = message;
                    dialogueUI.CloseRequested += () => closeEventReceived = true;
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
                        Require(!dialogueUI.IsOpen, "Dialogue panel should be hidden by default.");
                        dialogueUI.ShowInteractionPrompt(true);
                        Require(dialogueUI.IsInteractionPromptVisible, "Interaction prompt did not open.");
                        dialogueUI.ShowDialogue("EVA", "Hello, traveler.");
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.2d;
                        break;

                    case 2:
                        Require(dialogueUI.IsOpen, "Dialogue panel did not open.");
                        Require(!dialogueUI.IsInteractionPromptVisible,
                            "Interaction prompt remained visible during dialogue.");
                        Require(dialogueUI.DisplayedNpcName == "EVA", "NPC name was not displayed.");
                        Require(dialogueUI.DisplayedDialogue == "Hello, traveler.",
                            "Dialogue text was not displayed.");
                        dialogueUI.PlayerInputField.text = "Test message";
                        dialogueUI.SendButton.onClick.Invoke();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 3:
                        Require(submittedMessage == "Test message", "Send event returned the wrong message.");
                        Require(string.IsNullOrEmpty(dialogueUI.PlayerInputField.text),
                            "Input field was not cleared after submit.");
                        dialogueUI.SetThinking(true);
                        Require(dialogueUI.DisplayedDialogue == "Thinking...", "Thinking state was not displayed.");
                        Require(!dialogueUI.PlayerInputField.interactable && !dialogueUI.SendButton.interactable,
                            "Input controls remained enabled while thinking.");
                        dialogueUI.SetThinking(false);
                        dialogueUI.CloseButton.onClick.Invoke();
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.1d;
                        break;

                    case 4:
                        Require(!dialogueUI.IsOpen, "Dialogue panel did not close.");
                        Require(closeEventReceived, "CloseRequested event was not raised.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 6 Play Mode Dialogue UI test passed.");
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

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateUiObject(name, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_InputField CreateInputField(Transform parent, TMP_FontAsset font)
        {
            GameObject fieldObject = CreateUiObject("PlayerInputField", parent);
            Image background = fieldObject.AddComponent<Image>();
            background.color = new Color(0.10f, 0.12f, 0.17f, 1f);
            TMP_InputField inputField = fieldObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            GameObject viewportObject = CreateUiObject("Text Area", fieldObject.transform);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            SetStretchRect(viewport, Vector2.zero, Vector2.one,
                new Vector2(14f, 8f), new Vector2(-14f, -8f));
            viewportObject.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateText(
                "Placeholder", viewportObject.transform, font, "Type a message...", 24f,
                TextAlignmentOptions.Left);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(1f, 1f, 1f, 0.38f);
            SetStretchRect(placeholder.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            TextMeshProUGUI inputText = CreateText(
                "Text", viewportObject.transform, font, string.Empty, 24f,
                TextAlignmentOptions.Left);
            inputText.color = Color.white;
            SetStretchRect(inputText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            inputField.textViewport = viewport;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.targetGraphic = background;
            return inputField;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string label,
            Color color)
        {
            GameObject buttonObject = CreateUiObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI text = CreateText(
                "Text", buttonObject.transform, font, label, 24f, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            SetStretchRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetStretchRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
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
