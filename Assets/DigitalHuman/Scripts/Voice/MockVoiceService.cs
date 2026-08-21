using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class MockVoiceService : MonoBehaviour, IVoiceService
    {
        [SerializeField, Range(0.1f, 3f)] private float clipDuration = 0.5f;
        [SerializeField, Range(8000, 48000)] private int sampleRate = 22050;

        public string LastRequestedText { get; private set; }
        public int GenerationCount { get; private set; }

        public void Configure(float durationSeconds, int audioSampleRate = 22050)
        {
            clipDuration = Mathf.Clamp(durationSeconds, 0.1f, 3f);
            sampleRate = Mathf.Clamp(audioSampleRate, 8000, 48000);
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
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            LastRequestedText = trimmedText;
            GenerationCount++;
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(clipDuration * sampleRate));
            return AudioClip.Create(
                $"MockSpeech_{GenerationCount}",
                sampleCount,
                1,
                sampleRate,
                false);
        }

        private void OnValidate()
        {
            clipDuration = Mathf.Clamp(clipDuration, 0.1f, 3f);
            sampleRate = Mathf.Clamp(sampleRate, 8000, 48000);
        }
    }
}
