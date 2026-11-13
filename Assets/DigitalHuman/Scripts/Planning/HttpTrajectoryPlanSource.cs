using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>Where the most recent plan handed to the follower came from.</summary>
    public enum PlanOrigin
    {
        None,
        /// <summary>A sample from the learned model that passed every check.</summary>
        Model,
        /// <summary>The service rejected all its samples and answered with its own spring plan.</summary>
        ServiceFallback,
        /// <summary>The service was unreachable or returned an unusable response.</summary>
        LocalFallbackAfterError,
        /// <summary>The service's plan left the NavMesh, so it was discarded.</summary>
        LocalFallbackAfterNavMeshReject
    }

    /// <summary>
    /// Live plan source: posts the request to the planner repo's root-motion service
    /// (<c>python -m gtp.rootmotion.service</c>) and verifies the answer on the NavMesh.
    /// </summary>
    /// <remarks>
    /// The service only bounds how far a plan strays from the path it was sent. Whether
    /// that corridor is walkable is known here, not there, so every plan is re-checked
    /// segment by segment with <see cref="NavMeshPathPlanner.IsWalkable"/> before it is
    /// executed. A plan that fails, or a service that does not answer, falls through to
    /// the classical fallback source rather than stopping the character.
    /// </remarks>
    public sealed class HttpTrajectoryPlanSource : MonoBehaviour, ITrajectoryPlanSource
    {
        [Header("Service")]
        [Tooltip("Base URL of `python -m gtp.rootmotion.service`.")]
        [SerializeField] private string serviceUrl = "http://127.0.0.1:8787";
        [Tooltip("Give up on a request after this many milliseconds and use the fallback.")]
        [SerializeField, Min(1)] private int timeoutMilliseconds = DefaultTimeoutMilliseconds;
        [Tooltip("After a transport error or timeout, skip the service for this long and use the fallback directly.")]
        [SerializeField, Min(0f)] private float retryAfterSeconds = DefaultRetryAfterSeconds;
        [Tooltip("Samples the service draws per request before filtering.")]
        [SerializeField, Range(1, 64)] private int candidates = 16;

        [Header("Safety")]
        [Tooltip("Reject any plan with a segment that leaves the NavMesh.")]
        [SerializeField] private bool verifyOnNavMesh = true;
        [Tooltip("A component implementing ITrajectoryPlanSource, normally SpringPathPlanSource.")]
        [SerializeField] private MonoBehaviour fallbackSource;

        public const int DefaultTimeoutMilliseconds = 500;
        public const float DefaultRetryAfterSeconds = 5f;

        private IPlanHttpTransport transport;
        private ITrajectoryPlanSource fallback;
        private Func<double> clock;
        private double serviceSkippedUntil = double.NegativeInfinity;

        public string SourceName => $"http:{LastOrigin}";

        public PlanOrigin LastOrigin { get; private set; } = PlanOrigin.None;
        public int RequestCount { get; private set; }
        public int ModelPlanCount { get; private set; }
        public int ServiceFallbackCount { get; private set; }
        /// <summary>Requests the service failed (transport error, timeout or unusable response).</summary>
        public int ErrorFallbackCount { get; private set; }
        /// <summary>Requests answered by the fallback without contacting the service, inside the retry window.</summary>
        public int SkippedRequestCount { get; private set; }
        public int NavMeshRejectCount { get; private set; }
        public int TimeoutMilliseconds => timeoutMilliseconds;
        public float RetryAfterSeconds => retryAfterSeconds;
        /// <summary>True while a recent transport failure keeps requests away from the service.</summary>
        public bool IsServiceSkipped => Now() < serviceSkippedUntil;
        /// <summary>Planning time reported by the service for the last answered request.</summary>
        public float LastServiceLatencyMs { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Wires dependencies from code. Used by tests and the editor wiring tool.</summary>
        public void Configure(
            string url,
            IPlanHttpTransport httpTransport,
            ITrajectoryPlanSource fallbackPlanSource,
            bool verifyPlansOnNavMesh)
        {
            serviceUrl = url;
            transport = httpTransport;
            fallback = fallbackPlanSource;
            fallbackSource = fallbackPlanSource as MonoBehaviour;
            verifyOnNavMesh = verifyPlansOnNavMesh;
        }

        /// <summary>Sets the timeout and the retry window after a transport failure.</summary>
        public void ConfigureResilience(int timeoutMs, float retryAfter)
        {
            timeoutMilliseconds = Mathf.Max(1, timeoutMs);
            retryAfterSeconds = Mathf.Max(0f, retryAfter);
        }

        /// <summary>
        /// Replaces the clock (seconds) that times the retry window. Defaults to
        /// <see cref="Time.realtimeSinceStartupAsDouble"/>; tests inject their own.
        /// </summary>
        public void SetClock(Func<double> secondsNow)
        {
            clock = secondsNow;
        }

        public async Task<TrajectoryPlan> RequestPlanAsync(
            PlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ResolveDependencies();
            RequestCount++;

            if (IsServiceSkipped)
            {
                // A transport failure happened moments ago: asking again would only add the
                // timeout to every replan. Answer from the fallback until the window ends.
                SkippedRequestCount++;
                return await FallBackAsync(
                    request, PlanOrigin.LocalFallbackAfterError, null, cancellationToken);
            }

            string body;
            try
            {
                body = await transport.PostJsonAsync(
                    PlanEndpoint(),
                    PlanRequestJson.Build(request, candidates),
                    timeoutMilliseconds,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Unreachable, timed out or answered with an error status: stop asking for a while.
                LastError = exception.Message;
                ErrorFallbackCount++;
                serviceSkippedUntil = Now() + retryAfterSeconds;
                Debug.Log(
                    $"[Planning] Planner service unavailable; using the fallback for {retryAfterSeconds:F1} s ({LastError})",
                    this);
                return await FallBackAsync(
                    request, PlanOrigin.LocalFallbackAfterError, exception, cancellationToken);
            }

            TrajectoryPlan plan;
            PlanResponseMeta meta;
            try
            {
                plan = TrajectoryPlan.FromJson(body);
                meta = JsonUtility.FromJson<PlanResponseMeta>(body) ?? new PlanResponseMeta();
            }
            catch (Exception exception)
            {
                // The service answered, so it is reachable: fall back for this request only.
                LastError = exception.Message;
                ErrorFallbackCount++;
                return await FallBackAsync(
                    request, PlanOrigin.LocalFallbackAfterError, exception, cancellationToken);
            }

            LastServiceLatencyMs = meta.latency_ms;

            if (verifyOnNavMesh && !plan.IsFallback &&
                !NavMeshPathPlanner.IsWalkable(plan, out int blockedIndex))
            {
                LastError = $"Plan {plan.PlanId} leaves the NavMesh at point {blockedIndex}.";
                NavMeshRejectCount++;
                return await FallBackAsync(
                    request, PlanOrigin.LocalFallbackAfterNavMeshReject, null, cancellationToken);
            }

            // Clear the error before logging the origin change, so a switch back to the
            // service is not reported with the reason of the previous fallback.
            LastError = string.Empty;
            if (meta.source == "model")
            {
                ModelPlanCount++;
                SetOrigin(PlanOrigin.Model);
            }
            else
            {
                ServiceFallbackCount++;
                SetOrigin(PlanOrigin.ServiceFallback);
            }

            return plan;
        }

        private async Task<TrajectoryPlan> FallBackAsync(
            PlanRequest request,
            PlanOrigin origin,
            Exception cause,
            CancellationToken cancellationToken)
        {
            if (fallback == null)
            {
                // No fallback configured: surface the failure, as the interface asks.
                if (cause != null)
                {
                    throw new PlanServiceException($"{LastError} No fallback source is assigned.");
                }

                if (origin == PlanOrigin.LocalFallbackAfterError)
                {
                    throw new PlanServiceException(
                        $"Planner service skipped after a recent failure ({LastError}). No fallback source is assigned.");
                }

                throw new PlanServiceException(LastError);
            }

            TrajectoryPlan plan = await fallback.RequestPlanAsync(request, cancellationToken);
            SetOrigin(origin);
            return plan;
        }

        private void SetOrigin(PlanOrigin origin)
        {
            if (origin != LastOrigin)
            {
                // Logged on change only, so the Console shows each switch between the
                // model and a fallback without a line per replan.
                string reason = string.IsNullOrEmpty(LastError) ? string.Empty : $" ({LastError})";
                Debug.Log($"[Planning] Plan origin: {origin}{reason}", this);
            }

            LastOrigin = origin;
        }

        private string PlanEndpoint()
        {
            string trimmed = (serviceUrl ?? string.Empty).Trim().TrimEnd('/');
            if (trimmed.Length == 0)
            {
                throw new PlanServiceException("Planner service URL is empty.");
            }

            return trimmed + "/plan";
        }

        private double Now()
        {
            return clock != null ? clock() : Time.realtimeSinceStartupAsDouble;
        }

        private void ResolveDependencies()
        {
            if (transport == null)
            {
                transport = new UnityWebRequestPlanTransport();
            }

            if (fallback == null && fallbackSource is ITrajectoryPlanSource assigned)
            {
                fallback = assigned;
            }
        }

        private void OnValidate()
        {
            if (fallbackSource != null && !(fallbackSource is ITrajectoryPlanSource))
            {
                Debug.LogWarning(
                    $"{fallbackSource.GetType().Name} does not implement ITrajectoryPlanSource.",
                    this);
                fallbackSource = null;
            }

            if (fallbackSource == this)
            {
                Debug.LogWarning("HttpTrajectoryPlanSource cannot be its own fallback.", this);
                fallbackSource = null;
            }
        }
    }

    /// <summary>
    /// Fields the service adds on top of the base plan contract. Names are the wire format.
    /// </summary>
    [Serializable]
    public sealed class PlanResponseMeta
    {
        public string source = string.Empty;
        public int rejected_candidates;
        public int candidates;
        public float latency_ms;
    }
}
