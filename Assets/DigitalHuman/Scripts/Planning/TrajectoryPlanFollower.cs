using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Executes a <see cref="TrajectoryPlan"/> by driving the root transform along the
    /// planned world-space points and feeding the resulting kinematics to
    /// <see cref="MotionIntentAnimator"/>.
    /// </summary>
    /// <remarks>
    /// This is the second of two mutually exclusive locomotion paths. The NavMesh path
    /// (<see cref="AgentMovement"/>) still backs pickup and drop commands; while a plan is
    /// executing the NavMeshAgent is suspended so the two cannot fight over the transform.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrajectoryPlanFollower : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("A component implementing ITrajectoryPlanSource.")]
        [SerializeField] private MonoBehaviour planSource;
        [SerializeField] private MotionIntentAnimator intentAnimator;
        [SerializeField] private AgentMovement navMeshMovement;
        [Tooltip("Defaults to this component's transform.")]
        [SerializeField] private Transform root;

        [Header("Execution")]
        [Tooltip("Seconds between rolling replans. The planner horizon is 3 s.")]
        [SerializeField, Min(0.1f)] private float replanInterval = 1f;
        [Tooltip("Stop executing once the goal is within this distance.")]
        [SerializeField, Min(0.01f)] private float arrivalTolerance = 0.25f;
        [Tooltip("Degrees per second cap when turning the root towards the planned yaw.")]
        [SerializeField, Min(1f)] private float maxTurnDegreesPerSecond = 360f;
        [Tooltip("Keep the root on the NavMesh surface while following a plan.")]
        [SerializeField] private bool snapToNavMesh = true;

        private ITrajectoryPlanSource source;
        private CancellationTokenSource requestCancellation;
        private TrajectoryPlan activePlan;
        private PlanGoal activeGoal;
        private IReadOnlyList<PlanObstacle> activeObstacles = Array.Empty<PlanObstacle>();
        private float planElapsed;
        private float timeSinceReplan;
        private bool requestInFlight;
        private bool navMeshSuspended;

        public bool IsFollowing { get; private set; }
        public TrajectoryPlan ActivePlan => activePlan;
        public float PlanElapsed => planElapsed;
        public string SourceName => source != null ? source.SourceName : "<none>";
        public IReadOnlyList<PlanObstacle> ActiveObstacles => activeObstacles;

        /// <summary>Raised when a plan comes back with <c>fallback: true</c>.</summary>
        public event Action<TrajectoryPlan> FallbackPlanReceived;
        public event Action<TrajectoryPlan> PlanStarted;
        public event Action<string> PlanRequestFailed;
        public event Action GoalReached;

        private void Awake()
        {
            if (root == null)
            {
                root = transform;
            }

            if (intentAnimator == null)
            {
                intentAnimator = GetComponent<MotionIntentAnimator>();
            }

            if (navMeshMovement == null)
            {
                navMeshMovement = GetComponent<AgentMovement>();
            }

            ResolveSource();
        }

        private void OnDisable()
        {
            CancelPendingRequest();
            StopFollowing();
        }

        /// <summary>Replaces the plan source at runtime, for example to switch to a live service.</summary>
        public void SetPlanSource(ITrajectoryPlanSource newSource)
        {
            source = newSource;
            planSource = newSource as MonoBehaviour;
        }

        /// <summary>
        /// Requests a plan towards <paramref name="goal"/> and begins executing it.
        /// Obstacles are passed through to the planner unchanged.
        /// </summary>
        public void BeginPlanTo(PlanGoal goal, IReadOnlyList<PlanObstacle> obstacles = null)
        {
            activeGoal = goal;
            activeObstacles = obstacles ?? Array.Empty<PlanObstacle>();
            timeSinceReplan = 0f;
            RequestPlan();
        }

        public void StopFollowing()
        {
            IsFollowing = false;
            activePlan = null;
            planElapsed = 0f;
            ResumeNavMesh();
            if (intentAnimator != null)
            {
                intentAnimator.Halt();
                intentAnimator.SetIntent(MotionIntent.Idle);
            }
        }

        private void Update()
        {
            if (!IsFollowing || activePlan == null)
            {
                return;
            }

            float delta = Time.deltaTime;
            planElapsed += delta;
            timeSinceReplan += delta;

            TrajectoryPoint sample = activePlan.Sample(planElapsed);
            MotionIntent intent = activePlan.IntentAt(planElapsed);

            ApplyRootMotion(sample, delta);

            if (intentAnimator != null)
            {
                intentAnimator.SetMotion(intent, sample.Speed, sample.AngularVelocity);
            }

            if (HasReachedGoal())
            {
                StopFollowing();
                GoalReached?.Invoke();
                return;
            }

            bool planExhausted = planElapsed >= activePlan.Horizon;
            if ((planExhausted || timeSinceReplan >= replanInterval) && !requestInFlight)
            {
                timeSinceReplan = 0f;
                RequestPlan();
            }
        }

        private void ApplyRootMotion(TrajectoryPoint sample, float delta)
        {
            Vector3 target = sample.Position;
            target.y = root.position.y;

            if (snapToNavMesh &&
                NavMesh.SamplePosition(target, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            {
                target = hit.position;
            }

            root.position = target;

            if (sample.Speed > 1e-3f || Mathf.Abs(sample.AngularVelocity) > 1e-3f)
            {
                root.rotation = Quaternion.RotateTowards(
                    root.rotation,
                    sample.Rotation,
                    maxTurnDegreesPerSecond * delta);
            }
        }

        private bool HasReachedGoal()
        {
            Vector3 delta = activeGoal.TargetPosition - root.position;
            delta.y = 0f;
            return delta.magnitude <= activeGoal.StopDistance + arrivalTolerance;
        }

        private void RequestPlan()
        {
            ResolveSource();
            if (source == null)
            {
                PlanRequestFailed?.Invoke("No ITrajectoryPlanSource is assigned.");
                return;
            }

            CancelPendingRequest();
            requestCancellation = new CancellationTokenSource();
            requestInFlight = true;

            PlanRequest request = BuildRequest();
            _ = ExecuteRequestAsync(request, requestCancellation.Token);
        }

        private async Task ExecuteRequestAsync(PlanRequest request, CancellationToken token)
        {
            try
            {
                TrajectoryPlan plan = await source.RequestPlanAsync(request, token);
                if (token.IsCancellationRequested || this == null || !isActiveAndEnabled)
                {
                    return;
                }

                AdoptPlan(plan);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request; nothing to report.
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                {
                    PlanRequestFailed?.Invoke(exception.Message);
                    Debug.LogError($"[Planning] Plan request failed: {exception.Message}", this);
                }
            }
            finally
            {
                requestInFlight = false;
            }
        }

        private void AdoptPlan(TrajectoryPlan plan)
        {
            if (plan.IsFallback)
            {
                // Every candidate was rejected by the planner's constraint filter. Standing
                // still is the honest behaviour; interpolating a rejected path is not.
                activePlan = null;
                IsFollowing = false;
                ResumeNavMesh();
                if (intentAnimator != null)
                {
                    intentAnimator.Halt();
                    intentAnimator.SetIntent(MotionIntent.Idle);
                }

                FallbackPlanReceived?.Invoke(plan);
                Debug.LogWarning(
                    $"[Planning] Plan {plan.PlanId} is a fallback (score {plan.Score:F1}); holding idle.",
                    this);
                return;
            }

            activePlan = plan;
            planElapsed = 0f;
            IsFollowing = true;
            SuspendNavMesh();
            PlanStarted?.Invoke(plan);
        }

        private PlanRequest BuildRequest()
        {
            float yaw = root.rotation.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 velocity = Vector3.zero;
            float angularVelocity = 0f;

            if (activePlan != null)
            {
                TrajectoryPoint current = activePlan.Sample(planElapsed);
                velocity = current.Velocity;
                angularVelocity = current.AngularVelocity;
            }

            return new PlanRequest(
                root.position,
                yaw,
                velocity,
                angularVelocity,
                intentAnimator != null ? intentAnimator.CurrentIntent : MotionIntent.Idle,
                activeGoal,
                activeObstacles,
                activeGoal.Instruction);
        }

        private void SuspendNavMesh()
        {
            if (navMeshSuspended || navMeshMovement == null)
            {
                return;
            }

            navMeshMovement.Stop();
            NavMeshAgent agent = navMeshMovement.NavMeshAgent;
            if (agent != null)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
            }

            navMeshSuspended = true;
        }

        private void ResumeNavMesh()
        {
            if (!navMeshSuspended || navMeshMovement == null)
            {
                navMeshSuspended = false;
                return;
            }

            NavMeshAgent agent = navMeshMovement.NavMeshAgent;
            if (agent != null)
            {
                agent.updatePosition = true;
                agent.updateRotation = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(root.position);
                }
            }

            navMeshSuspended = false;
        }

        private void CancelPendingRequest()
        {
            if (requestCancellation == null)
            {
                return;
            }

            requestCancellation.Cancel();
            requestCancellation.Dispose();
            requestCancellation = null;
            requestInFlight = false;
        }

        private void ResolveSource()
        {
            if (source != null)
            {
                return;
            }

            if (planSource is ITrajectoryPlanSource assigned)
            {
                source = assigned;
                return;
            }

            source = GetComponent<ITrajectoryPlanSource>();
        }

        private void OnValidate()
        {
            if (planSource != null && !(planSource is ITrajectoryPlanSource))
            {
                Debug.LogWarning(
                    $"{planSource.GetType().Name} does not implement ITrajectoryPlanSource.",
                    this);
                planSource = null;
            }
        }
    }
}
