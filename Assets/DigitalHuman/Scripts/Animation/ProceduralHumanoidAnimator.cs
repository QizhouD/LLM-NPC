using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class ProceduralHumanoidAnimator : MonoBehaviour
    {
        [SerializeField] private DigitalHumanController stateController;
        [SerializeField] private AgentMovement movement;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField, Min(0.01f)] private float blendSpeed = 4f;
        [SerializeField, Min(0.01f)] private float idleFrequency = 1.25f;
        [SerializeField, Min(0.01f)] private float talkingFrequency = 4.5f;
        [SerializeField, Min(0.01f)] private float walkingFrequency = 2.4f;

        private Vector3 rootBasePosition;
        private Quaternion torsoBaseRotation;
        private Quaternion headBaseRotation;
        private Quaternion leftArmBaseRotation;
        private Quaternion rightArmBaseRotation;
        private Quaternion leftLegBaseRotation;
        private Quaternion rightLegBaseRotation;
        private bool basePoseCaptured;
        private DigitalHumanState currentState = DigitalHumanState.Idle;
        private float talkingBlend;
        private float movingBlend;

        public DigitalHumanState CurrentState => currentState;
        public float TalkingBlend => talkingBlend;
        public float MovingBlend => movingBlend;
        public float MotionMagnitude { get; private set; }
        public bool IsTalking =>
            currentState == DigitalHumanState.Speaking && talkingBlend > 0.5f;
        public bool IsWalking => movingBlend > 0.5f;

        private void Awake()
        {
            CaptureBasePose();
        }

        private void OnEnable()
        {
            Subscribe();
            currentState = stateController != null
                ? stateController.CurrentState
                : DigitalHumanState.Idle;
            CaptureBasePose();
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreBasePose();
        }

        private void LateUpdate()
        {
            if (!basePoseCaptured)
            {
                CaptureBasePose();
            }

            if (!basePoseCaptured)
            {
                return;
            }

            float targetTalking = currentState == DigitalHumanState.Speaking ? 1f : 0f;
            float targetMoving = movement != null && movement.IsMoving ? 1f : 0f;
            talkingBlend = Mathf.MoveTowards(
                talkingBlend,
                targetTalking,
                blendSpeed * Time.deltaTime);
            movingBlend = Mathf.MoveTowards(
                movingBlend,
                targetMoving,
                blendSpeed * Time.deltaTime);

            float idlePhase = Time.time * idleFrequency * Mathf.PI * 2f;
            float talkingPhase = Time.time * talkingFrequency * Mathf.PI * 2f;
            float walkingPhase = Time.time * walkingFrequency * Mathf.PI * 2f;
            float breath = Mathf.Sin(idlePhase) * 0.008f;
            float idleSway = Mathf.Sin(idlePhase * 0.5f) * 1.2f;
            float talkPulse = Mathf.Sin(talkingPhase);
            float talkSecondary = Mathf.Sin(talkingPhase * 0.57f + 0.8f);
            float walkSwing = Mathf.Sin(walkingPhase) * movingBlend;
            float walkBounce = Mathf.Abs(Mathf.Sin(walkingPhase)) * 0.018f * movingBlend;

            characterRoot.localPosition = rootBasePosition +
                                          Vector3.up *
                                          (breath + walkBounce +
                                           talkingBlend * 0.004f * talkPulse);
            torso.localRotation = torsoBaseRotation * Quaternion.Euler(
                breath * 85f + talkingBlend * talkSecondary * 1.8f,
                idleSway + talkingBlend * talkPulse * 2.2f,
                talkingBlend * talkSecondary * 1.2f - walkSwing * 1.5f);
            head.localRotation = headBaseRotation * Quaternion.Euler(
                Mathf.Sin(idlePhase * 0.65f) * 0.8f +
                talkingBlend * talkPulse * 4.5f,
                Mathf.Sin(idlePhase * 0.37f) * 1.2f +
                talkingBlend * talkSecondary * 3f,
                talkingBlend * talkSecondary * 1.5f);
            leftUpperArm.localRotation = leftArmBaseRotation * Quaternion.Euler(
                talkingBlend * (10f + talkPulse * 13f) - walkSwing * 24f,
                talkingBlend * -4f,
                talkingBlend * (-12f + talkSecondary * 5f));
            rightUpperArm.localRotation = rightArmBaseRotation * Quaternion.Euler(
                talkingBlend * (-4f + talkSecondary * 11f) + walkSwing * 24f,
                talkingBlend * 4f,
                talkingBlend * (12f - talkPulse * 5f));

            if (leftUpperLeg != null)
            {
                leftUpperLeg.localRotation = leftLegBaseRotation *
                                             Quaternion.Euler(walkSwing * 27f, 0f, 0f);
            }

            if (rightUpperLeg != null)
            {
                rightUpperLeg.localRotation = rightLegBaseRotation *
                                              Quaternion.Euler(-walkSwing * 27f, 0f, 0f);
            }

            MotionMagnitude = Mathf.Abs(breath * 85f) + Mathf.Abs(idleSway) +
                              movingBlend * (Mathf.Abs(walkSwing) * 54f + walkBounce * 40f) +
                              talkingBlend *
                              (Mathf.Abs(talkPulse) * 18f +
                               Mathf.Abs(talkSecondary) * 12f);
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
            basePoseCaptured = false;
            CaptureBasePose();
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
            basePoseCaptured = false;
            CaptureBasePose();
        }

        private void HandleStateChanged(
            DigitalHumanState _,
            DigitalHumanState nextState)
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

        private void CaptureBasePose()
        {
            if (characterRoot == null || torso == null || head == null ||
                leftUpperArm == null || rightUpperArm == null)
            {
                basePoseCaptured = false;
                return;
            }

            rootBasePosition = characterRoot.localPosition;
            torsoBaseRotation = torso.localRotation;
            headBaseRotation = head.localRotation;
            leftArmBaseRotation = leftUpperArm.localRotation;
            rightArmBaseRotation = rightUpperArm.localRotation;
            leftLegBaseRotation = leftUpperLeg != null
                ? leftUpperLeg.localRotation
                : Quaternion.identity;
            rightLegBaseRotation = rightUpperLeg != null
                ? rightUpperLeg.localRotation
                : Quaternion.identity;
            basePoseCaptured = true;
        }

        private void RestoreBasePose()
        {
            if (!basePoseCaptured)
            {
                return;
            }

            characterRoot.localPosition = rootBasePosition;
            torso.localRotation = torsoBaseRotation;
            head.localRotation = headBaseRotation;
            leftUpperArm.localRotation = leftArmBaseRotation;
            rightUpperArm.localRotation = rightArmBaseRotation;
            if (leftUpperLeg != null)
            {
                leftUpperLeg.localRotation = leftLegBaseRotation;
            }

            if (rightUpperLeg != null)
            {
                rightUpperLeg.localRotation = rightLegBaseRotation;
            }
        }
    }
}
