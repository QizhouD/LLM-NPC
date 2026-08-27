using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class OpenAILLMServiceTests
    {
        private GameObject host;
        private OpenAILLMService service;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("OpenAILLMService");
            service = host.AddComponent<OpenAILLMService>();
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
        public void BuildRequestJson_EmitsResponsesApiShape()
        {
            string json = service.BuildRequestJson(Conversation());

            Assert.That(json, Does.Contain("\"input\""));
            Assert.That(json, Does.Contain("\"max_output_tokens\""));
            Assert.That(json, Does.Contain("\"model\""));
            Assert.That(json, Does.Contain("You are EVA."));
            Assert.That(json, Does.Contain("Hello there."));
            Assert.That(json, Does.Contain("\"role\":\"system\""));
            Assert.That(json, Does.Contain("\"role\":\"user\""));
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
        public void ParseResponseJson_ExtractsOutputText()
        {
            const string json = @"{
                ""output"": [
                    {""content"": [{""type"": ""output_text"", ""text"": ""Nice to meet you.""}]}
                ]
            }";

            Assert.That(service.ParseResponseJson(json), Is.EqualTo("Nice to meet you."));
        }

        [Test]
        public void ParseResponseJson_SurfacesApiErrorAsException()
        {
            const string json = @"{""error"": {""message"": ""invalid api key""}}";

            LLMServiceException exception =
                Assert.Throws<LLMServiceException>(() => service.ParseResponseJson(json));
            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Api));
            Assert.That(exception.Message, Does.Contain("invalid api key"));
        }

        [Test]
        public void ParseResponseJson_SurfacesRefusalAsException()
        {
            const string json = @"{
                ""output"": [
                    {""content"": [{""type"": ""refusal"", ""refusal"": ""cannot comply""}]}
                ]
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
    }
}
