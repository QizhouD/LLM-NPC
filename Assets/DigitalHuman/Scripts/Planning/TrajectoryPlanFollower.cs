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

        [Header("Root-motion planning")]
        [Tooltip("Send the NavMesh route with each request. The planner then decides how to move along it, not where to go.")]
        [SerializeField] private bool sendNavMeshPath = true;
        [Tooltip("Optional. Supplies the last two seconds of root motion so replans continue smoothly.")]
        [SerializeField] private RootHistoryRecorder historyRecorder;
        [SerializeField] private LocomotionGait gait = LocomotionGait.Walk;
        [Tooltip("How far, in metres, a plan may stray from the NavMesh route.")]
        [SerializeField, Min(0.05f)] private float corridorHalfWidth = PlanRequest.DefaultCorridorHalfWidth;

        /// <summary>
        /// A plan older than its latency allowance is discarded and re-requested at most this
        /// many times in a row; after that it is adopted with the capped start time, so a
        /// slow source cannot leave the character waiting forever.
        /// </summary>
        public const int MaxConsecutiveStaleDiscards = 1;

        private ITrajectoryPlanSource source;
        private CancellationTokenSource requestCancellation;
        private Func<float> clock;
        private int consecutiveStaleDiscards;
        private TrajectoryPlan activePlan;
        private PlanGoal activeGoal;
        private IReadOnlyList<PlanObstacle> activeObstacles = Array.Empty<PlanObstacle>();
        private float planElapsed;
        private float timeSinceReplan;
        private bool requestInFlight;
        private bool navMeshSuspended;
        private readonly List<Vector3> pathCorners = new List<Vector3>();
        private readonly List<RootSample> history = new List<RootSample>();

        public bool IsFollowing { get; private set; }
        public TrajectoryPlan ActivePlan => activePlan;
        public float PlanElapsed => planElapsed;
        public string SourceName => source != null ? source.SourceName : "<none>";
        /// <summary>The resolved plan source, or null before one is assigned.</summary>
        public ITrajectoryPlanSource PlanSource => source;
        /// <summary>Plans discarded because they arrived older than the latency allowance.</summary>
        public int StalePlanDiscardCount { get; private set; }
        public IReadOnlyList<PlanObstacle> ActiveObstacles => activeObstacles;
        /// <summary>NavMesh route sent with the most recent request. Empty when none was found.</summary>
        public IReadOnlyList<Vector3> LastPathCorners => pathCorners;

        public LocomotionGait Gait
        {
            get => gait;
            set => gait = value;
        }

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

            if (historyRecorder == null)
            {
                historyRecorder = GetComponent<RootHistoryRecorder>();
            }

            ResolveSource();
        }

        private void OnDisable()
        {
            CancelPendingRequest();
            StopFollowing();
        }

        /// <summary>Binds the follower's dependencies. Used by the editor wiring tool.</summary>
        public void Configure(
            MonoBehaviour sourceComponent,
            MotionIntentAnimator animator,
            AgentMovement movement)
        {
            planSource = sourceComponent;
            source = sourceComponent as ITrajectoryPlanSource;
            intentAnimator = animator;
            navMeshMovement = movement;
            if (root == null)
            {
                root = transform;
            }
        }

        /// <summary>Replaces the plan source at runtime, for example to switch to a live service.</summary>
        public void SetPlanSource(ITrajectoryPlanSource newSource)
        {
            source = newSource;
            planSource = newSource as MonoBehaviour;
        }

        /// <summary>
        /// Replaces the clock (seconds) used to age plans. Defaults to <see cref="Time.time"/>;
        /// tests inject their own.
        /// </summary>
        public void SetClock(Func<float> secondsNow)
        {
            clock = secondsNow;
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
            consecutiveStaleDiscards = 0;
            RequestPlan();
        }

        public void StopFollowing()
        {
            // A request still in flight would otherwise be adopted after the stop and move
            // the character again (seen as goals_reached=2 and a late 0.22 m step).
            CancelPendingRequest();
            consecutiveStaleDiscards = 0;
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
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Advances plan execution by <paramref name="delta"/> seconds. <see cref="Update"/>
        /// calls this every frame; it is public so tests can drive the follower directly.
        /// </summary>
        public void Advance(float delta)
        {
            if (!IsFollowing || activePlan == null)
            {
                return;
            }

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
            CancellationTokenSource cancellation = new CancellationTokenSource();
            requestCancellation = cancellation;
            requestInFlight = true;

            PlanRequest request = BuildRequest();
            _ = ExecuteRequestAsync(request, Now(), cancellation);
        }

        private async Task ExecuteRequestAsync(
            PlanRequest request, float requestedAt, CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                TrajectoryPlan plan = await source.RequestPlanAsync(request, token);
                if (token.IsCancellationRequested || this == null || !isActiveAndEnabled)
                {
                    return;
                }

                AdoptPlan(plan, requestedAt);
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
                // Only the current request owns the flag. A superseded one finishing late, or
                // this one having just started a re-request from AdoptPlan, must not clear it.
                if (ReferenceEquals(requestCancellation, cancellation))
                {
                    requestInFlight = false;
                }
            }
        }

        private void AdoptPlan(TrajectoryPlan plan, float requestedAt)
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

            // A plan describes motion from the moment it was requested. A remote planner
            // takes time to answer, and starting its plan at zero would pull the character
            // back by that latency on every replan.
            float age = Mathf.Max(0f, Now() - requestedAt);
            float allowance = plan.Horizon * 0.5f;
            if (age > allowance && consecutiveStaleDiscards < MaxConsecutiveStaleDiscards)
            {
                // Starting it at the capped time would jump the character back by the
                // difference. Keep executing the current plan and ask again from here.
                consecutiveStaleDiscards++;
                StalePlanDiscardCount++;
                timeSinceReplan = 0f;
                RequestPlan();
                return;
            }

            consecutiveStaleDiscards = 0;
            activePlan = plan;
            planElapsed = AlignStart(plan, Mathf.Min(age, allowance), root.position);
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

            // The request owns copies: a replan may rebuild these lists while an earlier
            // request is still awaiting its answer.
            Vector3[] corners = null;
            bool pathEndsAtGoal = true;
            if (sendNavMeshPath &&
                activeGoal.Behavior != MotionIntent.Turn &&
                NavMeshPathPlanner.TryBuildPath(root.position, activeGoal, pathCorners, out pathEndsAtGoal))
            {
                corners = pathCorners.ToArray();
            }
            else
            {
                pathCorners.Clear();
                pathEndsAtGoal = true;
            }

            RootSample[] recentHistory = null;
            if (historyRecorder != null && historyRecorder.isActiveAndEnabled)
            {
                historyRecorder.Snapshot(history);
                recentHistory = history.ToArray();
            }

            return new PlanRequest(
                root.position,
                yaw,
                velocity,
                angularVelocity,
                intentAnimator != null ? intentAnimator.CurrentIntent : MotionIntent.Idle,
                activeGoal,
                activeObstacles,
                activeGoal.Instruction,
                corners,
                pathEndsAtGoal,
                recentHistory,
                gait,
                corridorHalfWidth);
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

        private float Now()
        {
            return clock != null ? clock() : Time.time;
        }

        /// <summary>
        /// Chooses where in a newly adopted plan to start. Latency compensation assumes the
        /// character moved as the plan does while the request was in flight. That fails when
        /// it stood still waiting, or followed a slower plan, and starting at the compensated
        /// time then jumps it forward in one frame. So start at the time in
        /// [0, <paramref name="latest"/>] whose planned position is closest to where the
        /// character is; a character that did follow the plan gets the compensated time.
        /// Ties (for example a turn in place) keep the compensated time.
        /// </summary>
        public static float AlignStart(TrajectoryPlan plan, float latest, Vector3 position)
        {
            const float step = 0.02f;
            const float tieTolerance = 1e-3f;
            float best = latest;
            float bestDistance = PathGeometry.PlanarDistance(plan.Sample(latest).Position, position);
            for (float t = 0f; t < latest; t += step)
            {
                float distance = PathGeometry.PlanarDistance(plan.Sample(t).Position, position);
                if (distance < bestDistance - tieTolerance)
                {
                    best = t;
                    bestDistance = distance;
                }
            }

            return best;
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
