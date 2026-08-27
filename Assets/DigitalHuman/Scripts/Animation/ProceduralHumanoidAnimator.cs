using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Additive procedural motion layer. The Vexa package ships no walk or run clip,
    /// so locomotion is synthesised here from the planner's root speed and turn rate
    /// and layered on top of whatever clip the Animator is playing.
    /// </summary>
    /// <remarks>
    /// Runs in <c>LateUpdate</c>, after the Animator has written its pose, and treats
    /// the current local rotation as the base to add onto. That makes it composable
    /// with real clips instead of replacing them. When no Animator drives the bones
    /// the captured rest pose is used as the base so offsets cannot accumulate.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ProceduralHumanoidAnimator : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private DigitalHumanController stateController;
        [SerializeField] private AgentMovement movement;
        [SerializeField] private MotionIntentAnimator intentAnimator;
        [SerializeField] private Animator animator;

        [Header("Bones")]
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform rightUpperLeg;

        [Header("Tuning")]
        [SerializeField, Min(0.01f)] private float blendSpeed = 4f;
        [SerializeField, Min(0.01f)] private float idleFrequency = 1.25f;
        [SerializeField, Min(0.01f)] private float talkingFrequency = 4.5f;
        [Tooltip("Stride cycles per second at referenceSpeed.")]
        [SerializeField, Min(0.01f)] private float walkingFrequency = 2.4f;
        [Tooltip("Root speed in m/s that maps to a full-amplitude stride.")]
        [SerializeField, Min(0.05f)] private float referenceSpeed = 1.4f;
        [Tooltip("Peak hip swing in degrees at referenceSpeed.")]
        [SerializeField, Range(0f, 60f)] private float strideAmplitude = 27f;
        [Tooltip("Peak shoulder counter-swing in degrees at referenceSpeed.")]
        [SerializeField, Range(0f, 60f)] private float armSwingAmplitude = 24f;
        [Tooltip("Degrees of inward lean per rad/s of turn rate.")]
        [SerializeField, Range(0f, 30f)] private float turnLeanPerRadian = 6f;

        private Vector3 rootRestPosition;
        private Quaternion torsoRest;
        private Quaternion headRest;
        private Quaternion leftArmRest;
        private Quaternion rightArmRest;
        private Quaternion leftLegRest;
        private Quaternion rightLegRest;
        private bool restPoseCaptured;

        private float stridePhase;
        private float talkingBlend;
        private float movingBlend;
        private float normalizedSpeed;
        private float smoothedTurnRate;
        private DigitalHumanState currentState = DigitalHumanState.Idle;

        public DigitalHumanState CurrentState => currentState;
        public float TalkingBlend => talkingBlend;
        public float MovingBlend => movingBlend;
        public float MotionMagnitude { get; private set; }
        public bool IsTalking => talkingBlend > 0.5f;
        public bool IsWalking => movingBlend > 0.5f;

        private void Awake()
        {
            ResolveReferences();
            CaptureRestPose();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            currentState = stateController != null
                ? stateController.CurrentState
                : DigitalHumanState.Idle;
            CaptureRestPose();
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreRestPose();
        }

        public void Configure(
            DigitalHumanController controller,
            Transform modelRoot,
            Transform torsoJoint,
            Transform headJoint,
            Transform leftArmJoint,
            Transform rightArmJoint)
        {
            Unsubscribe();
            stateController = controller;
            characterRoot = modelRoot;
            torso = torsoJoint;
            head = headJoint;
            leftUpperArm = leftArmJoint;
            rightUpperArm = rightArmJoint;
            restPoseCaptured = false;
            CaptureRestPose();
            if (isActiveAndEnabled)
            {
                Subscribe();
                currentState = stateController != null
                    ? stateController.CurrentState
                    : DigitalHumanState.Idle;
            }
        }

        public void ConfigureMovement(
            AgentMovement agentMovement,
            Transform leftLegJoint,
            Transform rightLegJoint)
        {
            movement = agentMovement;
            leftUpperLeg = leftLegJoint;
            rightUpperLeg = rightLegJoint;
            restPoseCaptured = false;
            CaptureRestPose();
        }

        public void ConfigureIntentSource(MotionIntentAnimator source, Animator targetAnimator)
        {
            intentAnimator = source;
            animator = targetAnimator;
        }

        private void LateUpdate()
        {
            if (!restPoseCaptured)
            {
                CaptureRestPose();
            }

            if (!restPoseCaptured)
            {
                return;
            }

            ReadMotionTargets(out float targetSpeed, out float targetTurnRate);
            float delta = Time.deltaTime;
            float blendStep = blendSpeed * delta;

            normalizedSpeed = Mathf.MoveTowards(
                normalizedSpeed,
                Mathf.Clamp01(targetSpeed / referenceSpeed),
                blendStep);
            smoothedTurnRate = Mathf.MoveTowards(smoothedTurnRate, targetTurnRate, blendStep * 4f);
            movingBlend = normalizedSpeed;
            talkingBlend = Mathf.MoveTowards(
                talkingBlend,
                currentState == DigitalHumanState.Speaking ? 1f : 0f,
                blendStep);

            // Phase is integrated rather than derived from Time.time so that a changing
            // stride frequency does not make the legs jump.
            stridePhase += Mathf.PI * 2f * walkingFrequency * normalizedSpeed * delta;
            if (stridePhase > Mathf.PI * 2f)
            {
                stridePhase -= Mathf.PI * 2f;
            }

            float idlePhase = Time.time * idleFrequency * Mathf.PI * 2f;
            float talkingPhase = Time.time * talkingFrequency * Mathf.PI * 2f;
            float breath = Mathf.Sin(idlePhase) * 0.008f;
            float idleSway = Mathf.Sin(idlePhase * 0.5f) * 1.2f;
            float talkPulse = Mathf.Sin(talkingPhase);
            float talkSecondary = Mathf.Sin(talkingPhase * 0.57f + 0.8f);
            float walkSwing = Mathf.Sin(stridePhase) * normalizedSpeed;
            float walkBounce = Mathf.Abs(Mathf.Sin(stridePhase * 2f)) * 0.018f * normalizedSpeed;
            float turnLean = smoothedTurnRate * turnLeanPerRadian;

            bool animatorDriven = IsAnimatorDriving();

            ApplyPosition(
                characterRoot,
                rootRestPosition,
                animatorDriven,
                Vector3.up * (breath + walkBounce + talkingBlend * 0.004f * talkPulse));

            ApplyRotation(torso, torsoRest, animatorDriven, new Vector3(
                breath * 85f + talkingBlend * talkSecondary * 1.8f,
                idleSway + talkingBlend * talkPulse * 2.2f,
                talkingBlend * talkSecondary * 1.2f - walkSwing * 1.5f - turnLean * 0.6f));

            ApplyRotation(head, headRest, animatorDriven, new Vector3(
                Mathf.Sin(idlePhase * 0.65f) * 0.8f + talkingBlend * talkPulse * 4.5f,
                Mathf.Sin(idlePhase * 0.37f) * 1.2f + talkingBlend * talkSecondary * 3f,
                talkingBlend * talkSecondary * 1.5f - turnLean * 0.3f));

            ApplyRotation(leftUpperArm, leftArmRest, animatorDriven, new Vector3(
                talkingBlend * (10f + talkPulse * 13f) - walkSwing * armSwingAmplitude,
                talkingBlend * -4f,
                talkingBlend * (-12f + talkSecondary * 5f)));

            ApplyRotation(rightUpperArm, rightArmRest, animatorDriven, new Vector3(
                talkingBlend * (-4f + talkSecondary * 11f) + walkSwing * armSwingAmplitude,
                talkingBlend * 4f,
                talkingBlend * (12f - talkPulse * 5f)));

            ApplyRotation(leftUpperLeg, leftLegRest, animatorDriven, new Vector3(
                walkSwing * strideAmplitude, 0f, 0f));

            ApplyRotation(rightUpperLeg, rightLegRest, animatorDriven, new Vector3(
                -walkSwing * strideAmplitude, 0f, 0f));

            MotionMagnitude = Mathf.Abs(breath * 85f) + Mathf.Abs(idleSway) +
                              normalizedSpeed *
                              (Mathf.Abs(walkSwing) * strideAmplitude * 2f + walkBounce * 40f) +
                              talkingBlend *
                              (Mathf.Abs(talkPulse) * 18f + Mathf.Abs(talkSecondary) * 12f);
        }

        private void ReadMotionTargets(out float targetSpeed, out float targetTurnRate)
        {
            if (intentAnimator != null)
            {
                targetSpeed = intentAnimator.Speed;
                targetTurnRate = intentAnimator.TurnRate;
                return;
            }

            // Legacy NavMesh path: no planner attached, so fall back to the agent's
            // own velocity and only a binary notion of moving.
            if (movement != null && movement.NavMeshAgent != null)
            {
                targetSpeed = movement.IsMoving
                    ? movement.NavMeshAgent.velocity.magnitude
                    : 0f;
                targetTurnRate = 0f;
                return;
            }

            targetSpeed = movement != null && movement.IsMoving ? referenceSpeed : 0f;
            targetTurnRate = 0f;
        }

        private bool IsAnimatorDriving()
        {
            return animator != null &&
                   animator.isActiveAndEnabled &&
                   animator.runtimeAnimatorController != null;
        }

        private static void ApplyRotation(
            Transform bone,
            Quaternion restRotation,
            bool animatorDriven,
            Vector3 eulerOffset)
        {
            if (bone == null)
            {
                return;
            }

            Quaternion baseRotation = animatorDriven ? bone.localRotation : restRotation;
            bone.localRotation = baseRotation * Quaternion.Euler(eulerOffset);
        }

        private static void ApplyPosition(
            Transform bone,
            Vector3 restPosition,
            bool animatorDriven,
            Vector3 offset)
        {
            if (bone == null)
            {
                return;
            }

            Vector3 basePosition = animatorDriven ? bone.localPosition : restPosition;
            bone.localPosition = basePosition + offset;
        }

        private void HandleStateChanged(DigitalHumanState _, DigitalHumanState nextState)
        {
            currentState = nextState;
        }

        private void Subscribe()
        {
            if (stateController == null)
            {
                return;
            }

            stateController.StateChanged -= HandleStateChanged;
            stateController.StateChanged += HandleStateChanged;
        }

        private void Unsubscribe()
        {
            if (stateController != null)
            {
                stateController.StateChanged -= HandleStateChanged;
            }
        }

        private void ResolveReferences()
        {
            if (intentAnimator == null)
            {
                intentAnimator = GetComponent<MotionIntentAnimator>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (movement == null)
            {
                movement = GetComponent<AgentMovement>();
            }

            if (stateController == null)
            {
                stateController = GetComponent<DigitalHumanController>();
            }
        }

        private void CaptureRestPose()
        {
            if (characterRoot == null || torso == null || head == null ||
                leftUpperArm == null || rightUpperArm == null)
            {
                restPoseCaptured = false;
                return;
            }

            rootRestPosition = characterRoot.localPosition;
            torsoRest = torso.localRotation;
            headRest = head.localRotation;
            leftArmRest = leftUpperArm.localRotation;
            rightArmRest = rightUpperArm.localRotation;
            leftLegRest = leftUpperLeg != null ? leftUpperLeg.localRotation : Quaternion.identity;
            rightLegRest = rightUpperLeg != null ? rightUpperLeg.localRotation : Quaternion.identity;
            restPoseCaptured = true;
        }

        private void RestoreRestPose()
        {
            if (!restPoseCaptured)
            {
                return;
            }

            characterRoot.localPosition = rootRestPosition;
            torso.localRotation = torsoRest;
            head.localRotation = headRest;
            leftUpperArm.localRotation = leftArmRest;
            rightUpperArm.localRotation = rightArmRest;
            if (leftUpperLeg != null)
            {
                leftUpperLeg.localRotation = leftLegRest;
            }

            if (rightUpperLeg != null)
            {
                rightUpperLeg.localRotation = rightLegRest;
            }
        }

        private void OnValidate()
        {
            blendSpeed = Mathf.Max(0.01f, blendSpeed);
            idleFrequency = Mathf.Max(0.01f, idleFrequency);
            talkingFrequency = Mathf.Max(0.01f, talkingFrequency);
            walkingFrequency = Mathf.Max(0.01f, walkingFrequency);
            referenceSpeed = Mathf.Max(0.05f, referenceSpeed);
        }
    }
}
