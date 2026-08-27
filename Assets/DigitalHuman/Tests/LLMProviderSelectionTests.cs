using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class LLMProviderSelectionTests
    {
        /// <summary>Stand-in backend that records the call instead of reaching the network.</summary>
        private sealed class StubBackend : MonoBehaviour, IConversationBackend
        {
            public string Reply = "stub reply. [intent:idle][emotion:neutral]";
            public int CallCount;

            public Task<string> SendMessageAsync(
                IReadOnlyList<ConversationMessage> messages,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                return Task.FromResult(Reply);
            }
        }

        private GameObject host;
        private AIConversationManager conversation;
        private StubBackend openAiStub;
        private StubBackend deepSeekStub;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Conversation");
            conversation = host.AddComponent<AIConversationManager>();
            openAiStub = host.AddComponent<StubBackend>();
            openAiStub.Reply = "openai says hello. [intent:idle][emotion:neutral]";
            deepSeekStub = host.AddComponent<StubBackend>();
            deepSeekStub.Reply = "deepseek says hello. [intent:wave][emotion:happy]";
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        [Test]
        public void DefaultProvider_IsOpenAI()
        {
            // The project shipped on OpenAI; adding DeepSeek must not silently move anyone.
            Assert.That(conversation.Provider, Is.EqualTo(LLMProvider.OpenAI));
        }

        [Test]
        public void OpenAIProvider_UsesTheOpenAISlot()
        {
            conversation.ConfigureProviders(openAiStub, deepSeekStub, LLMProvider.OpenAI);

            Assert.That(conversation.HasActiveBackend, Is.True);
        }

        [Test]
        public async Task OpenAIProvider_CallsOnlyTheOpenAIBackend()
        {
            conversation.ConfigureProviders(openAiStub, deepSeekStub, LLMProvider.OpenAI);

            string reply = await conversation.GetResponseAsync("hello");

            Assert.That(openAiStub.CallCount, Is.EqualTo(1));
            Assert.That(deepSeekStub.CallCount, Is.EqualTo(0));
            Assert.That(reply, Does.Contain("openai says hello"));
        }

        [Test]
        public async Task DeepSeekProvider_CallsOnlyTheDeepSeekBackend()
        {
            conversation.ConfigureProviders(openAiStub, deepSeekStub, LLMProvider.DeepSeek);

            string reply = await conversation.GetResponseAsync("hello");

            Assert.That(deepSeekStub.CallCount, Is.EqualTo(1));
            Assert.That(openAiStub.CallCount, Is.EqualTo(0));
            Assert.That(reply, Does.Contain("deepseek says hello"));
        }

        [Test]
        public async Task OfflineProvider_NeverCallsAnyBackend()
        {
            conversation.ConfigureProviders(openAiStub, deepSeekStub, LLMProvider.Offline);

            string reply = await conversation.GetResponseAsync("come over here");

            Assert.That(conversation.HasActiveBackend, Is.False);
            Assert.That(openAiStub.CallCount, Is.EqualTo(0));
            Assert.That(deepSeekStub.CallCount, Is.EqualTo(0));
            Assert.That(reply, Does.Contain("offline mode"));
        }

        [Test]
        public async Task DeepSeekProvider_WithoutAKeyFallsThroughToOfflineReplies()
        {
            DeepSeekLlmService deepSeek = host.AddComponent<DeepSeekLlmService>();
            deepSeek.Configure(
                DeepSeekLlmService.DefaultEndpoint,
                DeepSeekLlmService.DefaultModel,
                "DIGITAL_HUMAN_TEST_DEEPSEEK_KEY_THAT_IS_NOT_SET");
            conversation.ConfigureProviders(openAiStub, deepSeek, LLMProvider.DeepSeek);

            string reply = await conversation.GetResponseAsync("come over here");

            // Same degradation OpenAI already had: a missing key must not break the turn, and
            // the reply must be unmistakably not model output.
            Assert.That(reply, Does.Contain("offline mode"));
            ResponseAnnotation annotation = ResponseAnnotationParser.Parse(reply);
            Assert.That(annotation.HasIntent, Is.True);
            Assert.That(annotation.Intent, Is.EqualTo(MotionIntent.Walk));
        }

        [Test]
        public async Task SwitchingProvider_KeepsConversationHistory()
        {
            conversation.ConfigureProviders(openAiStub, deepSeekStub, LLMProvider.OpenAI);
            await conversation.GetResponseAsync("first turn");
            int afterFirstTurn = conversation.ConversationHistory.Count;

            conversation.SetProvider(LLMProvider.DeepSeek);
            await conversation.GetResponseAsync("second turn");

            Assert.That(conversation.Provider, Is.EqualTo(LLMProvider.DeepSeek));
            Assert.That(
                conversation.ConversationHistory.Count, Is.GreaterThan(afterFirstTurn));
            Assert.That(deepSeekStub.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void UnassignedSlot_ResolvesToOfflineRatherThanThrowing()
        {
            conversation.ConfigureProviders(openAiStub, null, LLMProvider.DeepSeek);

            Assert.That(conversation.HasActiveBackend, Is.False);
        }
    }
}
