using System.Threading;
using System.Threading.Tasks;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Where a <see cref="TrajectoryPlan"/> comes from. The embodiment layer depends only
    /// on this, so swapping the offline JSON source for a live <c>POST /plan</c> service is
    /// a one-component change.
    /// </summary>
    public interface ITrajectoryPlanSource
    {
        /// <summary>Human-readable source name, shown in the debug overlay.</summary>
        string SourceName { get; }

        /// <summary>
        /// Returns a plan for <paramref name="request"/>. Implementations should surface
        /// failures as exceptions rather than returning a fallback plan, so the caller can
        /// distinguish "planner refused" from "planner unreachable".
        /// </summary>
        Task<TrajectoryPlan> RequestPlanAsync(
            PlanRequest request,
            CancellationToken cancellationToken = default);
    }
}
