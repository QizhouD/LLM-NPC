using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalHuman
{
    /// <summary>
    /// Runtime layout for the command chat. Attach to DialogueCanvas.
    ///
    /// Why this exists:
    /// The previous panel used a decorative frame sprite as a stretched
    /// background and sat in a tiny bottom-right corner. On typical Game
    /// view sizes the input field collapsed below the font size, so typed
    /// text was clipped by RectMask2D and looked like "input does not work".
    ///
    /// This component rebuilds a bottom-center command console that keeps a
    /// usable fraction of the screen at any resolution.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    [RequireComponent(typeof(Canvas))]
    public sealed class CommandChatLayout : MonoBehaviour
    {
        public const string InputRowName = "InputRow";
        public const string DefaultPlaceholder =
            "Type a command, e.g. pick up the red box";
        public const string DefaultSendLabel = "Send";
        public const string DefaultGreeting = "Type a command to control me.";

        [Header("References")]
        [SerializeField] private DialogueUI dialogueUI;
        [SerializeField] private RectTransform panel;
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("If enabled, Play Mode overwrites the scene RectTransforms. Leave off so Inspector layout edits persist.")]
        [SerializeField] private bool applyOnAwake;

        [Header("Canvas")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float matchWidthOrHeight = 0.5f;

        [Header("Panel (screen fractions)")]
        [SerializeField] private Vector2 panelAnchorMin = new Vector2(0.08f, 0.03f);
        [SerializeField] private Vector2 panelAnchorMax = new Vector2(0.92f, 0.289f);
        [SerializeField] private Color panelColor = new Color(0.05f, 0.07f, 0.11f, 0.94f);

        [Header("Inner layout (panel fractions)")]
        [SerializeField] private Vector2 nameAnchorMin = new Vector2(0.03f, 0.84f);
        [SerializeField] private Vector2 nameAnchorMax = new Vector2(0.97f, 0.97f);
        [SerializeField] private Vector2 historyAnchorMin = new Vector2(0.03f, 0.30f);
        [SerializeField] private Vector2 historyAnchorMax = new Vector2(0.97f, 0.82f);
        [SerializeField] private Vector2 inputAnchorMin = new Vector2(0.03f, 0.05f);
        [SerializeField] private Vector2 inputAnchorMax = new Vector2(0.74f, 0.26f);
        [SerializeField] private Vector2 sendAnchorMin = new Vector2(0.76f, 0.05f);
        [SerializeField] private Vector2 sendAnchorMax = new Vector2(0.97f, 0.26f);

        [Header("Typography")]
        [SerializeField, Min(16f)] private float nameFontSize = 28f;
        [SerializeField, Min(14f)] private float historyFontSize = 22f;
        [SerializeField, Min(16f)] private float inputFontSize = 22f;
        [SerializeField, Min(16f)] private float sendFontSize = 24f;

        public DialogueUI DialogueUI => dialogueUI;
        public RectTransform Panel => panel;

        private void Awake()
        {
            ResolveReferences();
            if (applyOnAwake)
            {
                ApplyLayout();
            }
        }

        [ContextMenu("Apply Command Chat Layout")]
        private void ApplyLayoutFromContextMenu()
        {
            ApplyLayout();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Bind the DialogueUI this layout should drive. Safe to call from
        /// editor bootstraps and from runtime.
        /// </summary>
        public void Bind(DialogueUI ui, TMP_FontAsset overrideFont = null)
        {
            dialogueUI = ui;
            if (overrideFont != null)
            {
                font = overrideFont;
            }

            ResolveReferences();
        }

        /// <summary>
        /// Adds this component if missing, then applies the command-console
        /// layout. Existing DialogueUI references stay valid.
        /// </summary>
        public static CommandChatLayout EnsureConfigured(
            DialogueUI ui,
            TMP_FontAsset overrideFont = null)
        {
            if (ui == null)
            {
                throw new System.ArgumentNullException(nameof(ui));
            }

            CommandChatLayout layout = ui.GetComponent<CommandChatLayout>();
            if (layout == null)
            {
                layout = ui.gameObject.AddComponent<CommandChatLayout>();
            }

            layout.Bind(ui, overrideFont);
            layout.ApplyLayout();
            return layout;
        }

        /// <summary>
        /// Rebuilds canvas, panel, and child rects so the input field stays
        /// large enough to click and type on any Game view size.
        /// </summary>
        public void ApplyLayout()
        {
            ResolveReferences();
            if (dialogueUI == null || panel == null)
            {
                Debug.LogError(
                    "[AgentNPC] CommandChatLayout is missing DialogueUI or DialoguePanel.",
                    this);
                return;
            }

            ConfigureCanvas();
            ConfigurePanel();
            ConfigureChildren();
            ConfigureInputField();
            ConfigureSendButton();
            dialogueUI.ConfigurePersistentPanel(true, 8);
            panel.gameObject.SetActive(true);
        }

        private void ConfigureCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (canvas == null || scaler == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = matchWidthOrHeight;
            scaler.referencePixelsPerUnit = 100f;

            if (raycaster != null)
            {
                raycaster.enabled = true;
            }
        }

        private void ConfigurePanel()
        {
            SetStretch(panel, panelAnchorMin, panelAnchorMax);
            panel.pivot = new Vector2(0.5f, 0f);

            Image background = panel.GetComponent<Image>();
            if (background == null)
            {
                background = panel.gameObject.AddComponent<Image>();
            }

            // Do not stretch the generated decorative frame: its portrait
            // hole and outer margins eat the usable input area.
            background.sprite = null;
            background.color = panelColor;
            background.raycastTarget = true;
            background.type = Image.Type.Simple;
        }

        private void ConfigureChildren()
        {
            RectTransform nameRect = FindChild(panel, "NPCNameText");
            RectTransform historyRect = FindChild(panel, "DialogueText");
            RectTransform inputRect = FindChild(panel, "PlayerInputField");
            RectTransform sendRect = FindChild(panel, "SendButton");
            if (nameRect == null || historyRect == null ||
                inputRect == null || sendRect == null)
            {
                Debug.LogError(
                    "[AgentNPC] Command chat children are incomplete.",
                    this);
                return;
            }

            SetStretch(nameRect, nameAnchorMin, nameAnchorMax);
            SetStretch(historyRect, historyAnchorMin, historyAnchorMax);
            SetStretch(inputRect, inputAnchorMin, inputAnchorMax);
            SetStretch(sendRect, sendAnchorMin, sendAnchorMax);

            TMP_Text nameText = nameRect.GetComponent<TMP_Text>();
            TMP_Text historyText = historyRect.GetComponent<TMP_Text>();
            if (nameText != null)
            {
                ApplyFont(nameText, nameFontSize, FontStyles.Bold);
                nameText.alignment = TextAlignmentOptions.Left;
                nameText.raycastTarget = false;
                nameText.color = new Color(0.55f, 0.90f, 1f, 1f);
            }

            if (historyText != null)
            {
                ApplyFont(historyText, historyFontSize, FontStyles.Normal);
                historyText.alignment = TextAlignmentOptions.TopLeft;
                historyText.textWrappingMode = TextWrappingModes.Normal;
                historyText.overflowMode = TextOverflowModes.Truncate;
                historyText.raycastTarget = false;
                historyText.color = Color.white;
            }
        }

        private void ConfigureInputField()
        {
            if (dialogueUI == null || dialogueUI.PlayerInputField == null)
            {
                return;
            }

            TMP_InputField input = dialogueUI.PlayerInputField;
            input.interactable = true;
            input.readOnly = false;
            input.richText = false;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.pointSize = inputFontSize;
            input.shouldHideMobileInput = false;
            input.caretColor = Color.white;
            input.customCaretColor = true;

            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = new Color(0.10f, 0.13f, 0.18f, 1f);
                image.raycastTarget = true;
            }

            RectTransform textArea = input.transform.Find("Text Area") as RectTransform;
            if (textArea != null)
            {
                textArea.anchorMin = Vector2.zero;
                textArea.anchorMax = Vector2.one;
                textArea.offsetMin = new Vector2(14f, 8f);
                textArea.offsetMax = new Vector2(-14f, -8f);
            }

            if (input.textViewport == null && textArea != null)
            {
                input.textViewport = textArea;
            }

            TMP_Text text = input.textComponent;
            if (text != null)
            {
                ApplyFont(text, inputFontSize, FontStyles.Normal);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                text.color = Color.white;
            }

            TMP_Text placeholder = input.placeholder as TMP_Text;
            if (placeholder != null)
            {
                ApplyFont(placeholder, inputFontSize, FontStyles.Italic);
                placeholder.alignment = TextAlignmentOptions.MidlineLeft;
                placeholder.textWrappingMode = TextWrappingModes.NoWrap;
                placeholder.overflowMode = TextOverflowModes.Overflow;
                placeholder.raycastTarget = false;
                placeholder.color = new Color(1f, 1f, 1f, 0.45f);
                placeholder.text = DefaultPlaceholder;
            }

            if (input.textComponent != null && font != null)
            {
                input.fontAsset = font;
            }
        }

        private void ConfigureSendButton()
        {
            if (dialogueUI == null || dialogueUI.SendButton == null)
            {
                return;
            }

            Button send = dialogueUI.SendButton;
            send.interactable = true;
            send.navigation = new Navigation { mode = Navigation.Mode.None };

            Image image = send.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = new Color(0.12f, 0.52f, 0.78f, 1f);
                image.raycastTarget = true;
            }

            TMP_Text label = send.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                ApplyFont(label, sendFontSize, FontStyles.Bold);
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                label.color = Color.white;
                label.text = DefaultSendLabel;
            }
        }

        private void ApplyFont(TMP_Text text, float size, FontStyles style)
        {
            if (font != null)
            {
                text.font = font;
            }

            text.enableAutoSizing = false;
            text.fontSize = size;
            text.fontStyle = style;
        }

        private void ResolveReferences()
        {
            if (dialogueUI == null)
            {
                dialogueUI = GetComponent<DialogueUI>();
            }

            if (panel == null && dialogueUI != null)
            {
                Transform found = dialogueUI.transform.Find("DialoguePanel");
                if (found != null)
                {
                    panel = found as RectTransform;
                }
            }
        }

        private static RectTransform FindChild(Transform parent, string childName)
        {
            Transform child = parent != null ? parent.Find(childName) : null;
            return child as RectTransform;
        }

        private static void SetStretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void OnValidate()
        {
            matchWidthOrHeight = Mathf.Clamp01(matchWidthOrHeight);
            nameFontSize = Mathf.Max(16f, nameFontSize);
            historyFontSize = Mathf.Max(14f, historyFontSize);
            inputFontSize = Mathf.Max(16f, inputFontSize);
            sendFontSize = Mathf.Max(16f, sendFontSize);
        }
    }
}
