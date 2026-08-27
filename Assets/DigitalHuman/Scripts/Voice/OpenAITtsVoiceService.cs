using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DigitalHuman
{
    /// <summary>
    /// Speech from OpenAI's <c>/v1/audio/speech</c> endpoint, decoded straight into an
    /// <see cref="AudioClip"/>.
    /// </summary>
    /// <remarks>
    /// Requests <c>response_format: pcm</c>, which returns headerless signed 16-bit
    /// little-endian mono at 24 kHz. That avoids shipping an MP3 or WAV decoder: the bytes
    /// convert to a float buffer directly. Reuses the same environment-variable convention as
    /// <see cref="OpenAILLMService"/> so no key is ever stored in the project.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OpenAITtsVoiceService : MonoBehaviour, IVoiceService
    {
        public const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";

        /// <summary>Fixed by the API for <c>response_format: pcm</c>.</summary>
        private const int PcmSampleRate = 24000;
        private const int PcmChannels = 1;
        private const int BytesPerSample = 2;

        [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/speech";
        [SerializeField] private string model = "gpt-4o-mini-tts";
        [SerializeField] private string voice = "alloy";
        [Tooltip("Steers delivery. Supported by the gpt-4o-*-tts models.")]
        [TextArea(1, 3)]
        [SerializeField] private string instructions =
            "Speak calmly and warmly, like someone talking to a visitor standing in front of them.";
        [SerializeField] private string apiKeyEnvironmentVariable = DefaultApiKeyEnvironmentVariable;
        [SerializeField, Range(1, 120)] private int timeoutSeconds = 30;
        [Tooltip("Replies longer than this are truncated before being sent.")]
        [SerializeField, Range(64, 4096)] private int maxCharacters = 1000;

        public string LastRequestedText { get; private set; }
        public int GenerationCount { get; private set; }

        public void Configure(
            string ttsModel,
            string ttsVoice,
            string apiKeyEnvironmentName = DefaultApiKeyEnvironmentVariable)
        {
            model = string.IsNullOrWhiteSpace(ttsModel) ? "gpt-4o-mini-tts" : ttsModel.Trim();
            voice = string.IsNullOrWhiteSpace(ttsVoice) ? "alloy" : ttsVoice.Trim();
            apiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(apiKeyEnvironmentName)
                ? DefaultApiKeyEnvironmentVariable
                : apiKeyEnvironmentName.Trim();
        }

        public async Task<AudioClip> GenerateSpeechAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            string trimmedText = text != null ? text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(trimmedText))
            {
                throw new ArgumentException("Speech text cannot be empty.", nameof(text));
            }

            cancellationToken.ThrowIfCancellationRequested();

            string apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new LLMServiceException(
                    LLMErrorKind.Configuration,
                    $"Environment variable '{apiKeyEnvironmentVariable}' is not configured.");
            }

            if (trimmedText.Length > maxCharacters)
            {
                trimmedText = trimmedText.Substring(0, maxCharacters);
            }

            LastRequestedText = trimmedText;
            byte[] pcm = await RequestPcmAsync(trimmedText, apiKey.Trim(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            GenerationCount++;
            return CreateClipFromPcm16(pcm, $"OpenAISpeech_{GenerationCount}");
        }

        /// <summary>Builds the request body. Exposed so a test can assert its shape.</summary>
        public string BuildRequestJson(string text)
        {
            SpeechRequest request = new SpeechRequest
            {
                model = model,
                voice = voice,
                input = text ?? string.Empty,
                response_format = "pcm",
                instructions = instructions ?? string.Empty
            };
            return JsonUtility.ToJson(request);
        }

        /// <summary>
        /// Converts headerless signed 16-bit little-endian PCM into an AudioClip. Exposed so a
        /// test can verify the decode without hitting the network.
        /// </summary>
        public static AudioClip CreateClipFromPcm16(byte[] pcm, string clipName)
        {
            if (pcm == null || pcm.Length < BytesPerSample)
            {
                throw new LLMServiceException(
                    LLMErrorKind.EmptyResponse, "The speech response contained no audio.");
            }

            int sampleCount = pcm.Length / BytesPerSample;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short value = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = value / 32768f;
            }

            AudioClip clip = AudioClip.Create(
                clipName, sampleCount, PcmChannels, PcmSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private async Task<byte[]> RequestPcmAsync(
            string text,
            string apiKey,
            CancellationToken cancellationToken)
        {
            byte[] payload = Encoding.UTF8.GetBytes(BuildRequestJson(text));
            using (UnityWebRequest request =
                   new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, timeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                await AwaitRequestAsync(request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    throw new LLMServiceException(
                        LLMErrorKind.Network,
                        $"Speech request failed to reach the API: {request.error}");
                }

                if (request.responseCode < 200 || request.responseCode >= 300)
                {
                    // An error body is JSON even though a success body is binary.
                    string body = request.downloadHandler != null
                        ? request.downloadHandler.text
                        : string.Empty;
                    throw new LLMServiceException(
                        LLMErrorKind.Http,
                        $"Speech request returned {request.responseCode}: {body}",
                        request.responseCode);
                }

                return request.downloadHandler.data;
            }
        }

        private static async Task AwaitRequestAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);

            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                request.Abort();
                completion.TrySetCanceled();
            });

            try
            {
                await completion.Task;
            }
            finally
            {
                registration.Dispose();
            }
        }

        [Serializable]
        private struct SpeechRequest
        {
            public string model;
            public string voice;
            public string input;
            public string response_format;
            public string instructions;
        }
    }
}
