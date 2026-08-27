using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Turns the behaviour tags on a model reply into animation and face state. This is the
    /// component that makes "LLM-driven digital human" literally true: without it the model
    /// only produces text.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResponseAnnotationDriver : MonoBehaviour
    {
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private MotionIntentAnimator intentAnimator;
        [SerializeField] private ProceduralFaceController faceController;
        [Tooltip("Seconds an annotation-driven gesture intent holds before releasing to idle.")]
        [SerializeField, Min(0.1f)] private float gestureHoldSeconds = 2.5f;
        [Tooltip("Log every parsed annotation. Useful when tuning the prompt.")]
        [SerializeField] private bool logAnnotations;

        private float gestureReleaseTime = float.NegativeInfinity;

        public MotionIntent LastIntent { get; private set; } = MotionIntent.Idle;
        public FaceEmotion LastEmotion { get; private set; } = FaceEmotion.Neutral;
        /// <summary>Count of replies that arrived without usable tags, for prompt tuning.</summary>
        public int UntaggedReplyCount { get; private set; }

        private void Awake()
        {
            if (dialogueController == null)
            {
                dialogueController = GetComponent<DialogueController>();
            }

            if (intentAnimator == null)
            {
                intentAnimator = GetComponent<MotionIntentAnimator>();
            }

            if (faceController == null)
            {
                faceController = GetComponent<ProceduralFaceController>();
            }
        }

        private void OnEnable()
        {
            if (dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived += HandleAnnotation;
            }
        }

        private void OnDisable()
        {
            if (dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived -= HandleAnnotation;
            }
        }

        public void Configure(
            DialogueController controller,
            MotionIntentAnimator animator,
            ProceduralFaceController face)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled && dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived -= HandleAnnotation;
            }

            dialogueController = controller;
            intentAnimator = animator;
            faceController = face;

            if (wasEnabled && dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived += HandleAnnotation;
            }
        }

        private void Update()
        {
            if (Time.time < gestureReleaseTime || LastIntent == MotionIntent.Idle)
            {
                return;
            }

            // A gesture is a one-shot, not a mode. Without this the character would stay in
            // wave forever after a single tagged reply.
            gestureReleaseTime = float.NegativeInfinity;
            LastIntent = MotionIntent.Idle;
            if (intentAnimator != null && !IsLocomotion(intentAnimator.CurrentIntent))
            {
                intentAnimator.SetIntent(MotionIntent.Idle);
            }
        }

        private void HandleAnnotation(ResponseAnnotation annotation)
        {
            LastEmotion = annotation.Emotion;
            if (faceController != null)
            {
                faceController.SetEmotion(annotation.Emotion);
                if (annotation.Emotion == FaceEmotion.Surprised)
                {
                    faceController.Blink();
                }
            }

            if (!annotation.HasIntent && !annotation.HasEmotion)
            {
                UntaggedReplyCount++;
            }

            if (logAnnotations)
            {
                Debug.Log(
                    $"[Annotation] intent={MotionIntents.ToName(annotation.Intent)}" +
                    $"({annotation.HasIntent}) " +
                    $"emotion={FaceEmotions.ToName(annotation.Emotion)}" +
                    $"({annotation.HasEmotion})",
                    this);
            }

            if (!annotation.HasIntent || intentAnimator == null)
            {
                return;
            }

            // Locomotion intents belong to the planner, which owns the root transform. A tag
            // must not teleport the character, so only gestures are applied here.
            if (IsLocomotion(annotation.Intent))
            {
                LastIntent = annotation.Intent;
                return;
            }

            LastIntent = annotation.Intent;
            intentAnimator.SetIntent(annotation.Intent);
            gestureReleaseTime = Time.time + gestureHoldSeconds;
        }

        private static bool IsLocomotion(MotionIntent intent)
        {
            return intent == MotionIntent.Walk || intent == MotionIntent.Turn;
        }
    }
}
