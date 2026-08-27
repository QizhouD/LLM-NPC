#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Sends one real request to the selected chat backend and reports what came back,
    /// including whether the reply carried behaviour tags the parser can use.
    /// </summary>
    /// <remarks>
    /// The unit tests cover request and response shapes against fixtures; they cannot tell you
    /// that your key works, that the model honours the tag contract, or that the endpoint is
    /// reachable from here. This does, which makes it the thing to run before recording a demo.
    ///
    /// This costs real tokens. It is deliberately a single short turn.
    ///
    /// Start and Report are separate calls because the transport completes on the Editor update
    /// loop: blocking the main thread waiting for it would deadlock, so the request is kicked
    /// off and the result collected afterwards.
    /// </remarks>
    public static class LiveBackendCheck
    {
        private const string DefaultProbe = "Can you come over here and say hi?";

        private static string report = "not started";
        private static bool running;

        [MenuItem("Digital Human/Live Backend Check (real API call)")]
        public static string Start()
        {
            if (running)
            {
                return "already running; call Report()";
            }

            AIConversationManager conversation =
                UnityEngine.Object.FindAnyObjectByType<AIConversationManager>();
            if (conversation == null)
            {
                report = "no AIConversationManager in the open scene";
                return report;
            }

            IConversationBackend backend = ResolveSelectedBackend(conversation);
            if (backend == null)
            {
                report = $"provider is {conversation.Provider}, which has no live backend " +
                         "component; nothing to check";
                return report;
            }

            // The real persona prompt, so the check exercises the tag instructions the model
            // will actually receive rather than a hand-written stand-in.
            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage(
                    ConversationMessage.SystemRole, conversation.EffectiveSystemPrompt),
                new ConversationMessage(ConversationMessage.UserRole, DefaultProbe)
            };

            running = true;
            report = "running";
            _ = RunAsync(backend, conversation.Provider, messages);
            return $"started against {conversation.Provider}; call Report() in a few seconds";
        }

        [MenuItem("Digital Human/Live Backend Check Result")]
        public static string Report()
        {
            Debug.Log(report);
            return report;
        }

        private static IConversationBackend ResolveSelectedBackend(
            AIConversationManager conversation)
        {
            GameObject owner = conversation.gameObject;
            switch (conversation.Provider)
            {
                case LLMProvider.DeepSeek:
                    return owner.GetComponent<DeepSeekLlmService>();
                case LLMProvider.OpenAI:
                    return owner.GetComponent<OpenAILLMService>();
                default:
                    return null;
            }
        }

        private static async Task RunAsync(
            IConversationBackend backend,
            LLMProvider provider,
            List<ConversationMessage> messages)
        {
            StringBuilder result = new StringBuilder();
            result.Append("provider: ").AppendLine(provider.ToString());

            try
            {
                using (CancellationTokenSource timeout =
                       new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    string raw = await backend.SendMessageAsync(messages, timeout.Token);
                    ResponseAnnotation annotation = ResponseAnnotationParser.Parse(raw);

                    result.AppendLine("HTTP: ok");
                    result.Append("raw reply: ").AppendLine(raw);
                    result.Append("tags present: intent=").Append(annotation.HasIntent)
                        .Append(" emotion=").Append(annotation.HasEmotion).AppendLine();
                    result.Append("parsed intent: ").AppendLine(annotation.Intent.ToString());
                    result.Append("parsed emotion: ").AppendLine(annotation.Emotion.ToString());
                    result.Append("text shown to player: ").AppendLine(annotation.Text);
                }
            }
            catch (LLMServiceException exception)
            {
                result.Append("FAILED (").Append(exception.ErrorKind).Append("): ")
                    .AppendLine(exception.Message);
            }
            catch (OperationCanceledException)
            {
                result.AppendLine("FAILED: timed out after 60s");
            }
            catch (Exception exception)
            {
                result.Append("FAILED (unexpected ").Append(exception.GetType().Name)
                    .Append("): ").AppendLine(exception.Message);
            }
            finally
            {
                running = false;
            }

            report = result.ToString().TrimEnd();
            Debug.Log(report);
        }
    }
}
#endif
