using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class DeepSeekLlmServiceTests
    {
        private GameObject host;
        private DeepSeekLlmService service;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("DeepSeekLlmService");
            service = host.AddComponent<DeepSeekLlmService>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        private static List<ConversationMessage> Conversation()
        {
            return new List<ConversationMessage>
            {
                new ConversationMessage("system", "You are EVA."),
                new ConversationMessage("user", "Hello there.")
            };
        }

        [Test]
        public void Defaults_TargetDeepSeekChatCompletions()
        {
            Assert.That(service.Endpoint, Is.EqualTo("https://api.deepseek.com/chat/completions"));
            Assert.That(service.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(service.ApiKeyEnvironmentVariable, Is.EqualTo("DEEPSEEK_API_KEY"));
        }

        [Test]
        public void BuildRequestJson_EmitsChatCompletionsShape()
        {
            string json = service.BuildRequestJson(Conversation());

            // Chat Completions, not Responses: "messages" rather than "input".
            Assert.That(json, Does.Contain("\"messages\""));
            Assert.That(json, Does.Not.Contain("\"input\""));
            Assert.That(json, Does.Contain("\"max_tokens\""));
            Assert.That(json, Does.Contain("\"model\":\"deepseek-chat\""));
            Assert.That(json, Does.Contain("\"role\":\"system\""));
            Assert.That(json, Does.Contain("\"role\":\"user\""));
            Assert.That(json, Does.Contain("You are EVA."));
            Assert.That(json, Does.Contain("Hello there."));
        }

        [Test]
        public void BuildRequestJson_DisablesStreaming()
        {
            // The annotation parser needs the whole reply, and the transport is not incremental.
            Assert.That(service.BuildRequestJson(Conversation()), Does.Contain("\"stream\":false"));
        }

        [Test]
        public void BuildRequestJson_PreservesMessageOrder()
        {
            string json = service.BuildRequestJson(Conversation());

            Assert.That(
                json.IndexOf("You are EVA.", StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("Hello there.", StringComparison.Ordinal)));
        }

        [Test]
        public void BuildRequestJson_UsesConfiguredModelAndEndpoint()
        {
            service.Configure(
                "https://api.deepseek.com/v1/chat/completions", "deepseek-reasoner");

            Assert.That(service.Endpoint, Is.EqualTo("https://api.deepseek.com/v1/chat/completions"));
            Assert.That(
                service.BuildRequestJson(Conversation()),
                Does.Contain("\"model\":\"deepseek-reasoner\""));
        }

        [Test]
        public void BuildRequestJson_RejectsEmptyConversation()
        {
            Assert.Throws<ArgumentException>(
                () => service.BuildRequestJson(new List<ConversationMessage>()));
            Assert.Throws<ArgumentException>(() => service.BuildRequestJson(null));
        }

        [Test]
        public void BuildRequestJson_RejectsMessageWithBlankContent()
        {
            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage("user", "   ")
            };

            Assert.Throws<ArgumentException>(() => service.BuildRequestJson(messages));
        }

        [Test]
        public void ParseResponseJson_ExtractsChoiceContent()
        {
            const string json = @"{
                ""id"": ""chat-1"",
                ""model"": ""deepseek-chat"",
                ""choices"": [
                    {""index"": 0, ""finish_reason"": ""stop"",
                     ""message"": {""role"": ""assistant"", ""content"": ""Nice to meet you.""}}
                ]
            }";

            Assert.That(service.ParseResponseJson(json), Is.EqualTo("Nice to meet you."));
        }

        [Test]
        public void ParseResponseJson_KeepsBehaviourTagsIntact()
        {
            // The tags must survive transport untouched; stripping is the parser's job.
            const string json = @"{
                ""choices"": [
                    {""message"": {""content"": ""On my way. [intent:walk][emotion:curious]""}}
                ]
            }";

            string text = service.ParseResponseJson(json);
            ResponseAnnotation annotation = ResponseAnnotationParser.Parse(text);

            Assert.That(annotation.Intent, Is.EqualTo(MotionIntent.Walk));
            Assert.That(annotation.Emotion, Is.EqualTo(FaceEmotion.Curious));
            Assert.That(annotation.Text, Is.EqualTo("On my way."));
        }

        [Test]
        public void ParseResponseJson_SurfacesApiErrorAsException()
        {
            const string json = @"{""error"": {""message"": ""Authentication Fails""}}";

            LLMServiceException exception =
                Assert.Throws<LLMServiceException>(() => service.ParseResponseJson(json));
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Api));
            Assert.That(exception.Message, Does.Contain("Authentication Fails"));
        }

        [Test]
        public void ParseResponseJson_SurfacesRefusalAsException()
        {
            const string json = @"{
                ""choices"": [{""message"": {""refusal"": ""cannot comply""}}]
            }";

            LLMServiceException exception =
                Assert.Throws<LLMServiceException>(() => service.ParseResponseJson(json));
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Api));
        }

        [Test]
        public void ParseResponseJson_RejectsEmptyBody()
        {
            LLMServiceException exception =
                Assert.Throws<LLMServiceException>(() => service.ParseResponseJson("   "));
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.InvalidJson));
        }

        [Test]
        public void ParseResponseJson_RejectsResponseWithNoContent()
        {
            const string json = @"{""choices"": [{""message"": {""content"": """"}}]}";

            LLMServiceException exception =
                Assert.Throws<LLMServiceException>(() => service.ParseResponseJson(json));
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.EmptyResponse));
        }

        [Test]
        public void SendMessageAsync_ReportsMissingKeyAsConfigurationError()
        {
            // A name that cannot be set in the environment, so the test does not depend on
            // whether the developer running it happens to have a real DeepSeek key.
            service.Configure(
                DeepSeekLlmService.DefaultEndpoint,
                DeepSeekLlmService.DefaultModel,
                "DIGITAL_HUMAN_TEST_DEEPSEEK_KEY_THAT_IS_NOT_SET");

            LLMServiceException exception = Assert.ThrowsAsync<LLMServiceException>(
                async () => await service.SendMessageAsync(Conversation()));

            // Configuration is what AIConversationManager degrades on; Api would surface as an
            // error to the player instead of falling through to the offline heuristic.
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Configuration));
        }

        [Test]
        public void SendMessageAsync_RejectsInvalidEndpointBeforeTouchingTheNetwork()
        {
            service.Configure("not-a-url", DeepSeekLlmService.DefaultModel);

            LLMServiceException exception = Assert.ThrowsAsync<LLMServiceException>(
                async () => await service.SendMessageAsync(Conversation()));

            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Configuration));
        }
    }
}
