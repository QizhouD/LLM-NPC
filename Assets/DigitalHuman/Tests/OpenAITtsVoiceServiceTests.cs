using System;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class OpenAITtsVoiceServiceTests
    {
        private GameObject host;
        private OpenAITtsVoiceService service;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("OpenAITtsVoiceService");
            service = host.AddComponent<OpenAITtsVoiceService>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        [Test]
        public void BuildRequestJson_AsksForRawPcm()
        {
            string json = service.BuildRequestJson("Hello there.");

            // Raw PCM is what makes the decode a loop instead of a codec dependency.
            Assert.That(json, Does.Contain("\"response_format\":\"pcm\""));
            Assert.That(json, Does.Contain("\"input\":\"Hello there.\""));
            Assert.That(json, Does.Contain("\"model\""));
            Assert.That(json, Does.Contain("\"voice\""));
        }

        [Test]
        public void BuildRequestJson_EscapesTextTheModelMightProduce()
        {
            string json = service.BuildRequestJson("She said \"hi\"\nthen left.");

            Assert.That(json, Does.Contain("\\\"hi\\\""));
            Assert.That(json, Does.Contain("\\n"));
        }

        [Test]
        public void BuildRequestJson_UsesConfiguredModelAndVoice()
        {
            service.Configure("gpt-4o-audio-tts", "sage");

            string json = service.BuildRequestJson("Test.");

            Assert.That(json, Does.Contain("\"model\":\"gpt-4o-audio-tts\""));
            Assert.That(json, Does.Contain("\"voice\":\"sage\""));
        }

        [Test]
        public void CreateClipFromPcm16_DecodesLittleEndianSignedSamples()
        {
            // 0, full positive, full negative, half positive.
            byte[] pcm =
            {
                0x00, 0x00,
                0xFF, 0x7F,
                0x00, 0x80,
                0x00, 0x40
            };

            AudioClip clip = OpenAITtsVoiceService.CreateClipFromPcm16(pcm, "test");

            try
            {
                Assert.That(clip.samples, Is.EqualTo(4));
                Assert.That(clip.channels, Is.EqualTo(1));
                Assert.That(clip.frequency, Is.EqualTo(24000));

                float[] decoded = new float[4];
                clip.GetData(decoded, 0);
                Assert.That(decoded[0], Is.EqualTo(0f).Within(1e-4f));
                Assert.That(decoded[1], Is.EqualTo(1f).Within(1e-3f));
                Assert.That(decoded[2], Is.EqualTo(-1f).Within(1e-4f));
                Assert.That(decoded[3], Is.EqualTo(0.5f).Within(1e-3f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void CreateClipFromPcm16_RejectsEmptyAudio()
        {
            LLMServiceException exception = Assert.Throws<LLMServiceException>(
                () => OpenAITtsVoiceService.CreateClipFromPcm16(Array.Empty<byte>(), "test"));

            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.EmptyResponse));
        }

        [Test]
        public void GenerateSpeechAsync_RejectsBlankText()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await service.GenerateSpeechAsync("   "));
        }

        [Test]
        public void GenerateSpeechAsync_ReportsMissingApiKeyAsConfigurationError()
        {
            service.Configure(
                "gpt-4o-mini-tts", "alloy", "DIGITAL_HUMAN_TEST_KEY_THAT_IS_NOT_SET");

            LLMServiceException exception = Assert.ThrowsAsync<LLMServiceException>(
                async () => await service.GenerateSpeechAsync("Hello."));

            Assert.That(exception.ErrorKind, Is.EqualTo(LLMErrorKind.Configuration));
        }
    }
}
