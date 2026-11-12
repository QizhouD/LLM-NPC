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
        [SerializeField, Min(1)] private int timeoutSeconds = 2;
        [Tooltip("Samples the service draws per request before filtering.")]
        [SerializeField, Range(1, 64)] private int candidates = 16;

        [Header("Safety")]
        [Tooltip("Reject any plan with a segment that leaves the NavMesh.")]
        [SerializeField] private bool verifyOnNavMesh = true;
        [Tooltip("A component implementing ITrajectoryPlanSource, normally SpringPathPlanSource.")]
        [SerializeField] private MonoBehaviour fallbackSource;

        private IPlanHttpTransport transport;
        private ITrajectoryPlanSource fallback;

        public string SourceName => $"http:{LastOrigin}";

        public PlanOrigin LastOrigin { get; private set; } = PlanOrigin.None;
        public int RequestCount { get; private set; }
        public int ModelPlanCount { get; private set; }
        public int ServiceFallbackCount { get; private set; }
        public int ErrorFallbackCount { get; private set; }
        public int NavMeshRejectCount { get; private set; }
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

            TrajectoryPlan plan;
            PlanResponseMeta meta;
            try
            {
                string body = await transport.PostJsonAsync(
                    PlanEndpoint(),
                    PlanRequestJson.Build(request, candidates),
                    timeoutSeconds,
                    cancellationToken);
                plan = TrajectoryPlan.FromJson(body);
                meta = JsonUtility.FromJson<PlanResponseMeta>(body) ?? new PlanResponseMeta();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
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

            if (meta.source == "model")
            {
                ModelPlanCount++;
                LastOrigin = PlanOrigin.Model;
            }
            else
            {
                ServiceFallbackCount++;
                LastOrigin = PlanOrigin.ServiceFallback;
            }

            LastError = string.Empty;
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

                throw new PlanServiceException(LastError);
            }

            TrajectoryPlan plan = await fallback.RequestPlanAsync(request, cancellationToken);
            LastOrigin = origin;
            return plan;
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
