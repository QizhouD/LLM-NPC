using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DialogueController : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionDetector interactionDetector;
        [SerializeField] private DialogueUI dialogueUI;
        [SerializeField] private MonoBehaviour conversationServiceComponent;
        [SerializeField] private AgentCommandController agentCommandController;
        [SerializeField] private bool autoStartDialogue = true;
        [SerializeField] private bool allowRemoteDialogue = true;
        [Tooltip("Chat title. Change this field (not only the UI text) if you want a custom NPC name.")]
        [SerializeField] private string npcName = "EVA";
        [SerializeField] private string greeting = "Type a command to control me.";
        [SerializeField] private string unavailableMessage =
            "Conversation service is not configured yet.";
        [SerializeField] private string requestFailedMessage =
            "I could not respond just now. Please try again.";

        private IConversationService conversationService;
        private CancellationTokenSource requestCancellation;

        public bool IsDialogueActive { get; private set; }
        public bool IsBusy { get; private set; }
        public bool AutoStartDialogue => autoStartDialogue;
        public bool AllowRemoteDialogue => allowRemoteDialogue;
        public string NpcName => npcName;
        public string Greeting => greeting;

        public event Action DialogueStarted;
        public event Action DialogueEnded;
        public event Action<string> PlayerMessageSubmitted;
        public event Action ThinkingStarted;
        public event Action<string> NpcResponseReceived;
        public event Action<Exception> RequestFailed;

        private void Awake()
        {
            ResolveConversationService();
            ResolveAgentCommands();
        }

        private void OnEnable()
        {
            ResolveConversationService();
            ResolveAgentCommands();
            SubscribeEvents();
        }

        private void Start()
        {
            if (autoStartDialogue)
            {
                StartDialogue();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            CancelActiveRequest();
        }

        public void Configure(
            PlayerInteractionDetector detector,
            DialogueUI ui,
            MonoBehaviour serviceComponent)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
            {
                UnsubscribeEvents();
            }

            interactionDetector = detector;
            dialogueUI = ui;
            conversationServiceComponent = serviceComponent;
            ResolveConversationService();
            ResolveAgentCommands();

            if (wasEnabled)
            {
                SubscribeEvents();
            }
        }

        public void ConfigureAgentCommands(AgentCommandController controller)
        {
            agentCommandController = controller;
        }

        public void ConfigureAgentDemoAccess(bool autoOpen, bool remoteAccess)
        {
            autoStartDialogue = autoOpen;
            allowRemoteDialogue = remoteAccess;
        }

        public void ConfigureGreeting(string npc, string greetingText)
        {
            if (!string.IsNullOrWhiteSpace(npc))
            {
                npcName = npc.Trim();
            }

            if (!string.IsNullOrWhiteSpace(greetingText))
            {
                greeting = greetingText.Trim();
            }
        }

        public void SetConversationService(IConversationService service)
        {
            conversationService = service;
        }

        public void StartDialogue(Transform player = null)
        {
            if (IsDialogueActive || dialogueUI == null)
            {
                return;
            }

            IsDialogueActive = true;
            dialogueUI.ShowInteractionPrompt(false);
            dialogueUI.ShowDialogue(ResolveDisplayName(), greeting);
            DialogueStarted?.Invoke();
            Debug.Log("[DigitalHuman] Dialogue started.", this);
        }

        public void EndDialogue()
        {
            if (!IsDialogueActive)
            {
                return;
            }

            IsDialogueActive = false;
            CancelActiveRequest();
            if (dialogueUI != null)
            {
                dialogueUI.SetThinking(false);
                dialogueUI.HideDialogue();
                dialogueUI.ShowInteractionPrompt(
                    !allowRemoteDialogue &&
                    interactionDetector != null &&
                    interactionDetector.CanInteract);
            }

            DialogueEnded?.Invoke();
            Debug.Log("[DigitalHuman] Dialogue ended.", this);
        }

        public async Task SendPlayerMessageAsync(string message)
        {
            string trimmedMessage = message != null ? message.Trim() : string.Empty;
            if (!IsDialogueActive || IsBusy || string.IsNullOrEmpty(trimmedMessage))
            {
                return;
            }

            dialogueUI?.AppendPlayerMessage(trimmedMessage);
            PlayerMessageSubmitted?.Invoke(trimmedMessage);
            ResolveAgentCommands();
            if (agentCommandController != null &&
                agentCommandController.ExecuteCommand(trimmedMessage))
            {
                return;
            }

            IsBusy = true;
            ThinkingStarted?.Invoke();
            dialogueUI?.SetThinking(true);

            CancellationTokenSource localCancellation = new CancellationTokenSource();
            requestCancellation = localCancellation;

            try
            {
                IConversationService service = conversationService;
                string response;
                if (service == null)
                {
                    await Task.Yield();
                    response = unavailableMessage;
                }
                else
                {
                    response = await service.GetResponseAsync(
                        trimmedMessage,
                        localCancellation.Token);
                }

                localCancellation.Token.ThrowIfCancellationRequested();
                ReceiveNpcResponse(string.IsNullOrWhiteSpace(response)
                    ? requestFailedMessage
                    : response.Trim());
            }
            catch (OperationCanceledException) when (localCancellation.IsCancellationRequested)
            {
                // Dialogue closure intentionally cancels in-flight requests.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DigitalHuman] Conversation request failed: {exception}", this);
                if (IsDialogueActive)
                {
                    dialogueUI?.SetDialogueText(requestFailedMessage);
                }

                RequestFailed?.Invoke(exception);
            }
            finally
            {
                if (ReferenceEquals(requestCancellation, localCancellation))
                {
                    requestCancellation = null;
                    IsBusy = false;
                    if (IsDialogueActive)
                    {
                        dialogueUI?.SetThinking(false);
                    }
                }

                localCancellation.Dispose();
            }
        }

        public void ReceiveNpcResponse(string response)
        {
            if (!IsDialogueActive)
            {
                return;
            }

            string safeResponse = response ?? string.Empty;
            dialogueUI?.SetDialogueText(safeResponse);
            NpcResponseReceived?.Invoke(safeResponse);
        }

        private void HandlePlayerEnteredRange(Transform _)
        {
            if (!allowRemoteDialogue && !IsDialogueActive)
            {
                dialogueUI?.ShowInteractionPrompt(true);
            }
        }

        private void HandlePlayerExitedRange(Transform _)
        {
            dialogueUI?.ShowInteractionPrompt(false);
            if (allowRemoteDialogue ||
                (agentCommandController != null &&
                 agentCommandController.IsExecutingCommand))
            {
                return;
            }

            EndDialogue();
        }

        private void HandleInteractionRequested(Transform player)
        {
            StartDialogue(player);
        }

        private async void HandleMessageSubmitted(string message)
        {
            await SendPlayerMessageAsync(message);
        }

        private void SubscribeEvents()
        {
            if (interactionDetector != null)
            {
                interactionDetector.PlayerEnteredRange += HandlePlayerEnteredRange;
                interactionDetector.PlayerExitedRange += HandlePlayerExitedRange;
                interactionDetector.InteractionRequested += HandleInteractionRequested;
            }

            if (dialogueUI != null)
            {
                dialogueUI.MessageSubmitted += HandleMessageSubmitted;
                dialogueUI.CloseRequested += EndDialogue;
            }
        }

        private void UnsubscribeEvents()
        {
            if (interactionDetector != null)
            {
                interactionDetector.PlayerEnteredRange -= HandlePlayerEnteredRange;
                interactionDetector.PlayerExitedRange -= HandlePlayerExitedRange;
                interactionDetector.InteractionRequested -= HandleInteractionRequested;
            }

            if (dialogueUI != null)
            {
                dialogueUI.MessageSubmitted -= HandleMessageSubmitted;
                dialogueUI.CloseRequested -= EndDialogue;
            }
        }

        private string ResolveDisplayName()
        {
            string controllerName = string.IsNullOrWhiteSpace(npcName)
                ? "EVA"
                : npcName.Trim();
            if (dialogueUI == null)
            {
                return controllerName;
            }

            string uiName = dialogueUI.DisplayedNpcName;
            if (!string.IsNullOrWhiteSpace(uiName))
            {
                uiName = uiName.Trim();
            }

            // Scene UI text wins when the controller is still the default EVA
            // and the user renamed NPCNameText in the Hierarchy.
            if (string.Equals(controllerName, "EVA", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(uiName) &&
                !string.Equals(uiName, "EVA", StringComparison.Ordinal))
            {
                return uiName;
            }

            return controllerName;
        }

        private void ResolveConversationService()
        {
            conversationService = conversationServiceComponent as IConversationService;
            if (conversationServiceComponent != null && conversationService == null)
            {
                Debug.LogError(
                    "[DigitalHuman] Assigned conversation service does not implement IConversationService.",
                    this);
            }
        }

        private void ResolveAgentCommands()
        {
            if (agentCommandController == null)
            {
                agentCommandController = GetComponent<AgentCommandController>();
            }
        }

        private void CancelActiveRequest()
        {
            CancellationTokenSource cancellation = requestCancellation;
            requestCancellation = null;
            IsBusy = false;
            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();
        }
    }
}
