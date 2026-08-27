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

        /// <summary>
        /// Raised alongside <see cref="NpcResponseReceived"/> with the behaviour tags the
        /// model attached to the reply. Drives animation and face.
        /// </summary>
        public event Action<ResponseAnnotation> ResponseAnnotationReceived;
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

            // The keyword parser used to run first and return, which meant every command-like
            // input bypassed the model entirely. The model now always gets the message; the
            // parser only supplies the world action, or takes over if the model is unreachable.
            bool looksLikeCommand = agentCommandController != null &&
                                    agentCommandController.CanHandle(trimmedMessage);
            bool modelAnswered = false;

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
                if (string.IsNullOrWhiteSpace(response))
                {
                    ReceiveNpcResponse(requestFailedMessage);
                }
                else
                {
                    ReceiveNpcResponse(response.Trim());
                    modelAnswered = service != null;
                }
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

            if (!looksLikeCommand || !IsDialogueActive)
            {
                return;
            }

            // The model has already spoken, so the parser executes the world action without
            // overwriting the reply. Failures still speak, because silence would be worse.
            agentCommandController.ExecuteCommand(
                trimmedMessage, announceAcknowledgements: !modelAnswered);
        }

        public void ReceiveNpcResponse(string response)
        {
            if (!IsDialogueActive)
            {
                return;
            }

            // Tags are stripped before anything downstream sees the text, so they can never
            // reach the dialogue box or the voice service.
            ResponseAnnotation annotation =
                ResponseAnnotationParser.Parse(response ?? string.Empty);

            dialogueUI?.SetDialogueText(annotation.Text);
            NpcResponseReceived?.Invoke(annotation.Text);
            ResponseAnnotationReceived?.Invoke(annotation);
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
