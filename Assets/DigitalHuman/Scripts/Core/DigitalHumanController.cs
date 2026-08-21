using System;
using System.Collections;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DigitalHumanController : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionDetector interactionDetector;
        [SerializeField] private DialogueController dialogueController;
        [SerializeField, Min(0f)] private float speakingDuration = 0.65f;

        private Coroutine speakingRoutine;

        public DigitalHumanState CurrentState { get; private set; } = DigitalHumanState.Idle;

        public event Action<DigitalHumanState, DigitalHumanState> StateChanged;

        private void OnEnable()
        {
            SubscribeEvents();
            RefreshPresenceState();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            StopSpeakingRoutine();
        }

        public void Configure(
            PlayerInteractionDetector detector,
            DialogueController controller,
            float responseSpeakingDuration = 0.65f)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
            {
                UnsubscribeEvents();
            }

            interactionDetector = detector;
            dialogueController = controller;
            speakingDuration = Mathf.Max(0f, responseSpeakingDuration);

            if (wasEnabled)
            {
                SubscribeEvents();
                RefreshPresenceState();
            }
        }

        public void NotifySpeakingComplete()
        {
            StopSpeakingRoutine();
            if (dialogueController != null && dialogueController.IsDialogueActive)
            {
                SetState(DigitalHumanState.Listening);
            }
            else
            {
                RefreshPresenceState();
            }
        }

        private void SubscribeEvents()
        {
            if (interactionDetector != null)
            {
                interactionDetector.PlayerEnteredRange += HandlePlayerEnteredRange;
                interactionDetector.PlayerExitedRange += HandlePlayerExitedRange;
            }

            if (dialogueController != null)
            {
                dialogueController.DialogueStarted += HandleDialogueStarted;
                dialogueController.DialogueEnded += HandleDialogueEnded;
                dialogueController.ThinkingStarted += HandleThinkingStarted;
                dialogueController.NpcResponseReceived += HandleNpcResponseReceived;
            }
        }

        private void UnsubscribeEvents()
        {
            if (interactionDetector != null)
            {
                interactionDetector.PlayerEnteredRange -= HandlePlayerEnteredRange;
                interactionDetector.PlayerExitedRange -= HandlePlayerExitedRange;
            }

            if (dialogueController != null)
            {
                dialogueController.DialogueStarted -= HandleDialogueStarted;
                dialogueController.DialogueEnded -= HandleDialogueEnded;
                dialogueController.ThinkingStarted -= HandleThinkingStarted;
                dialogueController.NpcResponseReceived -= HandleNpcResponseReceived;
            }
        }

        private void HandlePlayerEnteredRange(Transform _)
        {
            if (dialogueController == null || !dialogueController.IsDialogueActive)
            {
                SetState(DigitalHumanState.PlayerNearby);
            }
        }

        private void HandlePlayerExitedRange(Transform _)
        {
            StopSpeakingRoutine();
            SetState(DigitalHumanState.Idle);
        }

        private void HandleDialogueStarted()
        {
            StopSpeakingRoutine();
            SetState(DigitalHumanState.Listening);
        }

        private void HandleDialogueEnded()
        {
            StopSpeakingRoutine();
            RefreshPresenceState();
        }

        private void HandleThinkingStarted()
        {
            StopSpeakingRoutine();
            SetState(DigitalHumanState.Thinking);
        }

        private void HandleNpcResponseReceived(string _)
        {
            StopSpeakingRoutine();
            SetState(DigitalHumanState.Speaking);
            if (speakingDuration <= 0f)
            {
                NotifySpeakingComplete();
                return;
            }

            speakingRoutine = StartCoroutine(CompleteSpeakingAfterDelay());
        }

        private IEnumerator CompleteSpeakingAfterDelay()
        {
            yield return new WaitForSeconds(speakingDuration);
            speakingRoutine = null;
            if (dialogueController != null && dialogueController.IsDialogueActive)
            {
                SetState(DigitalHumanState.Listening);
            }
            else
            {
                RefreshPresenceState();
            }
        }

        private void RefreshPresenceState()
        {
            SetState(interactionDetector != null && interactionDetector.CanInteract
                ? DigitalHumanState.PlayerNearby
                : DigitalHumanState.Idle);
        }

        private void SetState(DigitalHumanState nextState)
        {
            if (CurrentState == nextState)
            {
                return;
            }

            DigitalHumanState previousState = CurrentState;
            CurrentState = nextState;
            Debug.Log(
                $"[DigitalHuman] NPC state changed: {previousState} -> {nextState}",
                this);
            StateChanged?.Invoke(previousState, nextState);
        }

        private void StopSpeakingRoutine()
        {
            if (speakingRoutine == null)
            {
                return;
            }

            StopCoroutine(speakingRoutine);
            speakingRoutine = null;
        }

        private void OnValidate()
        {
            speakingDuration = Mathf.Max(0f, speakingDuration);
        }
    }
}
