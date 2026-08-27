using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class OpenAILLMService : MonoBehaviour, ILLMService
    {
        private const string DefaultEndpoint = "https://api.openai.com/v1/responses";
        private const string DefaultModel = "gpt-5.6-terra";
        public const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";

        [SerializeField] private string endpoint = DefaultEndpoint;
        [SerializeField] private string model = DefaultModel;
        [SerializeField] private string apiKeyEnvironmentVariable =
            DefaultApiKeyEnvironmentVariable;
        [SerializeField, Range(5, 120)] private int timeoutSeconds = 30;
        [SerializeField, Range(32, 2048)] private int maxOutputTokens = 160;
        [SerializeField] private string reasoningEffort = "low";

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
            string effort = "low")
        {
            endpoint = string.IsNullOrWhiteSpace(apiEndpoint)
                ? DefaultEndpoint
                : apiEndpoint.Trim();
            model = string.IsNullOrWhiteSpace(modelId) ? DefaultModel : modelId.Trim();
            apiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(apiKeyEnvironmentName)
                ? DefaultApiKeyEnvironmentVariable
                : apiKeyEnvironmentName.Trim();
            timeoutSeconds = Mathf.Clamp(requestTimeoutSeconds, 5, 120);
            maxOutputTokens = Mathf.Clamp(responseTokenLimit, 32, 2048);
            reasoningEffort = string.IsNullOrWhiteSpace(effort) ? "low" : effort.Trim();
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
                throw new LLMServiceException(
                    LLMErrorKind.Configuration,
                    $"Environment variable '{apiKeyEnvironmentVariable}' is not configured.");
            }

            EnsureTransport();
            string requestJson = BuildRequestJson(messages);
            Debug.Log(
                $"[DigitalHuman] Sending {messages.Count} messages to the OpenAI Responses API " +
                $"(model: {model}).",
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
                throw new LLMServiceException(
                    LLMErrorKind.Timeout,
                    "The LLM request timed out.");
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
                        $"OpenAI API error ({response.StatusCode}): {apiError}",
                        response.StatusCode);
                }

                throw new LLMServiceException(
                    LLMErrorKind.Http,
                    $"OpenAI HTTP error {response.StatusCode}.",
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

            ResponsesInputMessage[] input = new ResponsesInputMessage[messages.Count];
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

                input[i] = new ResponsesInputMessage
                {
                    role = message.Role,
                    content = message.Content
                };
            }

            ResponsesRequest request = new ResponsesRequest
            {
                model = model,
                input = input,
                max_output_tokens = maxOutputTokens,
                store = false,
                reasoning = new ResponsesReasoning
                {
                    effort = reasoningEffort
                }
            };
            return JsonUtility.ToJson(request);
        }

        public string ParseResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    "The OpenAI response body was empty.");
            }

            ResponsesResponse response;
            try
            {
                response = JsonUtility.FromJson<ResponsesResponse>(json);
            }
            catch (Exception exception)
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    $"Unable to parse the OpenAI response JSON: {exception.Message}",
                    0,
                    exception);
            }

            if (response == null)
            {
                throw new LLMServiceException(
                    LLMErrorKind.InvalidJson,
                    "Unable to parse the OpenAI response JSON.");
            }

            if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Api,
                    $"OpenAI API error: {response.error.message}");
            }

            StringBuilder outputText = new StringBuilder();
            if (response.output != null)
            {
                for (int outputIndex = 0; outputIndex < response.output.Length; outputIndex++)
                {
                    ResponsesOutputItem outputItem = response.output[outputIndex];
                    if (outputItem == null || outputItem.content == null)
                    {
                        continue;
                    }

                    for (int contentIndex = 0;
                         contentIndex < outputItem.content.Length;
                         contentIndex++)
                    {
                        ResponsesOutputContent content = outputItem.content[contentIndex];
                        if (content == null)
                        {
                            continue;
                        }

                        if (content.type == "refusal" &&
                            !string.IsNullOrWhiteSpace(content.refusal))
                        {
                            throw new LLMServiceException(
                                LLMErrorKind.Api,
                                $"The model refused the request: {content.refusal}");
                        }

                        if (content.type != "output_text" ||
                            string.IsNullOrWhiteSpace(content.text))
                        {
                            continue;
                        }

                        if (outputText.Length > 0)
                        {
                            outputText.AppendLine();
                        }

                        outputText.Append(content.text.Trim());
                    }
                }
            }

            if (outputText.Length == 0)
            {
                throw new LLMServiceException(
                    LLMErrorKind.EmptyResponse,
                    "The OpenAI response did not contain output text.");
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
                ResponsesResponse response = JsonUtility.FromJson<ResponsesResponse>(json);
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
            maxOutputTokens = Mathf.Clamp(maxOutputTokens, 32, 2048);
        }

        [Serializable]
        private sealed class ResponsesRequest
        {
            public string model;
            public ResponsesInputMessage[] input;
            public int max_output_tokens;
            public bool store;
            public ResponsesReasoning reasoning;
        }

        [Serializable]
        private sealed class ResponsesInputMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class ResponsesReasoning
        {
            public string effort;
        }

        [Serializable]
        private sealed class ResponsesResponse
        {
            public string id;
            public string status;
            public ResponsesOutputItem[] output;
            public ResponsesError error;
        }

        [Serializable]
        private sealed class ResponsesOutputItem
        {
            public string type;
            public string role;
            public ResponsesOutputContent[] content;
        }

        [Serializable]
        private sealed class ResponsesOutputContent
        {
            public string type;
            public string text;
            public string refusal;
        }

        [Serializable]
        private sealed class ResponsesError
        {
            public string message;
            public string type;
            public string code;
        }
    }
}
