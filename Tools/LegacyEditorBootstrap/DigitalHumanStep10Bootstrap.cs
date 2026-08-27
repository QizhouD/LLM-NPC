#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep10Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step10.SmokeState";
        private const string TestApiKeyEnvironmentVariable =
            "DIGITAL_HUMAN_STEP10_TEST_KEY";
        private const string TestApiKey = "step10-test-token-not-a-secret";
        private const string Endpoint = "https://api.openai.com/v1/responses";
        private const string Model = "gpt-5.6-terra";

        private static readonly List<ConversationMessage> TestMessages =
            new List<ConversationMessage>
            {
                new ConversationMessage(
                    ConversationMessage.SystemRole,
                    "You are EVA."),
                new ConversationMessage(
                    ConversationMessage.UserRole,
                    "Hello.")
            };

        private static OpenAILLMService service;
        private static RecordingTransport transport;
        private static Task<string> pendingResponse;
        private static CancellationTokenSource cancellationSource;
        private static int runtimeStage;
        private static int transportCallsBeforeMissingKey;
        private static double deadline;
        private static string previousApiKey;

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

        public static void ConfigureScene()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject digitalHuman = GameObject.Find("DigitalHuman");
                Require(digitalHuman != null, "DigitalHuman root is missing.");
                Require(digitalHuman.GetComponent<AIConversationManager>() != null,
                    "AIConversationManager is missing.");

                OpenAILLMService sceneService =
                    digitalHuman.GetComponent<OpenAILLMService>();
                if (sceneService == null)
                {
                    sceneService = digitalHuman.AddComponent<OpenAILLMService>();
                }

                sceneService.Configure(
                    Endpoint,
                    Model,
                    "OPENAI_API_KEY",
                    30,
                    160,
                    "low");
                EditorUtility.SetDirty(sceneService);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 10 OpenAI LLM Service scene setup completed.");
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
                OpenAILLMService sceneService =
                    UnityEngine.Object.FindFirstObjectByType<OpenAILLMService>();
                Require(sceneService != null, "OpenAILLMService is missing.");
                Require(sceneService is ILLMService,
                    "OpenAILLMService does not implement ILLMService.");
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
                    service = UnityEngine.Object.FindFirstObjectByType<OpenAILLMService>();
                    Require(service != null, "OpenAILLMService is missing in Play Mode.");

                    previousApiKey =
                        Environment.GetEnvironmentVariable(TestApiKeyEnvironmentVariable);
                    Environment.SetEnvironmentVariable(
                        TestApiKeyEnvironmentVariable,
                        TestApiKey);

                    service.Configure(
                        Endpoint,
                        Model,
                        TestApiKeyEnvironmentVariable,
                        10,
                        160,
                        "low");
                    transport = new RecordingTransport();
                    service.SetTransport(transport);
                    pendingResponse = null;
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
                RestoreEnvironment();
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
                        transport.EnqueueResponse(new LLMHttpResponse(
                            200,
                            "{\"id\":\"resp_test\",\"status\":\"completed\"," +
                            "\"output\":[{\"type\":\"message\",\"role\":\"assistant\"," +
                            "\"content\":[{\"type\":\"output_text\"," +
                            "\"text\":\"Hello player.\"}]}]}"));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(2);
                        break;

                    case 2:
                        RequireSuccess("Hello player.");
                        Require(transport.Requests.Count == 1,
                            "Successful request did not reach the transport.");
                        CapturedRequest request = transport.Requests[0];
                        Require(request.Url == Endpoint, "LLM endpoint was not forwarded.");
                        Require(request.BearerToken == TestApiKey,
                            "Environment API key was not forwarded.");
                        Require(request.TimeoutSeconds == 10,
                            "Request timeout was not forwarded.");
                        Require(request.JsonBody.Contains("\"model\":\"gpt-5.6-terra\""),
                            "Request JSON contains the wrong model.");
                        Require(request.JsonBody.Contains("\"role\":\"system\"") &&
                                request.JsonBody.Contains("\"role\":\"user\""),
                            "Request JSON did not preserve conversation roles.");
                        Require(request.JsonBody.Contains("\"max_output_tokens\":160") &&
                                request.JsonBody.Contains("\"effort\":\"low\""),
                            "Request JSON omitted generation controls.");

                        transport.EnqueueResponse(new LLMHttpResponse(
                            429,
                            "{\"error\":{\"message\":\"Rate limit reached\"," +
                            "\"type\":\"rate_limit_error\",\"code\":\"rate_limit\"}}"));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(3);
                        break;

                    case 3:
                        RequireError(LLMErrorKind.Api, 429);
                        transport.EnqueueResponse(new LLMHttpResponse(
                            503,
                            "Service unavailable"));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(4);
                        break;

                    case 4:
                        RequireError(LLMErrorKind.Http, 503);
                        transport.EnqueueResponse(new LLMHttpResponse(200, "not-json"));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(5);
                        break;

                    case 5:
                        RequireError(LLMErrorKind.InvalidJson);
                        transport.EnqueueResponse(new LLMHttpResponse(
                            200,
                            "{\"id\":\"resp_empty\",\"status\":\"completed\",\"output\":[]}"));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(6);
                        break;

                    case 6:
                        RequireError(LLMErrorKind.EmptyResponse);
                        transport.EnqueueException(new TimeoutException("Simulated timeout."));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(7);
                        break;

                    case 7:
                        RequireError(LLMErrorKind.Timeout);
                        transport.EnqueueResponse(new LLMHttpResponse(
                            0,
                            string.Empty,
                            "Simulated DNS failure."));
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(8);
                        break;

                    case 8:
                        RequireError(LLMErrorKind.Network);
                        transportCallsBeforeMissingKey = transport.Requests.Count;
                        Environment.SetEnvironmentVariable(
                            TestApiKeyEnvironmentVariable,
                            null);
                        pendingResponse = service.SendMessageAsync(TestMessages);
                        AdvanceTo(9);
                        break;

                    case 9:
                        RequireError(LLMErrorKind.Configuration);
                        Require(transport.Requests.Count == transportCallsBeforeMissingKey,
                            "A request was sent without an API key.");
                        Environment.SetEnvironmentVariable(
                            TestApiKeyEnvironmentVariable,
                            TestApiKey);
                        cancellationSource = new CancellationTokenSource();
                        cancellationSource.Cancel();
                        pendingResponse = service.SendMessageAsync(
                            TestMessages,
                            cancellationSource.Token);
                        AdvanceTo(10);
                        break;

                    case 10:
                        Require(pendingResponse != null &&
                                pendingResponse.Status == TaskStatus.Canceled,
                            "A pre-cancelled request did not remain cancelled.");
                        RestoreEnvironment();
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log(
                            "[DigitalHuman] STEP 10 Play Mode LLM Service test passed.");
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

        private static void AdvanceTo(int nextStage)
        {
            runtimeStage = nextStage;
            deadline = EditorApplication.timeSinceStartup + 0.08d;
        }

        private static void RequireSuccess(string expectedResponse)
        {
            Require(pendingResponse != null, "LLM task was not created.");
            Require(pendingResponse.Status == TaskStatus.RanToCompletion,
                pendingResponse.Exception != null
                    ? pendingResponse.Exception.ToString()
                    : "LLM task did not complete.");
            Require(pendingResponse.Result == expectedResponse,
                $"Expected '{expectedResponse}', received '{pendingResponse.Result}'.");
        }

        private static void RequireError(
            LLMErrorKind expectedKind,
            long expectedStatusCode = 0)
        {
            Require(pendingResponse != null, "LLM task was not created.");
            Require(pendingResponse.IsFaulted, "LLM task should have failed.");
            Exception exception = pendingResponse.Exception.GetBaseException();
            Require(exception is LLMServiceException,
                $"Expected LLMServiceException, received {exception.GetType().Name}.");
            LLMServiceException llmException = (LLMServiceException)exception;
            Require(llmException.ErrorKind == expectedKind,
                $"Expected {expectedKind}, received {llmException.ErrorKind}.");
            if (expectedStatusCode != 0)
            {
                Require(llmException.StatusCode == expectedStatusCode,
                    $"Expected HTTP {expectedStatusCode}, received {llmException.StatusCode}.");
            }
        }

        private static void RestoreEnvironment()
        {
            cancellationSource?.Dispose();
            cancellationSource = null;
            Environment.SetEnvironmentVariable(
                TestApiKeyEnvironmentVariable,
                previousApiKey);
        }

        private static void FailSmokeTest(Exception exception)
        {
            Debug.LogException(exception);
            RestoreEnvironment();
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class RecordingTransport : ILLMHttpTransport
        {
            private readonly Queue<object> outcomes = new Queue<object>();

            public readonly List<CapturedRequest> Requests =
                new List<CapturedRequest>();

            public void EnqueueResponse(LLMHttpResponse response)
            {
                outcomes.Enqueue(response);
            }

            public void EnqueueException(Exception exception)
            {
                outcomes.Enqueue(exception);
            }

            public Task<LLMHttpResponse> PostJsonAsync(
                string url,
                string jsonBody,
                string bearerToken,
                int timeoutSeconds,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(new CapturedRequest(
                    url,
                    jsonBody,
                    bearerToken,
                    timeoutSeconds));
                Require(outcomes.Count > 0, "No mock transport outcome was queued.");

                object outcome = outcomes.Dequeue();
                if (outcome is Exception exception)
                {
                    return Task.FromException<LLMHttpResponse>(exception);
                }

                return Task.FromResult((LLMHttpResponse)outcome);
            }
        }

        private sealed class CapturedRequest
        {
            public string Url { get; }
            public string JsonBody { get; }
            public string BearerToken { get; }
            public int TimeoutSeconds { get; }

            public CapturedRequest(
                string url,
                string jsonBody,
                string bearerToken,
                int timeoutSeconds)
            {
                Url = url;
                JsonBody = jsonBody;
                BearerToken = bearerToken;
                TimeoutSeconds = timeoutSeconds;
            }
        }
    }
}
#endif
