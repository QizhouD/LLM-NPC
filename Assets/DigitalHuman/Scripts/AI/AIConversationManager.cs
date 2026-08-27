using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AIConversationManager : MonoBehaviour, IConversationService
    {
        [TextArea(2, 6)]
        [SerializeField] private string systemPrompt =
            "You are EVA, a friendly digital human. Reply clearly and concisely.";
        [SerializeField, Min(2)] private int maxHistoryMessages = 16;
        [SerializeField] private DigitalHumanPersona persona;
        [Tooltip("Which chat backend to call. A missing key degrades to offline replies.")]
        [SerializeField] private LLMProvider provider = LLMProvider.OpenAI;
        [Tooltip("Backend used when Provider is OpenAI. Normally OpenAILLMService.")]
        [SerializeField] private MonoBehaviour responseProviderComponent;
        [Tooltip("Backend used when Provider is DeepSeek. Normally DeepSeekLlmService.")]
        [SerializeField] private MonoBehaviour deepSeekProviderComponent;
        [SerializeField] private MonoBehaviour gameContextProviderComponent;

        private readonly List<ConversationMessage> conversationHistory =
            new List<ConversationMessage>();
        private IConversationBackend responseProvider;
        private IGameContextProvider gameContextProvider;
        private bool offlineFallbackWarningLogged;

        public IReadOnlyList<ConversationMessage> ConversationHistory => conversationHistory;
        public string SystemPrompt => systemPrompt;
        public int MaxHistoryMessages => maxHistoryMessages;
        public DigitalHumanPersona Persona => persona;
        public IGameContextProvider GameContextProvider => gameContextProvider;
        public string EffectiveSystemPrompt => BuildEffectiveSystemPrompt();
        public LLMProvider Provider => provider;
        /// <summary>False when the selected provider has no backend and replies are offline.</summary>
        public bool HasActiveBackend => responseProvider != null;

        private void Awake()
        {
            if (persona != null)
            {
                systemPrompt = persona.BuildSystemPrompt();
            }

            ResolveResponseProvider();
            ResolveGameContextProvider();
        }

        public void Configure(
            MonoBehaviour providerComponent,
            string prompt,
            int historyLimit = 16)
        {
            persona = null;
            AssignBackendToMatchingSlot(providerComponent);
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                systemPrompt = prompt.Trim();
            }

            maxHistoryMessages = Mathf.Max(2, historyLimit);
            ResolveResponseProvider();
            TrimHistoryToLimit(maxHistoryMessages);
        }

        public void Configure(
            DigitalHumanPersona personaAsset,
            MonoBehaviour providerComponent,
            int historyLimit = 16)
        {
            if (personaAsset == null)
            {
                throw new ArgumentNullException(nameof(personaAsset));
            }

            persona = personaAsset;
            AssignBackendToMatchingSlot(providerComponent);
            systemPrompt = persona.BuildSystemPrompt();
            maxHistoryMessages = Mathf.Max(2, historyLimit);
            ResolveResponseProvider();
            TrimHistoryToLimit(maxHistoryMessages);
        }

        public void SetResponseProvider(IConversationBackend backend)
        {
            responseProvider = backend;
        }

        /// <summary>
        /// Switches provider at runtime. Nothing downstream is affected, so this is safe to
        /// call mid-conversation; the history carries over.
        /// </summary>
        public void SetProvider(LLMProvider selectedProvider)
        {
            provider = selectedProvider;
            offlineFallbackWarningLogged = false;
            ResolveResponseProvider();
        }

        /// <summary>
        /// Fills the DeepSeek slot without disturbing the OpenAI slot or the current selection,
        /// so adding the second provider to an already-wired prefab is non-destructive.
        /// </summary>
        public void SetDeepSeekProvider(MonoBehaviour backend)
        {
            deepSeekProviderComponent = backend;
            ResolveResponseProvider();
        }

        /// <summary>Assigns both backend slots and the active selection in one call.</summary>
        public void ConfigureProviders(
            MonoBehaviour openAiBackend,
            MonoBehaviour deepSeekBackend,
            LLMProvider selectedProvider)
        {
            responseProviderComponent = openAiBackend;
            deepSeekProviderComponent = deepSeekBackend;
            provider = selectedProvider;
            offlineFallbackWarningLogged = false;
            ResolveResponseProvider();
        }

        public void SetGameContextProvider(MonoBehaviour providerComponent)
        {
            gameContextProviderComponent = providerComponent;
            ResolveGameContextProvider();
        }

        public async Task<string> GetResponseAsync(
            string playerMessage,
            CancellationToken cancellationToken = default)
        {
            string trimmedMessage = playerMessage != null ? playerMessage.Trim() : string.Empty;
            if (string.IsNullOrEmpty(trimmedMessage))
            {
                throw new ArgumentException("Player message cannot be empty.", nameof(playerMessage));
            }

            ConversationMessage userMessage =
                new ConversationMessage(ConversationMessage.UserRole, trimmedMessage);
            conversationHistory.Add(userMessage);
            TrimHistoryToLimit(Mathf.Max(1, maxHistoryMessages - 1));

            try
            {
                IReadOnlyList<ConversationMessage> requestMessages = BuildRequestMessages();
                string response;
                if (responseProvider == null)
                {
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    response = OfflineReplyHeuristic.BuildReply(trimmedMessage);
                }
                else
                {
                    response = await SendWithOfflineFallbackAsync(
                        requestMessages, trimmedMessage, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(response))
                {
                    throw new InvalidOperationException(
                        "Conversation backend returned an empty response.");
                }

                string trimmedResponse = response.Trim();
                conversationHistory.Add(new ConversationMessage(
                    ConversationMessage.AssistantRole,
                    trimmedResponse));
                TrimHistoryToLimit(maxHistoryMessages);
                Debug.Log(
                    $"[DigitalHuman] Conversation turn completed. History messages: {conversationHistory.Count}.",
                    this);
                return trimmedResponse;
            }
            catch
            {
                conversationHistory.Remove(userMessage);
                throw;
            }
        }

        /// <summary>
        /// Calls the backend, degrading to the offline heuristic if it is unreachable or
        /// unconfigured. Without this, a missing API key leaves the character mute and
        /// motionless, which makes the whole embodiment layer impossible to evaluate.
        /// Cancellation is never swallowed: that is the player closing the dialogue.
        /// </summary>
        private async Task<string> SendWithOfflineFallbackAsync(
            IReadOnlyList<ConversationMessage> requestMessages,
            string playerMessage,
            CancellationToken cancellationToken)
        {
            try
            {
                return await responseProvider.SendMessageAsync(requestMessages, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!offlineFallbackWarningLogged)
                {
                    offlineFallbackWarningLogged = true;
                    Debug.LogWarning(
                        "[DigitalHuman] Conversation backend unavailable, falling back to " +
                        $"offline replies for the rest of this session: {exception.Message}",
                        this);
                }

                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return OfflineReplyHeuristic.BuildReply(playerMessage);
            }
        }

        public IReadOnlyList<ConversationMessage> BuildRequestMessages()
        {
            List<ConversationMessage> requestMessages =
                new List<ConversationMessage>(conversationHistory.Count + 1)
                {
                    new ConversationMessage(
                        ConversationMessage.SystemRole,
                        BuildEffectiveSystemPrompt())
                };
            requestMessages.AddRange(conversationHistory);
            return requestMessages;
        }

        public void ResetConversation()
        {
            conversationHistory.Clear();
            Debug.Log("[DigitalHuman] Conversation history reset.", this);
        }

        /// <summary>
        /// Routes a single backend handed to <c>Configure</c> into the slot it belongs in and
        /// selects it. Callers that predate provider selection pass one component and expect it
        /// to be the one that gets used, whichever vendor it is.
        /// </summary>
        private void AssignBackendToMatchingSlot(MonoBehaviour backend)
        {
            if (backend is DeepSeekLlmService)
            {
                deepSeekProviderComponent = backend;
                provider = LLMProvider.DeepSeek;
                return;
            }

            responseProviderComponent = backend;
            if (provider == LLMProvider.DeepSeek)
            {
                provider = backend != null ? LLMProvider.OpenAI : LLMProvider.Offline;
            }
        }

        private void ResolveResponseProvider()
        {
            if (provider == LLMProvider.Offline)
            {
                responseProvider = null;
                return;
            }

            MonoBehaviour selected = provider == LLMProvider.DeepSeek
                ? deepSeekProviderComponent
                : responseProviderComponent;

            responseProvider = selected as IConversationBackend;
            if (selected != null && responseProvider == null)
            {
                Debug.LogError(
                    "[DigitalHuman] Assigned response provider does not implement IConversationBackend.",
                    this);
            }
            else if (selected == null)
            {
                // Not an error: an unassigned slot is a legitimate way to run offline.
                Debug.LogWarning(
                    $"[DigitalHuman] Provider is {provider} but no backend component is " +
                    "assigned for it; replies will use the offline heuristic.",
                    this);
            }
        }

        private void ResolveGameContextProvider()
        {
            gameContextProvider = gameContextProviderComponent as IGameContextProvider;
            if (gameContextProviderComponent != null && gameContextProvider == null)
            {
                Debug.LogError(
                    "[DigitalHuman] Assigned game context provider does not implement " +
                    "IGameContextProvider.",
                    this);
            }
        }

        private string BuildEffectiveSystemPrompt()
        {
            string basePrompt = persona != null
                ? persona.BuildSystemPrompt()
                : systemPrompt;
            if (gameContextProvider == null)
            {
                return basePrompt;
            }

            string context = gameContextProvider.GetGameContext();
            if (string.IsNullOrWhiteSpace(context))
            {
                return basePrompt;
            }

            return persona != null
                ? persona.BuildSystemPrompt(context)
                : $"{basePrompt}\n\nCurrent game context:\n{context.Trim()}";
        }

        private void TrimHistoryToLimit(int limit)
        {
            int safeLimit = Mathf.Max(0, limit);
            int excessCount = conversationHistory.Count - safeLimit;
            if (excessCount > 0)
            {
                conversationHistory.RemoveRange(0, excessCount);
            }
        }

        private void OnValidate()
        {
            maxHistoryMessages = Mathf.Max(2, maxHistoryMessages);
        }
    }
}
