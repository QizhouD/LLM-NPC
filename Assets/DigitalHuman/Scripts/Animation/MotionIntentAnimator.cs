using System;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman
{
    /// <summary>
    /// Single owner of the Animator parameters. Everything upstream speaks in planner
    /// terms (intent plus root kinematics) and never touches the Animator directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionIntentAnimator : MonoBehaviour
    {
        public const string SpeedParameter = "Speed";
        public const string TurnRateParameter = "TurnRate";
        public const string MotionIntentParameter = "MotionIntent";
        public const string WaveParameter = "Wave";
        public const string JumpParameter = "Jump";
        public const string TalkingParameter = "IsTalking";

        public const string WaveStateName = "Wave";
        public const string JumpStateName = "Jump";

        private const float AgentMovingThreshold = 0.05f;

        private static readonly int SpeedHash = Animator.StringToHash(SpeedParameter);
        private static readonly int TurnRateHash = Animator.StringToHash(TurnRateParameter);
        private static readonly int MotionIntentHash = Animator.StringToHash(MotionIntentParameter);
        private static readonly int WaveHash = Animator.StringToHash(WaveParameter);
        private static readonly int JumpHash = Animator.StringToHash(JumpParameter);
        private static readonly int TalkingHash = Animator.StringToHash(TalkingParameter);
        private static readonly int WaveStateHash = Animator.StringToHash(WaveStateName);
        private static readonly int JumpStateHash = Animator.StringToHash(JumpStateName);

        [SerializeField] private Animator animator;
        [Tooltip("Seconds used to smooth Speed and TurnRate towards their targets.")]
        [SerializeField, Min(0f)] private float parameterDamping = 0.12f;
        [SerializeField] private DigitalHumanController stateController;
        [Tooltip("Supplies Speed and TurnRate whenever the planner is not driving.")]
        [SerializeField] private AgentMovement movement;
        [Tooltip("Seconds after the last planner sample before reading the NavMeshAgent instead.")]
        [SerializeField, Min(0.05f)] private float plannerSampleTimeout = 0.35f;

        private float speed;
        private float turnRate;
        private float targetSpeed;
        private float targetTurnRate;
        private float lastPlannerSampleTime = float.NegativeInfinity;
        private float previousYawDegrees;
        private bool previousYawValid;

        public MotionIntent CurrentIntent { get; private set; } = MotionIntent.Idle;
        public float Speed => speed;
        public float TurnRate => turnRate;
        public bool IsTalking { get; private set; }

        /// <summary>Whether the controller has a jump state to play.</summary>
        public bool CanJump => HasParameter(JumpHash, AnimatorControllerParameterType.Trigger);

        /// <summary>
        /// True from the moment a wave or jump is triggered until the Animator has left the
        /// gesture state, so a caller can wait for a one-shot to finish.
        /// </summary>
        public bool IsPlayingGesture
        {
            get
            {
                if (animator == null || !animator.isActiveAndEnabled ||
                    animator.runtimeAnimatorController == null)
                {
                    return false;
                }

                if (IsTriggerPending(WaveHash) || IsTriggerPending(JumpHash))
                {
                    return true;
                }

                if (IsGestureState(animator.GetCurrentAnimatorStateInfo(0)))
                {
                    return true;
                }

                return animator.IsInTransition(0) &&
                       IsGestureState(animator.GetNextAnimatorStateInfo(0));
            }
        }

        public event Action<MotionIntent, MotionIntent> IntentChanged;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (movement == null)
            {
                movement = GetComponent<AgentMovement>();
            }
        }

        private void OnEnable()
        {
            if (stateController != null)
            {
                stateController.StateChanged += HandleDialogueStateChanged;
                HandleDialogueStateChanged(stateController.CurrentState, stateController.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (stateController != null)
            {
                stateController.StateChanged -= HandleDialogueStateChanged;
            }
        }

        public void Configure(Animator targetAnimator, DigitalHumanController controller)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled && stateController != null)
            {
                stateController.StateChanged -= HandleDialogueStateChanged;
            }

            animator = targetAnimator;
            stateController = controller;

            if (wasEnabled && stateController != null)
            {
                stateController.StateChanged += HandleDialogueStateChanged;
            }
        }

        /// <summary>
        /// Feeds one planner sample. <paramref name="rootSpeed"/> is metres per second and
        /// <paramref name="rootTurnRate"/> is radians per second, matching the
        /// <c>speed</c> and <c>angular_velocity</c> channels of a TrajectoryPlan point.
        /// </summary>
        public void SetMotion(MotionIntent intent, float rootSpeed, float rootTurnRate)
        {
            targetSpeed = Mathf.Max(0f, rootSpeed);
            targetTurnRate = rootTurnRate;
            lastPlannerSampleTime = Time.time;
            SetIntent(intent);
        }

        public void SetIntent(MotionIntent intent)
        {
            if (CurrentIntent == intent)
            {
                return;
            }

            MotionIntent previous = CurrentIntent;
            CurrentIntent = intent;
            SetInteger(MotionIntentHash, (int)intent);
            if (intent == MotionIntent.Wave)
            {
                TriggerWave();
            }

            IntentChanged?.Invoke(previous, intent);
        }

        public void TriggerWave()
        {
            if (animator != null && HasParameter(WaveHash, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(WaveHash);
            }
        }

        public void TriggerJump()
        {
            if (animator != null && CanJump)
            {
                animator.SetTrigger(JumpHash);
            }
        }

        /// <summary>Brings the character to a stop without changing the intent.</summary>
        public void Halt()
        {
            targetSpeed = 0f;
            targetTurnRate = 0f;
        }

        private void Update()
        {
            if (Time.time - lastPlannerSampleTime > plannerSampleTimeout)
            {
                ReadKinematicsFromAgent();
            }

            if (parameterDamping <= 0f)
            {
                speed = targetSpeed;
                turnRate = targetTurnRate;
            }
            else
            {
                float step = Time.deltaTime / parameterDamping;
                speed = Mathf.Lerp(speed, targetSpeed, Mathf.Clamp01(step));
                turnRate = Mathf.Lerp(turnRate, targetTurnRate, Mathf.Clamp01(step));
            }

            SetFloat(SpeedHash, speed);
            SetFloat(TurnRateHash, turnRate);
        }

        /// <summary>
        /// Derives the root kinematics from the NavMeshAgent while no plan is being followed.
        /// The character can walk by either route, and without this the Animator's Speed sits
        /// at zero for the whole NavMesh path, which leaves both the locomotion blend and the
        /// procedural stride stuck in idle while the body slides across the floor.
        /// </summary>
        private void ReadKinematicsFromAgent()
        {
            if (movement == null)
            {
                movement = GetComponent<AgentMovement>();
            }

            NavMeshAgent agent = movement != null ? movement.NavMeshAgent : null;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                targetSpeed = 0f;
                targetTurnRate = 0f;
                previousYawValid = false;
                return;
            }

            targetSpeed = agent.velocity.magnitude;

            float yawDegrees = transform.eulerAngles.y;
            if (previousYawValid && Time.deltaTime > 0f)
            {
                float deltaDegrees = Mathf.DeltaAngle(previousYawDegrees, yawDegrees);
                targetTurnRate = deltaDegrees * Mathf.Deg2Rad / Time.deltaTime;
            }
            else
            {
                targetTurnRate = 0f;
            }

            previousYawDegrees = yawDegrees;
            previousYawValid = true;

            // Keep the reported intent honest about a body that is travelling, but only
            // promote and demote Idle so a gesture the dialogue layer asked for, such as a
            // wave, is not cancelled by the fact that the agent happens to be walking.
            if (targetSpeed > AgentMovingThreshold && CurrentIntent == MotionIntent.Idle)
            {
                SetIntent(MotionIntent.Walk);
            }
            else if (targetSpeed <= AgentMovingThreshold && CurrentIntent == MotionIntent.Walk)
            {
                SetIntent(MotionIntent.Idle);
            }
        }

        private void HandleDialogueStateChanged(DigitalHumanState _, DigitalHumanState next)
        {
            IsTalking = next == DigitalHumanState.Speaking;
            SetBool(TalkingHash, IsTalking);
        }

        private void SetFloat(int hash, float value)
        {
            if (animator != null && HasParameter(hash, AnimatorControllerParameterType.Float))
            {
                animator.SetFloat(hash, value);
            }
        }

        private void SetInteger(int hash, int value)
        {
            if (animator != null && HasParameter(hash, AnimatorControllerParameterType.Int))
            {
                animator.SetInteger(hash, value);
            }
        }

        private void SetBool(int hash, bool value)
        {
            if (animator != null && HasParameter(hash, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(hash, value);
            }
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == hash && parameters[i].type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsTriggerPending(int hash)
        {
            return HasParameter(hash, AnimatorControllerParameterType.Trigger) &&
                   animator.GetBool(hash);
        }

        private static bool IsGestureState(AnimatorStateInfo state)
        {
            return state.shortNameHash == WaveStateHash ||
                   state.shortNameHash == JumpStateHash;
        }

        private void OnValidate()
        {
            parameterDamping = Mathf.Max(0f, parameterDamping);
            plannerSampleTimeout = Mathf.Max(0.05f, plannerSampleTimeout);
        }
    }
}
