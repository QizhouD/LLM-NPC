using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Drives blinking, gaze and eyebrow emotion purely from skeleton bones.
    /// </summary>
    /// <remarks>
    /// The Vexa mesh has no blend shapes and no jaw bone, so there is no lip sync to be had.
    /// What it does have is eight usable face bones: two eyeballs, two eyebrows and four
    /// eyelashes. None of them is touched by any imported clip, which makes them free for
    /// procedural control without fighting the Animator.
    ///
    /// Bone local axes on this rig are non-standard (bones extend along local X), so the
    /// rotation axes are exposed rather than assumed. Defaults are a starting point to be
    /// tuned in the Inspector, not a guarantee.
    ///
    /// Runs after <see cref="ProceduralHumanoidAnimator"/> so that head gaze composes on top
    /// of body motion instead of being overwritten by it.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class ProceduralFaceController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private DigitalHumanController stateController;
        [SerializeField] private LookAtController lookAtController;
        [Tooltip("Explicit gaze target. Falls back to the LookAtController's current target.")]
        [SerializeField] private Transform gazeTarget;
        [Tooltip("Root to search for face bones. Defaults to this transform.")]
        [SerializeField] private Transform skeletonRoot;

        [Header("Bones")]
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        [SerializeField] private Transform leftEyeBrow;
        [SerializeField] private Transform rightEyeBrow;
        [SerializeField] private Transform leftUpperLash;
        [SerializeField] private Transform leftLowerLash;
        [SerializeField] private Transform rightUpperLash;
        [SerializeField] private Transform rightLowerLash;
        [SerializeField] private Transform head;
        [SerializeField] private Transform neck;

        [Header("Blink")]
        [Tooltip("Mean seconds between blinks while idle.")]
        [SerializeField, Min(0.5f)] private float blinkIntervalMean = 4f;
        [Tooltip("Mean seconds between blinks while speaking. Blink rate rises with speech.")]
        [SerializeField, Min(0.5f)] private float blinkIntervalSpeaking = 2.4f;
        [SerializeField, Min(0.02f)] private float blinkDuration = 0.13f;
        [Tooltip("Euler offset in degrees applied to an upper lash at full closure.")]
        [SerializeField] private Vector3 upperLashCloseEuler = new Vector3(0f, 0f, -32f);
        [Tooltip("Euler offset in degrees applied to a lower lash at full closure.")]
        [SerializeField] private Vector3 lowerLashCloseEuler = new Vector3(0f, 0f, 14f);

        [Header("Gaze")]
        [SerializeField] private bool gazeEnabled = true;
        [Tooltip("Local axis of the eye bone that points out of the eye.")]
        [SerializeField] private Vector3 eyeForwardAxis = Vector3.forward;
        [SerializeField] private Vector3 eyeUpAxis = Vector3.up;
        [SerializeField, Range(0f, 60f)] private float maxEyeYaw = 32f;
        [SerializeField, Range(0f, 45f)] private float maxEyePitch = 22f;
        [SerializeField, Min(0.5f)] private float eyeTrackSpeed = 12f;
        [Tooltip("Degrees of head follow per degree of eye yaw.")]
        [SerializeField, Range(0f, 1f)] private float headFollowRatio = 0.35f;
        [SerializeField, Range(0f, 1f)] private float neckFollowRatio = 0.2f;
        [SerializeField, Range(0f, 45f)] private float maxHeadYaw = 40f;
        [SerializeField, Min(0.5f)] private float headTrackSpeed = 5f;

        [Header("Saccade")]
        [SerializeField, Range(0f, 6f)] private float saccadeAmplitude = 2.2f;
        [SerializeField, Min(0.05f)] private float saccadeIntervalMin = 0.18f;
        [SerializeField, Min(0.05f)] private float saccadeIntervalMax = 0.7f;

        [Header("Gaze aversion")]
        [Tooltip("Averting gaze while thinking is a documented correlate of cognitive load, " +
                 "not a stylistic flourish. See docs/embodiment-behaviours.md.")]
        [SerializeField] private bool avertGazeWhileThinking = true;
        [SerializeField] private Vector3 thinkingAversionEuler = new Vector3(-11f, -24f, 0f);
        [SerializeField, Min(0.1f)] private float aversionBlendSpeed = 3f;
        [Tooltip("Chance per second of a brief aversion while speaking.")]
        [SerializeField, Range(0f, 2f)] private float speakingAversionRate = 0.35f;
        [SerializeField, Min(0.1f)] private float speakingAversionDuration = 0.8f;

        [Header("Eyebrows")]
        [SerializeField, Min(0.1f)] private float browBlendSpeed = 6f;
        [SerializeField] private Vector3 browNeutralEuler = Vector3.zero;
        [SerializeField] private Vector3 browCuriousEuler = new Vector3(0f, 0f, 9f);
        [SerializeField] private Vector3 browHappyEuler = new Vector3(0f, 0f, 5f);
        [SerializeField] private Vector3 browConcernedEuler = new Vector3(0f, 0f, -8f);
        [SerializeField] private Vector3 browSurprisedEuler = new Vector3(0f, 0f, 16f);

        private Quaternion leftEyeRest;
        private Quaternion rightEyeRest;
        private Quaternion leftBrowRest;
        private Quaternion rightBrowRest;
        private Quaternion leftUpperLashRest;
        private Quaternion leftLowerLashRest;
        private Quaternion rightUpperLashRest;
        private Quaternion rightLowerLashRest;
        private bool restPoseCaptured;

        private float nextBlinkTime;
        private float blinkStartedAt = float.NegativeInfinity;
        private float blinkAmount;

        private Vector2 gazeAngles;
        private Vector2 saccadeOffset;
        private float nextSaccadeTime;
        private float headYaw;
        private float aversionBlend;
        private float speakingAversionUntil;
        private Vector3 currentBrowEuler;

        private DigitalHumanState currentState = DigitalHumanState.Idle;

        public FaceEmotion CurrentEmotion { get; private set; } = FaceEmotion.Neutral;
        public float BlinkAmount => blinkAmount;
        public bool IsAvertingGaze => aversionBlend > 0.5f;
        public Vector2 GazeAngles => gazeAngles;

        private void Awake()
        {
            if (skeletonRoot == null)
            {
                skeletonRoot = transform;
            }

            ResolveBones();
            CaptureRestPose();
            currentBrowEuler = browNeutralEuler;
            ScheduleNextBlink();
            ScheduleNextSaccade();
        }

        private void OnEnable()
        {
            if (stateController != null)
            {
                stateController.StateChanged += HandleStateChanged;
                currentState = stateController.CurrentState;
            }
        }

        private void OnDisable()
        {
            if (stateController != null)
            {
                stateController.StateChanged -= HandleStateChanged;
            }

            RestoreRestPose();
        }

        /// <summary>Sets the eyebrow emotion. Called by the response annotation parser.</summary>
        public void SetEmotion(FaceEmotion emotion)
        {
            CurrentEmotion = emotion;
        }

        public void SetGazeTarget(Transform target)
        {
            gazeTarget = target;
        }

        /// <summary>Forces a blink, for example on a surprise reaction.</summary>
        public void Blink()
        {
            blinkStartedAt = Time.time;
            ScheduleNextBlink();
        }

        private void LateUpdate()
        {
            if (!restPoseCaptured)
            {
                CaptureRestPose();
                if (!restPoseCaptured)
                {
                    return;
                }
            }

            float delta = Time.deltaTime;
            UpdateBlink();
            UpdateGaze(delta);
            UpdateBrows(delta);
        }

        private void UpdateBlink()
        {
            float now = Time.time;
            if (now >= nextBlinkTime)
            {
                blinkStartedAt = now;
                ScheduleNextBlink();
            }

            float sinceBlink = now - blinkStartedAt;
            if (sinceBlink < 0f || sinceBlink > blinkDuration)
            {
                blinkAmount = 0f;
            }
            else
            {
                // Closing is faster than opening, which is what real eyelids do.
                float phase = sinceBlink / blinkDuration;
                blinkAmount = phase < 0.35f
                    ? phase / 0.35f
                    : 1f - (phase - 0.35f) / 0.65f;
                blinkAmount = Mathf.Clamp01(blinkAmount);
            }

            ApplyLash(leftUpperLash, leftUpperLashRest, upperLashCloseEuler);
            ApplyLash(rightUpperLash, rightUpperLashRest, upperLashCloseEuler);
            ApplyLash(leftLowerLash, leftLowerLashRest, lowerLashCloseEuler);
            ApplyLash(rightLowerLash, rightLowerLashRest, lowerLashCloseEuler);
        }

        private void ApplyLash(Transform lash, Quaternion rest, Vector3 closeEuler)
        {
            if (lash == null)
            {
                return;
            }

            lash.localRotation = rest * Quaternion.Euler(closeEuler * blinkAmount);
        }

        private void UpdateGaze(float delta)
        {
            if (!gazeEnabled || head == null)
            {
                return;
            }

            Transform target = ResolveGazeTarget();
            Vector2 desired = target != null ? ComputeGazeAngles(target) : Vector2.zero;

            UpdateAversion(delta);
            if (aversionBlend > 0f)
            {
                Vector2 averted = new Vector2(thinkingAversionEuler.y, thinkingAversionEuler.x);
                desired = Vector2.Lerp(desired, averted, aversionBlend);
            }

            if (Time.time >= nextSaccadeTime)
            {
                saccadeOffset = new Vector2(
                    Random.Range(-saccadeAmplitude, saccadeAmplitude),
                    Random.Range(-saccadeAmplitude, saccadeAmplitude) * 0.6f);
                ScheduleNextSaccade();
            }

            desired += saccadeOffset;
            desired.x = Mathf.Clamp(desired.x, -maxEyeYaw, maxEyeYaw);
            desired.y = Mathf.Clamp(desired.y, -maxEyePitch, maxEyePitch);

            gazeAngles = Vector2.Lerp(
                gazeAngles, desired, Mathf.Clamp01(eyeTrackSpeed * delta));

            ApplyEye(leftEye, leftEyeRest);
            ApplyEye(rightEye, rightEyeRest);
            ApplyHeadFollow(delta);
        }

        private void ApplyEye(Transform eye, Quaternion rest)
        {
            if (eye == null)
            {
                return;
            }

            // Pitch about local X, yaw about local Y, expressed in the eye's rest frame.
            Quaternion offset = Quaternion.Euler(-gazeAngles.y, gazeAngles.x, 0f);
            Quaternion axisCorrection = Quaternion.Inverse(
                Quaternion.LookRotation(eyeForwardAxis, eyeUpAxis));
            eye.localRotation = rest * axisCorrection * offset * Quaternion.Inverse(axisCorrection);
        }

        private void ApplyHeadFollow(float delta)
        {
            float desiredHeadYaw = Mathf.Clamp(
                gazeAngles.x * headFollowRatio, -maxHeadYaw, maxHeadYaw);
            headYaw = Mathf.Lerp(headYaw, desiredHeadYaw, Mathf.Clamp01(headTrackSpeed * delta));

            if (head != null)
            {
                head.localRotation *= Quaternion.Euler(0f, headYaw, 0f);
            }

            if (neck != null && neckFollowRatio > 0f)
            {
                neck.localRotation *= Quaternion.Euler(0f, headYaw * neckFollowRatio, 0f);
            }
        }

        private void UpdateAversion(float delta)
        {
            bool shouldAvert = avertGazeWhileThinking &&
                               currentState == DigitalHumanState.Thinking;

            if (!shouldAvert && currentState == DigitalHumanState.Speaking)
            {
                if (Time.time < speakingAversionUntil)
                {
                    shouldAvert = true;
                }
                else if (Random.value < speakingAversionRate * delta)
                {
                    speakingAversionUntil = Time.time + speakingAversionDuration;
                    shouldAvert = true;
                }
            }

            aversionBlend = Mathf.MoveTowards(
                aversionBlend, shouldAvert ? 1f : 0f, aversionBlendSpeed * delta);
        }

        private Vector2 ComputeGazeAngles(Transform target)
        {
            Vector3 toTarget = target.position - head.position;
            if (toTarget.sqrMagnitude < 1e-6f)
            {
                return Vector2.zero;
            }

            // Measure the target against the body's forward, not the head's, so head follow
            // does not feed back into its own input.
            Transform reference = transform;
            Vector3 local = reference.InverseTransformDirection(toTarget.normalized);
            float yaw = Mathf.Atan2(local.x, Mathf.Max(0.05f, local.z)) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        private void UpdateBrows(float delta)
        {
            Vector3 targetEuler = BrowEulerFor(CurrentEmotion);

            // A surprise reaction reads better with the brows riding the blink.
            if (CurrentEmotion == FaceEmotion.Surprised)
            {
                targetEuler *= 1f - blinkAmount * 0.4f;
            }

            currentBrowEuler = Vector3.Lerp(
                currentBrowEuler, targetEuler, Mathf.Clamp01(browBlendSpeed * delta));

            if (leftEyeBrow != null)
            {
                leftEyeBrow.localRotation = leftBrowRest * Quaternion.Euler(currentBrowEuler);
            }

            if (rightEyeBrow != null)
            {
                // Mirrored so the pair moves symmetrically on a mirrored skeleton.
                Vector3 mirrored = new Vector3(
                    currentBrowEuler.x, -currentBrowEuler.y, -currentBrowEuler.z);
                rightEyeBrow.localRotation = rightBrowRest * Quaternion.Euler(mirrored);
            }
        }

        private Vector3 BrowEulerFor(FaceEmotion emotion)
        {
            switch (emotion)
            {
                case FaceEmotion.Curious:
                    return browCuriousEuler;
                case FaceEmotion.Happy:
                    return browHappyEuler;
                case FaceEmotion.Concerned:
                    return browConcernedEuler;
                case FaceEmotion.Surprised:
                    return browSurprisedEuler;
                default:
                    return browNeutralEuler;
            }
        }

        private Transform ResolveGazeTarget()
        {
            if (gazeTarget != null)
            {
                return gazeTarget;
            }

            return lookAtController != null ? lookAtController.CurrentTarget : null;
        }

        private void HandleStateChanged(DigitalHumanState _, DigitalHumanState next)
        {
            currentState = next;
            if (next == DigitalHumanState.Thinking)
            {
                speakingAversionUntil = 0f;
            }
        }

        private void ScheduleNextBlink()
        {
            float mean = currentState == DigitalHumanState.Speaking
                ? blinkIntervalSpeaking
                : blinkIntervalMean;

            // Exponential inter-arrival times, i.e. a Poisson blink process, which looks far
            // less mechanical than a fixed interval.
            float uniform = Mathf.Clamp(Random.value, 0.001f, 0.999f);
            float interval = -Mathf.Log(uniform) * mean;
            nextBlinkTime = Time.time + Mathf.Clamp(interval, blinkDuration * 2f, mean * 3f);
        }

        private void ScheduleNextSaccade()
        {
            nextSaccadeTime = Time.time + Random.Range(
                saccadeIntervalMin, Mathf.Max(saccadeIntervalMin, saccadeIntervalMax));
        }

        [ContextMenu("Resolve Face Bones")]
        private void ResolveBonesFromContextMenu()
        {
            skeletonRoot = skeletonRoot != null ? skeletonRoot : transform;
            ResolveBones();
            restPoseCaptured = false;
        }

        /// <summary>
        /// Binds sources and face bones by name, and reports which bones were found. Used by
        /// the editor wiring tool so the bindings are serialized and tunable in the Inspector
        /// rather than resolved silently at runtime.
        /// </summary>
        public bool AutoBind(
            Transform root,
            DigitalHumanController controller,
            LookAtController lookAt,
            Transform explicitGazeTarget,
            out string report)
        {
            skeletonRoot = root != null ? root : transform;
            stateController = controller;
            lookAtController = lookAt;
            if (explicitGazeTarget != null)
            {
                gazeTarget = explicitGazeTarget;
            }

            ResolveBones();
            restPoseCaptured = false;

            (string Name, Transform Bone)[] bones =
            {
                ("LEyeBall", leftEye),
                ("REyeBall", rightEye),
                ("LEyeBrow", leftEyeBrow),
                ("REyeBrow", rightEyeBrow),
                ("LUpperLash", leftUpperLash),
                ("LLowLash", leftLowerLash),
                ("RUpperLash", rightUpperLash),
                ("RLowLash", rightLowerLash),
                ("Head", head),
                ("Neck", neck)
            };

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            int missing = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].Bone == null)
                {
                    missing++;
                    builder.Append("MISSING ").Append(bones[i].Name).Append(' ');
                }
            }

            report = missing == 0
                ? $"all {bones.Length} face bones bound"
                : $"{bones.Length - missing}/{bones.Length} bones bound; {builder}".TrimEnd();
            return missing == 0;
        }

        private void ResolveBones()
        {
            if (stateController == null)
            {
                stateController = GetComponent<DigitalHumanController>();
            }

            if (lookAtController == null)
            {
                lookAtController = GetComponent<LookAtController>();
            }

            Transform root = skeletonRoot != null ? skeletonRoot : transform;
            leftEye = leftEye ?? FindBone(root, "Base HumanLEyeBall");
            rightEye = rightEye ?? FindBone(root, "Base HumanREyeBall");
            leftEyeBrow = leftEyeBrow ?? FindBone(root, "Base HumanLEyeBrow");
            rightEyeBrow = rightEyeBrow ?? FindBone(root, "Base HumanREyeBrow");
            leftUpperLash = leftUpperLash ?? FindBone(root, "Base HumanLUpperLash");
            leftLowerLash = leftLowerLash ?? FindBone(root, "Base HumanLLowLash");
            rightUpperLash = rightUpperLash ?? FindBone(root, "Base HumanRUpperLash");
            rightLowerLash = rightLowerLash ?? FindBone(root, "Base HumanRLowLash");
            head = head ?? FindBone(root, "Base HumanHead");
            neck = neck ?? FindBone(root, "Base HumanNeck");
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == boneName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void CaptureRestPose()
        {
            if (leftEye == null && rightEye == null &&
                leftUpperLash == null && rightUpperLash == null)
            {
                restPoseCaptured = false;
                return;
            }

            leftEyeRest = Rest(leftEye);
            rightEyeRest = Rest(rightEye);
            leftBrowRest = Rest(leftEyeBrow);
            rightBrowRest = Rest(rightEyeBrow);
            leftUpperLashRest = Rest(leftUpperLash);
            leftLowerLashRest = Rest(leftLowerLash);
            rightUpperLashRest = Rest(rightUpperLash);
            rightLowerLashRest = Rest(rightLowerLash);
            restPoseCaptured = true;
        }

        private static Quaternion Rest(Transform bone)
        {
            return bone != null ? bone.localRotation : Quaternion.identity;
        }

        private void RestoreRestPose()
        {
            if (!restPoseCaptured)
            {
                return;
            }

            Restore(leftEye, leftEyeRest);
            Restore(rightEye, rightEyeRest);
            Restore(leftEyeBrow, leftBrowRest);
            Restore(rightEyeBrow, rightBrowRest);
            Restore(leftUpperLash, leftUpperLashRest);
            Restore(leftLowerLash, leftLowerLashRest);
            Restore(rightUpperLash, rightUpperLashRest);
            Restore(rightLowerLash, rightLowerLashRest);
        }

        private static void Restore(Transform bone, Quaternion rest)
        {
            if (bone != null)
            {
                bone.localRotation = rest;
            }
        }

        private void OnValidate()
        {
            saccadeIntervalMax = Mathf.Max(saccadeIntervalMin, saccadeIntervalMax);
            blinkIntervalSpeaking = Mathf.Max(0.5f, blinkIntervalSpeaking);
            blinkIntervalMean = Mathf.Max(0.5f, blinkIntervalMean);
        }
    }
}
