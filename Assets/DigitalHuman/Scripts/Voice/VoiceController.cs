using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class VoiceController : MonoBehaviour
    {
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private DigitalHumanController stateController;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private MonoBehaviour voiceServiceComponent;

        private IVoiceService voiceService;
        private CancellationTokenSource speechCancellation;

        public bool IsSpeaking { get; private set; }

        public event Action<string> SpeechStarted;
        public event Action<string> SpeechCompleted;
        public event Action<Exception> SpeechFailed;

        private void Awake()
        {
            ResolveVoiceService();
        }

        private void OnEnable()
        {
            ResolveVoiceService();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopSpeech();
        }

        public void Configure(
            DialogueController controller,
            DigitalHumanController digitalHumanController,
            AudioSource targetAudioSource,
            MonoBehaviour serviceComponent)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
            {
                Unsubscribe();
            }

            dialogueController = controller;
            stateController = digitalHumanController;
            audioSource = targetAudioSource;
            voiceServiceComponent = serviceComponent;
            ResolveVoiceService();

            if (wasEnabled)
            {
                Subscribe();
            }
        }

        public void SetVoiceService(IVoiceService service)
        {
            voiceService = service;
        }

        public async Task SpeakAsync(string text)
        {
            StopSpeech();
            if (voiceService == null || audioSource == null)
            {
                stateController?.NotifySpeakingComplete();
                return;
            }

            CancellationTokenSource localCancellation = new CancellationTokenSource();
            speechCancellation = localCancellation;
            try
            {
                AudioClip clip = await voiceService.GenerateSpeechAsync(
                    text,
                    localCancellation.Token);
                localCancellation.Token.ThrowIfCancellationRequested();
                if (clip == null)
                {
                    throw new InvalidOperationException(
                        "Voice service returned no AudioClip.");
                }

                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.Play();
                IsSpeaking = true;
                SpeechStarted?.Invoke(text);
                Debug.Log("[DigitalHuman] Voice playback started.", this);

                float playbackDeadline = Time.unscaledTime + Mathf.Max(0.01f, clip.length);
                while (Time.unscaledTime < playbackDeadline)
                {
                    localCancellation.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                localCancellation.Token.ThrowIfCancellationRequested();
                audioSource.Stop();
                IsSpeaking = false;
                SpeechCompleted?.Invoke(text);
                stateController?.NotifySpeakingComplete();
                Debug.Log("[DigitalHuman] Voice playback completed.", this);
            }
            catch (OperationCanceledException) when (localCancellation.IsCancellationRequested)
            {
                // Closing dialogue or starting a new response intentionally cancels playback.
            }
            catch (Exception exception)
            {
                IsSpeaking = false;
                Debug.LogError(
                    $"[DigitalHuman] Voice playback failed: {exception}",
                    this);
                SpeechFailed?.Invoke(exception);
                stateController?.NotifySpeakingComplete();
            }
            finally
            {
                if (ReferenceEquals(speechCancellation, localCancellation))
                {
                    speechCancellation = null;
                }

                localCancellation.Dispose();
            }
        }

        public void StopSpeech()
        {
            CancellationTokenSource cancellation = speechCancellation;
            speechCancellation = null;
            if (cancellation != null)
            {
                cancellation.Cancel();
            }

            if (audioSource != null)
            {
                audioSource.Stop();
            }

            IsSpeaking = false;
        }

        private async void HandleNpcResponse(string response)
        {
            await SpeakAsync(response);
        }

        private void HandleDialogueEnded()
        {
            StopSpeech();
        }

        private void Subscribe()
        {
            if (dialogueController != null)
            {
                dialogueController.NpcResponseReceived += HandleNpcResponse;
                dialogueController.DialogueEnded += HandleDialogueEnded;
            }
        }

        private void Unsubscribe()
        {
            if (dialogueController != null)
            {
                dialogueController.NpcResponseReceived -= HandleNpcResponse;
                dialogueController.DialogueEnded -= HandleDialogueEnded;
            }
        }

        private void ResolveVoiceService()
        {
            voiceService = voiceServiceComponent as IVoiceService;
            if (voiceServiceComponent != null && voiceService == null)
            {
                Debug.LogError(
                    "[DigitalHuman] Assigned voice service does not implement IVoiceService.",
                    this);
            }
        }
    }
}
