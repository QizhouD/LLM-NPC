using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Chat backend for DeepSeek's OpenAI-compatible Chat Completions API.
    /// </summary>
    /// <remarks>
    /// Sits alongside <see cref="OpenAILLMService"/> rather than replacing it. Both implement
    /// <see cref="ILLMService"/> and return plain reply text, so the embodied chain downstream —
    /// persona prompt, annotation parsing, plan bridge — is identical whichever one is selected.
    ///
    /// The wire format is *not* the same as the OpenAI service here: that one targets the
    /// Responses API (<c>input</c> / <c>output[].content[].text</c>), while DeepSeek exposes
    /// Chat Completions (<c>messages</c> / <c>choices[].message.content</c>). Only the request
    /// and response shapes differ; the transport, exception types and error taxonomy are shared.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DeepSeekLlmService : MonoBehaviour, ILLMService
    {
        public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";
        public const string DefaultModel = "deepseek-chat";
        public const string DefaultApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";

        [SerializeField] private string endpoint = DefaultEndpoint;
        [Tooltip("deepseek-chat is the general model; deepseek-reasoner is the reasoning one.")]
        [SerializeField] private string model = DefaultModel;
        [SerializeField] private string apiKeyEnvironmentVariable =
            DefaultApiKeyEnvironmentVariable;
        [SerializeField, Range(5, 120)] private int timeoutSeconds = 30;
        [SerializeField, Range(32, 2048)] private int maxTokens = 160;
        [SerializeField, Range(0f, 2f)] private float temperature = 1f;

        private ILLMHttpTransport transport;

        public string Endpoint => endpoint;
        public string Model => model;
        public string ApiKeyEnvironmentVariable => apiKeyEnvironmentVariable;

        private void Awake()
        {
            EnsureTransport();
        }

        public void Configure(
            string apiEndpoint,
            string modelId,
            string apiKeyEnvironmentName = DefaultApiKeyEnvironmentVariable,
            int requestTimeoutSeconds = 30,
            int responseTokenLimit = 160,
            float samplingTemperature = 1f)
        {
            endpoint = string.IsNullOrWhiteSpace(apiEndpoint)
                ? DefaultEndpoint
                : apiEndpoint.Trim();
            model = string.IsNullOrWhiteSpace(modelId) ? DefaultModel : modelId.Trim();
            apiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(apiKeyEnvironmentName)
                ? DefaultApiKeyEnvironmentVariable
                : apiKeyEnvironmentName.Trim();
            timeoutSeconds = Mathf.Clamp(requestTimeoutSeconds, 5, 120);
            maxTokens = Mathf.Clamp(responseTokenLimit, 32, 2048);
            temperature = Mathf.Clamp(samplingTemperature, 0f, 2f);
            EnsureTransport();
        }

        public void SetTransport(ILLMHttpTransport httpTransport)
        {
            transport = httpTransport ?? throw new ArgumentNullException(nameof(httpTransport));
        }

        public async Task<string> SendMessageAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateConfiguration(messages);

            string apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // Configuration, not Api: AIConversationManager treats this as "backend
                // unavailable" and degrades to the offline heuristic, exactly as it does when
                // OPENAI_API_KEY is absent.
                throw new LLMServiceException(
                    LLMErrorKind.Configuration,
                    $"Environment variable '{apiKeyEnvironmentVariable}' is not configured.");
            }

            EnsureTransport();
            string requestJson = BuildRequestJson(messages);
            Debug.Log(
                $"[DigitalHuman] Sending {messages.Count} messages to the DeepSeek Chat " +
                $"Completions API (model: {model}).",
                this);

            LLMHttpResponse response;
            try
            {
                response = await transport.PostJsonAsync(
                    endpoint,
                    requestJson,
                    apiKey.Trim(),
                    timeoutSeconds,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException exception)
            {
                throw new LLMServiceException(
                    LLMErrorKind.Timeout,
                    "The LLM request timed out.",
                    0,
                    exception);
            }
            catch (Exception exception)
            {
                throw new LLMServiceException(
                    LLMErrorKind.Network,
                    $"The LLM request failed before receiving a response: {exception.Message}",
                    0,
                    exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (response == null)
            {
                throw new LLMServiceException(
                    LLMErrorKind.Network,
                    "The LLM transport returned no response.");
            }

            if (response.TimedOut)
            {
                throw new LLMServiceException(LLMErrorKind.Timeout, "The LLM request timed out.");
            }

            if (!string.IsNullOrWhiteSpace(response.TransportError))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Network,
                    $"LLM network error: {response.TransportError}");
            }

            if (!response.IsSuccess)
            {
                string apiError = TryExtractApiError(response.Body);
                if (!string.IsNullOrWhiteSpace(apiError))
                {
                    throw new LLMServiceException(
                        LLMErrorKind.Api,
                        $"DeepSeek API error ({response.StatusCode}): {apiError}",
                        response.StatusCode);
                }

                throw new LLMServiceException(
                    LLMErrorKind.Http,
                    $"DeepSeek HTTP error {response.StatusCode}.",
                    response.StatusCode);
            }

            string result = ParseResponseJson(response.Body);
            Debug.Log("[DigitalHuman] LLM response received.", this);
            return result;
        }

        public string BuildRequestJson(IReadOnlyList<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                throw new ArgumentException(
                    "At least one conversation message is required.",
                    nameof(messages));
            }

            ChatMessage[] chatMessages = new ChatMessage[messages.Count];
            for (int i = 0; i < messages.Count; i++)
            {
                ConversationMessage message = messages[i];
                if (message == null ||
                    string.IsNullOrWhiteSpace(message.Role) ||
                    string.IsNullOrWhiteSpace(message.Content))
                {
                    throw new ArgumentException(
                        $"Conversation message at index {i} is invalid.",
                        nameof(messages));
                }

                chatMessages[i] = new ChatMessage
                {
                    role = message.Role,
                    content = message.Content
                };
            }

            ChatCompletionRequest request = new ChatCompletionRequest
            {
                model = model,
                messages = chatMessages,
                max_tokens = maxTokens,
                temperature = temperature,
                // Streaming would need an incremental transport; the dialogue box shows the
                // reply in one go, and the annotation parser needs the whole text anyway.
                stream = false
            };
            return JsonUtility.ToJson(request);
        }

        public string ParseResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    "The DeepSeek response body was empty.");
            }

            ChatCompletionResponse response;
            try
            {
                response = JsonUtility.FromJson<ChatCompletionResponse>(json);
            }
            catch (Exception exception)
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    $"Unable to parse the DeepSeek response JSON: {exception.Message}",
                    0,
                    exception);
            }

            if (response == null)
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    "Unable to parse the DeepSeek response JSON.");
            }

            if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Api,
                    $"DeepSeek API error: {response.error.message}");
            }

            StringBuilder outputText = new StringBuilder();
            if (response.choices != null)
            {
                for (int i = 0; i < response.choices.Length; i++)
                {
                    ChatChoice choice = response.choices[i];
                    if (choice == null || choice.message == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(choice.message.refusal))
                    {
                        throw new LLMServiceException(
                            LLMErrorKind.Api,
                            $"The model refused the request: {choice.message.refusal}");
                    }

                    if (string.IsNullOrWhiteSpace(choice.message.content))
                    {
                        continue;
                    }

                    if (outputText.Length > 0)
                    {
                        outputText.AppendLine();
                    }

                    outputText.Append(choice.message.content.Trim());
                }
            }

            if (outputText.Length == 0)
            {
                throw new LLMServiceException(
                    LLMErrorKind.EmptyResponse,
                    "The DeepSeek response did not contain any message content.");
            }

            return outputText.ToString();
        }

        private void ValidateConfiguration(IReadOnlyList<ConversationMessage> messages)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttps &&
                 endpointUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Configuration,
                    $"LLM endpoint is invalid: {endpoint}");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Configuration,
                    "LLM model is not configured.");
            }

            BuildRequestJson(messages);
        }

        private static string TryExtractApiError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                ChatCompletionResponse response =
                    JsonUtility.FromJson<ChatCompletionResponse>(json);
                return response != null && response.error != null
                    ? response.error.message
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureTransport()
        {
            if (transport == null)
            {
                transport = new UnityWebRequestLLMTransport();
            }
        }

        private void OnValidate()
        {
            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 120);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
        }

        [Serializable]
        private sealed class ChatCompletionRequest
        {
            public string model;
            public ChatMessage[] messages;
            public int max_tokens;
            public float temperature;
            public bool stream;
        }

        [Serializable]
        private sealed class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class ChatCompletionResponse
        {
            public string id;
            public string model;
            public ChatChoice[] choices;
            public ChatError error;
        }

        [Serializable]
        private sealed class ChatChoice
        {
            public int index;
            public string finish_reason;
            public ChatResponseMessage message;
        }

        [Serializable]
        private sealed class ChatResponseMessage
        {
            public string role;
            public string content;
            public string refusal;
        }

        [Serializable]
        private sealed class ChatError
        {
            public string message;
            public string type;
            public string code;
        }
    }
}
