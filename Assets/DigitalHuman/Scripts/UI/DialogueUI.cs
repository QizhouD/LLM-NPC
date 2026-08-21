using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DialogueUI : MonoBehaviour
    {
        [SerializeField] private GameObject dialoguePanel;
        [SerializeField] private TMP_Text interactionPromptText;
        [SerializeField] private TMP_Text npcNameText;
        [SerializeField] private TMP_Text dialogueText;
        [SerializeField] private TMP_InputField playerInputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button closeButton;
        [SerializeField, Range(4, 30)] private int maxHistoryEntries = 8;
        [SerializeField] private bool persistentPanel = true;

        private readonly List<string> history = new List<string>();
        private string displayedDialogue = string.Empty;
        private string activeNpcName = "EVA";

        public bool IsOpen => dialoguePanel != null && dialoguePanel.activeSelf;
        public bool IsPersistent => persistentPanel;
        public bool IsInteractionPromptVisible =>
            interactionPromptText != null && interactionPromptText.gameObject.activeSelf;
        public string DisplayedNpcName => npcNameText != null
            ? npcNameText.text
            : string.Empty;
        public string DisplayedDialogue => displayedDialogue;
        public string ConversationHistory => string.Join("\n", history);
        public TMP_InputField PlayerInputField => playerInputField;
        public Button SendButton => sendButton;
        public Button CloseButton => closeButton;

        /// <summary>
        /// True while the command input field has keyboard focus.
        /// Player movement should ignore WASD in this state so IME/typing works.
        /// </summary>
        public bool IsInputFocused =>
            playerInputField != null && playerInputField.isFocused;

        public event Action<string> MessageSubmitted;
        public event Action CloseRequested;

        private void Awake()
        {
            EnableTmpShaderChannels();
            PrepareInputField();
            ApplyPersistentPanelState();
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(RequestSubmit);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(RequestClose);
            }

            if (playerInputField != null)
            {
                playerInputField.onSubmit.AddListener(HandleInputSubmitted);
            }
        }

        private void OnDestroy()
        {
            if (sendButton != null)
            {
                sendButton.onClick.RemoveListener(RequestSubmit);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
            }

            if (playerInputField != null)
            {
                playerInputField.onSubmit.RemoveListener(HandleInputSubmitted);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (!persistentPanel && IsOpen && keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                RequestClose();
            }
        }

        public void Configure(
            GameObject panel,
            TMP_Text prompt,
            TMP_Text nameLabel,
            TMP_Text dialogueLabel,
            TMP_InputField inputField,
            Button submitButton,
            Button dismissButton)
        {
            dialoguePanel = panel;
            interactionPromptText = prompt;
            npcNameText = nameLabel;
            dialogueText = dialogueLabel;
            playerInputField = inputField;
            sendButton = submitButton;
            closeButton = dismissButton;
            ApplyPersistentPanelState();
        }

        public void ConfigurePersistentPanel(bool persistent, int historyEntries = 8)
        {
            persistentPanel = persistent;
            maxHistoryEntries = Mathf.Clamp(historyEntries, 4, 30);
            ApplyPersistentPanelState();
        }

        public void ShowInteractionPrompt(
            bool visible,
            string prompt = "Press E to Talk")
        {
            if (interactionPromptText == null)
            {
                return;
            }

            interactionPromptText.text = prompt;
            interactionPromptText.gameObject.SetActive(visible && !persistentPanel);
        }

        public void ShowDialogue(string npcName, string initialDialogue = "")
        {
            activeNpcName = string.IsNullOrWhiteSpace(npcName) ? "NPC" : npcName;
            if (npcNameText != null)
            {
                npcNameText.text = activeNpcName;
            }

            history.Clear();
            displayedDialogue = initialDialogue ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(displayedDialogue))
            {
                AddHistoryEntry($"{activeNpcName}: {displayedDialogue}");
            }

            RenderHistory();
            ShowInteractionPrompt(false);
            if (dialoguePanel != null)
            {
                dialoguePanel.SetActive(true);
            }

            FocusInput();
        }

        /// <summary>
        /// Selects the command input so the player can type immediately.
        /// </summary>
        public void FocusInput()
        {
            if (playerInputField == null || !playerInputField.interactable)
            {
                return;
            }

            playerInputField.text = string.Empty;
            playerInputField.Select();
            playerInputField.ActivateInputField();
        }

        public void HideDialogue()
        {
            if (persistentPanel)
            {
                return;
            }

            if (playerInputField != null)
            {
                playerInputField.DeactivateInputField();
            }

            if (dialoguePanel != null)
            {
                dialoguePanel.SetActive(false);
            }
        }

        public void AppendPlayerMessage(string message)
        {
            string safeMessage = message != null ? message.Trim() : string.Empty;
            if (string.IsNullOrEmpty(safeMessage))
            {
                return;
            }

            AddHistoryEntry($"You: {safeMessage}");
            RenderHistory();
        }

        public void SetDialogueText(string text)
        {
            displayedDialogue = text ?? string.Empty;
            if (IsOpen && !string.IsNullOrWhiteSpace(displayedDialogue))
            {
                AddHistoryEntry($"{activeNpcName}: {displayedDialogue}");
                RenderHistory();
            }
            else if (dialogueText != null)
            {
                dialogueText.text = displayedDialogue;
            }
        }

        public void SetThinking(bool isThinking)
        {
            if (isThinking)
            {
                displayedDialogue = "Thinking...";
                RenderHistory($"{activeNpcName}: Thinking...");
            }
            else
            {
                RenderHistory();
            }

            if (playerInputField != null)
            {
                playerInputField.interactable = !isThinking;
            }

            if (sendButton != null)
            {
                sendButton.interactable = !isThinking;
            }
        }

        public void RequestSubmit()
        {
            if (playerInputField == null || !playerInputField.interactable)
            {
                return;
            }

            string message = playerInputField.text.Trim();
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            playerInputField.text = string.Empty;
            MessageSubmitted?.Invoke(message);
            playerInputField.ActivateInputField();
        }

        public void RequestClose()
        {
            if (persistentPanel || !IsOpen)
            {
                return;
            }

            HideDialogue();
            CloseRequested?.Invoke();
        }

        private void HandleInputSubmitted(string _)
        {
            RequestSubmit();
        }

        private void AddHistoryEntry(string entry)
        {
            history.Add(entry);
            while (history.Count > maxHistoryEntries)
            {
                history.RemoveAt(0);
            }
        }

        private void RenderHistory(string temporaryEntry = null)
        {
            if (dialogueText == null)
            {
                return;
            }

            string transcript = string.Join("\n", history);
            if (!string.IsNullOrWhiteSpace(temporaryEntry))
            {
                transcript = string.IsNullOrEmpty(transcript)
                    ? temporaryEntry
                    : transcript + "\n" + temporaryEntry;
            }

            dialogueText.text = transcript;
        }

        private void ApplyPersistentPanelState()
        {
            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(!persistentPanel);
            }

            if (interactionPromptText != null && persistentPanel)
            {
                interactionPromptText.gameObject.SetActive(false);
            }

            // Persistent command chat must stay visible; the scene previously
            // serialized DialoguePanel as inactive, which hid the input field.
            if (persistentPanel && dialoguePanel != null)
            {
                dialoguePanel.SetActive(true);
            }
        }

        private void EnableTmpShaderChannels()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.additionalShaderChannels |=
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;
        }

        private void PrepareInputField()
        {
            if (playerInputField == null)
            {
                return;
            }

            playerInputField.interactable = true;
            playerInputField.readOnly = false;
            playerInputField.richText = false;
            playerInputField.lineType = TMP_InputField.LineType.SingleLine;

            TMP_Text placeholder = playerInputField.placeholder as TMP_Text;
            if (placeholder != null)
            {
                placeholder.text = CommandChatLayout.DefaultPlaceholder;
            }
        }

        private void OnValidate()
        {
            maxHistoryEntries = Mathf.Clamp(maxHistoryEntries, 4, 30);
            ApplyPersistentPanelState();
        }
    }
}
